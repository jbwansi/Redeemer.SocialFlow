using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Redeemer.SocialFlow.Api.Controllers;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed class LinkedInOAuthTests : IAsyncLifetime
{
    private const string Secret = "fake-client-secret-private";
    private const string Token = "fake-access-token-private";
    private const string Code = "fake-code-private";
    private const string Prefix = "/api/dev/linkedin";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "linkedin-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new();
    private readonly Provider _provider = new();
    private readonly Logs _logs = new();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> Factory(string environment = "Development") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("LinkedIn:WorkerEnabled", "false").UseEnvironment(environment)
            .ConfigureLogging(logging => { logging.SetMinimumLevel(LogLevel.Trace); logging.AddProvider(_logs); })
            .ConfigureServices(services =>
            {
                services.Configure<LinkedInOptions>(options =>
                { options.ClientId = "fake-client-id"; options.ClientSecret = Secret; options.StorageDirectory = _directory; });
                services.AddSingleton<TimeProvider>(_clock);
                services.AddHttpClient<LinkedInOAuthClient>().ConfigurePrimaryHttpMessageHandler(() => new ProviderProxy(_provider)).RemoveAllLoggers();
            }));
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost:65474"), AllowAutoRedirect = false, HandleCookies = false });
    public Task InitializeAsync()
    { _factory = Factory(); _client = Client(_factory); return Task.CompletedTask; }
    public async Task DisposeAsync()
    {
        _client.Dispose(); await _factory.DisposeAsync();
        // Only this test's explicitly created temporary directory is removed.
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
    private async Task<(string State, string Cookie)> Connect()
    {
        using var response = await _client.GetAsync(Prefix + "/connect");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var uri = response.Headers.Location!;
        Assert.Equal("https://www.linkedin.com/oauth/v2/authorization", uri.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("openid profile w_member_social", query["scope"]);
        Assert.Equal(LinkedInOptions.Callback, query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("fake-client-id", query["client_id"]);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(43, query["state"].ToString().Length);
        return (query["state"].ToString(), cookie.Split(';')[0]);
    }
    private async Task<HttpResponseMessage> Callback(string state, string? cookie, string suffix = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Prefix + "/callback?state=" + Uri.EscapeDataString(state) + "&code=" + Code + suffix);
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        return await _client.SendAsync(request);
    }
    private void AssertNoSensitiveLogs()
    {
        foreach (var secret in new[] { Secret, Token, Code, "fake-refresh-private", "fake-id-token-private", "hostile-description-private" })
            Assert.DoesNotContain(_logs.Messages, value => value.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuccessfulFlow_IsServerSideEncryptedAndSurvivesRestartWithoutTokensInStatus()
    {
        var flow = await Connect();
        using var callback = await Callback(flow.State, flow.Cookie);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(Prefix + "/status", callback.Headers.Location!.ToString());
        Assert.Equal(2, _provider.Calls);
        var saved = await _factory.Services.GetRequiredService<LinkedInTokenStore>().ReadAsync(default);
        Assert.Equal(Token, saved!.AccessToken);
        Assert.Equal("member-sub", saved.Subject);
        Assert.Equal(_clock.Now.AddSeconds(3600), saved.ExpiresAt);
        foreach (var file in Directory.GetFiles(_directory, "*", SearchOption.AllDirectories))
        {
            var bytes = await File.ReadAllTextAsync(file);
            Assert.DoesNotContain(Token, bytes);
            Assert.DoesNotContain(Secret, bytes);
            Assert.DoesNotContain("member-sub", bytes);
        }
        await using var restarted = Factory();
        using var client = Client(restarted);
        var credentialProvider = new ProtectedLinkedInCredentialProvider(
            restarted.Services.GetRequiredService<LinkedInTokenStore>(),
            restarted.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<LinkedInOptions>>());
        var credential = await credentialProvider.GetAsync(default);
        Assert.Equal(Token, credential!.AccessToken);
        Assert.Equal(saved.Subject, credential.Subject);
        Assert.Equal(saved.ExpiresAt, credential.ExpiresAt);
        var otherApp = new ProtectedLinkedInCredentialProvider(restarted.Services.GetRequiredService<LinkedInTokenStore>(),
            Microsoft.Extensions.Options.Options.Create(new LinkedInOptions { ClientId = "other-app", ClientSecret = Secret }));
        Assert.Null(await otherApp.GetAsync(default));
        using var statusResponse = await client.GetAsync(Prefix + "/status");
        var status = (await statusResponse.Content.ReadFromJsonAsync<LinkedInConnectionStatus>())!;
        Assert.True(status.Connected);
        Assert.Equal("connected", status.State);
        Assert.Equal(saved.ExpiresAt, status.ExpiresAt);
        var body = await statusResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Token, body);
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("member-sub", body);
        Assert.True(statusResponse.Headers.CacheControl!.NoStore);
        _clock.Now = saved.ExpiresAt;
        var expired = (await client.GetFromJsonAsync<LinkedInConnectionStatus>(Prefix + "/status"))!;
        Assert.False(expired.Connected);
        Assert.Equal("expired", expired.State);
        Assert.Equal(2, _provider.Calls); // no refresh or remote status probe
        AssertNoSensitiveLogs();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("expired")]
    [InlineData("missing-cookie")]
    [InlineData("other-browser")]
    public async Task InvalidStateCannotExchangeCode(string kind)
    {
        var flow = await Connect();
        if (kind == "expired") _clock.Now = _clock.Now.AddMinutes(10);
        var cookie = kind == "missing-cookie" ? null : kind == "other-browser" ? (await Connect()).Cookie : flow.Cookie;
        using var response = await Callback(kind == "invalid" ? new string('x', 43) : flow.State, cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _provider.Calls);
        AssertNoSensitiveLogs();
    }

    [Fact]
    public async Task StateIsSingleUseEvenOnRefusal()
    {
        var flow = await Connect();
        using var denied = await Callback(flow.State, flow.Cookie, "&error=user_cancelled_authorize&error_description=hostile-description-private");
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.DoesNotContain("hostile-description-private", await denied.Content.ReadAsStringAsync());
        using var replay = await Callback(flow.State, flow.Cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(0, _provider.Calls);
        AssertNoSensitiveLogs();
    }

    [Fact]
    public async Task ConcurrentReplayExchangesAtMostOnce()
    {
        var flow = await Connect();
        var responses = await Task.WhenAll(Callback(flow.State, flow.Cookie), Callback(flow.State, flow.Cookie));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Redirect);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Equal(2, _provider.Calls);
        foreach (var response in responses) response.Dispose();
    }

    [Theory]
    [InlineData("token-error", "TokenExchange", "InvalidOperationException", "401", "LI_TOKEN_HTTP")]
    [InlineData("userinfo-error", "UserInfo", "InvalidOperationException", "401", "LI_USERINFO_HTTP")]
    [InlineData("malformed", "TokenExchange", "JsonReaderException", "200", "LI_TOKEN_RESPONSE")]
    [InlineData("no-sub", "UserInfo", "KeyNotFoundException", "200", "LI_USERINFO_RESPONSE")]
    [InlineData("expired-token", "TokenExchange", "InvalidOperationException", "200", "LI_TOKEN_RESPONSE")]
    [InlineData("missing-scope", "TokenExchange", "InvalidOperationException", "200", "LI_TOKEN_PERMISSION_MISSING")]
    [InlineData("timeout", "TokenExchange", "TaskCanceledException", "(null)", "LI_TOKEN_TRANSPORT")]
    public async Task ProviderFailuresAreGenericAndNeverStored(string scenario, string stage, string exceptionType, string status, string diagnosticCode)
    {
        _provider.Scenario = scenario;
        var flow = await Connect();
        using var response = await Callback(flow.State, flow.Cookie);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Token, body);
        Assert.DoesNotContain(Secret, body);
        Assert.False(File.Exists(Path.Combine(_directory, "connection.protected")));
        Assert.Equal($"LinkedIn OAuth: Stage={stage} ExceptionType={exceptionType} HttpStatus={status} DiagnosticCode={diagnosticCode}",
            Assert.Single(_logs.Messages, message => message.StartsWith("LinkedIn OAuth:")));
        Assert.DoesNotContain(diagnosticCode, body);
        Assert.DoesNotContain(flow.State, string.Join("", _logs.Messages));
        Assert.DoesNotContain(flow.Cookie, string.Join("", _logs.Messages));
        using var replay = await Callback(flow.State, flow.Cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        AssertNoSensitiveLogs();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task AllRoutesReturn404OutsideDevelopment(string environment)
    {
        await using var factory = Factory(environment);
        using var client = Client(factory);
        foreach (var path in new[] { "/connect", "/callback", "/status" })
        {
            using var response = await client.GetAsync(Prefix + path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(0, _provider.Calls);
    }

    [Fact]
    public async Task UnconfiguredConnectAndHttpAreRefused_EmptyStatusIsDisconnected()
    {
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<LinkedInOptions>(options => options.ClientSecret = "")));
        using var client = Client(factory);
        using var response = await client.GetAsync(Prefix + "/connect");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(_logs.Messages, message => message == "LinkedIn OAuth: Stage=Configuration ExceptionType=None HttpStatus=(null) DiagnosticCode=LI_CONFIG_MISSING");
        AssertNoSensitiveLogs();
        using var insecure = await _client.GetAsync("http://localhost:65475" + Prefix + "/connect");
        Assert.Equal(HttpStatusCode.BadRequest, insecure.StatusCode);
        Assert.Equal("disconnected", (await _client.GetFromJsonAsync<LinkedInConnectionStatus>(Prefix + "/status"))!.State);
    }

    [Fact]
    public async Task StorageFailureHasSafeDiagnosticAndGenericResponse()
    {
        // A directory at the target file path prevents atomic replacement.
        Directory.CreateDirectory(Path.Combine(_directory, "connection.protected"));
        var flow = await Connect();
        using var response = await Callback(flow.State, flow.Cookie);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("LinkedIn OAuth: Stage=ProtectedStorage ExceptionType=UnauthorizedAccessException HttpStatus=(null) DiagnosticCode=LI_STORAGE_SAVE",
            Assert.Single(_logs.Messages, message => message.StartsWith("LinkedIn OAuth:")));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("LI_STORAGE_SAVE", body);
        Assert.DoesNotContain(_directory, body);
        Assert.DoesNotContain(_directory, string.Join("", _logs.Messages));
        AssertNoSensitiveLogs();
        using var replay = await Callback(flow.State, flow.Cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(2, _provider.Calls);
    }

    [Theory]
    [InlineData("openid profile w_member_social")]
    [InlineData("openid%20profile%20w_member_social")]
    [InlineData("openid+profile+w_member_social")]
    [InlineData("openid,profile,w_member_social")]
    [InlineData("openid%2Cprofile%2Cw_member_social")]
    [InlineData(" profile,  w_member_social openid openid ")]
    public async Task SupportedScopesAreComparedAsExactPermissions(string scope)
    {
        _provider.Scope = scope;
        var flow = await Connect();
        using var response = await Callback(flow.State, flow.Cookie);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(2, _provider.Calls);
        AssertNoSensitiveLogs();
    }

    [Fact]
    public async Task AbsentScopeUsesRequestedScopePerOAuthWithoutIntrospection()
    {
        _provider.OmitScope = true;
        var flow = await Connect();
        using var response = await Callback(flow.State, flow.Cookie);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(2, _provider.Calls);
        Assert.Contains(_logs.Messages, message => message.EndsWith("DiagnosticCode=LI_TOKEN_SCOPE_ABSENT"));
        AssertNoSensitiveLogs();
    }

    [Theory]
    [InlineData(null, "LI_TOKEN_SCOPE_FORMAT")]
    [InlineData(42, "LI_TOKEN_SCOPE_FORMAT")]
    [InlineData("", "LI_TOKEN_SCOPE_FORMAT")]
    [InlineData("openid\tprofile w_member_social", "LI_TOKEN_SCOPE_FORMAT")]
    [InlineData("openid%ZZprofile w_member_social", "LI_TOKEN_SCOPE_FORMAT")]
    [InlineData("openid,,profile,w_member_social", "LI_TOKEN_SCOPE_FORMAT")]
    [InlineData("openid,profile", "LI_TOKEN_PERMISSION_MISSING")]
    [InlineData("openid profile w_member_social_extra", "LI_TOKEN_PERMISSION_MISSING")]
    [InlineData("openid profile W_MEMBER_SOCIAL", "LI_TOKEN_PERMISSION_MISSING")]
    public async Task InvalidOrInsufficientScopesNeverReachUserInfoOrStorage(object? scope, string diagnostic)
    {
        _provider.Scope = scope;
        var flow = await Connect();
        using var response = await Callback(flow.State, flow.Cookie);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, _provider.Calls);
        Assert.False(File.Exists(Path.Combine(_directory, "connection.protected")));
        Assert.Contains(_logs.Messages, message => message.EndsWith("DiagnosticCode=" + diagnostic));
        Assert.DoesNotContain(diagnostic, await response.Content.ReadAsStringAsync());
        AssertNoSensitiveLogs();
    }

    private sealed class Provider
    {
        private int _calls;
        public int Calls => _calls;
        public string Scenario { get; set; } = "success";
        public object? Scope { get; set; } = LinkedInOptions.Scopes;
        public bool OmitScope { get; set; }
        public async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            if (Scenario == "timeout") throw new TaskCanceledException(Secret);
            var exchange = request.RequestUri!.AbsolutePath.EndsWith("accessToken");
            if (exchange)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://www.linkedin.com/oauth/v2/accessToken", request.RequestUri.ToString());
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal(Code, form["code"]);
                Assert.Equal(Secret, form["client_secret"]);
                Assert.Equal(LinkedInOptions.Callback, form["redirect_uri"]);
                Assert.Equal("authorization_code", form["grant_type"]);
                Assert.DoesNotContain(Secret, request.RequestUri.ToString());
            }
            else
            {
                Assert.Equal("https://api.linkedin.com/v2/userinfo", request.RequestUri.ToString());
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.Equal(Token, request.Headers.Authorization.Parameter);
            }
            if (Scenario == "token-error" && exchange || Scenario == "userinfo-error" && !exchange)
                return new(HttpStatusCode.Unauthorized) { Content = new StringContent(Secret + Token) };
            var tokenResponse = new Dictionary<string, object?>
            {
                ["access_token"] = Token, ["expires_in"] = Scenario == "expired-token" ? 0 : 3600,
                ["refresh_token"] = "fake-refresh-private", ["id_token"] = "fake-id-token-private"
            };
            if (!OmitScope) tokenResponse["scope"] = Scenario == "missing-scope" ? "openid profile" : Scope;
            var json = exchange ? System.Text.Json.JsonSerializer.Serialize(tokenResponse) : Scenario == "no-sub" ? "{}" : "{\"sub\":\"member-sub\"}";
            return new(HttpStatusCode.OK) { Content = new StringContent(Scenario == "malformed" ? "{invalid" : json) };
        }
    }
    private sealed class ProviderProxy(Provider provider) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => provider.Send(request, cancellationToken);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Logs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLog(Messages);
        public void Dispose() { }
        private sealed class CapturingLog(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(formatter(state, exception) + exception);
        }
    }
}
