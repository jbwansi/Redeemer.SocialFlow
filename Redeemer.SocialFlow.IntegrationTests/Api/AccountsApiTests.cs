using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Infrastructure.Accounts;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed class AccountsApiTests : IAsyncLifetime
{
    private const string Email = "person@example.test";
    private const string Password = "Test-Password-947!";
    private readonly string path = Path.Combine(Path.GetTempPath(), $"socialflow-accounts-test-{Guid.NewGuid():N}.db");
    private readonly Mailbox mail = new();
    private readonly SessionClock clock = new();
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseSetting("Facebook:Enabled", "true")
            .UseSetting("Visuals:Enabled", "true")
            .UseSetting("ConnectionStrings:SocialFlow", "Data Source=:memory:")
            .UseSetting("ConnectionStrings:Accounts", $"Data Source={path};Pooling=False")
            .UseSetting("Authentication:PublicOrigin", "https://localhost:65474")
            .ConfigureServices(services =>
            {
                services.RemoveAll<IAccountEmailSender>();
                services.AddSingleton<IAccountEmailSender>(mail);
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
            }));
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost:65474"), AllowAutoRedirect = false });
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountDbContext>().Database.MigrateAsync();
    }

    private async Task<HttpResponseMessage> Post(string endpoint, object body, HttpClient? browser = null)
    {
        browser ??= client;
        var csrf = await browser.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/" + endpoint) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await browser.SendAsync(request);
    }
    private object Link(string purpose)
    {
        var link = new Uri(mail.Messages.Last(m => m.Purpose == purpose).Link);
        var query = QueryHelpers.ParseQuery(link.Fragment.Split('?', 2)[1]);
        return new { email = query["email"].ToString(), code = query["code"].ToString() };
    }
    private async Task Confirmed()
    {
        Assert.Equal(HttpStatusCode.Accepted, (await Post("register", new { email = Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post("confirm-email", Link("confirm-email"))).StatusCode);
    }
    private Task<HttpResponseMessage> Login(string password = Password) => Post("login", new { email = Email, password });

    [Fact]
    public async Task Registration_RequiresConfirmation_AndNeverEnumeratesAccounts()
    {
        var first = await Post("register", new { email = Email, password = Password });
        var duplicate = await Post("register", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await duplicate.Content.ReadAsStringAsync());
        Assert.Single(mail.Messages);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        var link = Link("confirm-email");
        Assert.Equal(HttpStatusCode.NoContent, (await Post("confirm-email", link)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("confirm-email", link)).StatusCode);
        var login = await Login();
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-SocialFlow.Session="));
        Assert.Contains("secure", cookie.ToLowerInvariant()); Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant()); Assert.DoesNotContain("domain=", cookie.ToLowerInvariant());
        var me = await client.GetFromJsonAsync<AccountProfile>("/api/auth/me");
        Assert.Equal(Email, me!.Email);
    }

    [Fact]
    public async Task Logout_RevokesReplayedSession()
    {
        await Confirmed();
        var login = await Login();
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-SocialFlow.Session=")).Split(';')[0];
        Assert.Equal(HttpStatusCode.NoContent, (await Post("logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        using var replay = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = client.BaseAddress!, HandleCookies = false });
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task PasswordReset_IsSingleUse_RevokesSessions_AndHidesUnknownEmails()
    {
        await Confirmed(); await Login();
        var known = await Post("forgot-password", new { email = Email });
        var unknown = await Post("forgot-password", new { email = "unknown@example.test" });
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        var data = JsonSerializer.SerializeToElement(Link("reset-password"));
        var reset = new { email = Email, code = data.GetProperty("code").GetString(), password = "Another-Password-481!" };
        Assert.Equal(HttpStatusCode.NoContent, (await Post("reset-password", reset)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("reset-password", reset)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login()).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Login(reset.password)).StatusCode);
    }

    [Theory]
    [InlineData("/api/posts")]
    [InlineData("/api/posts/trash")]
    [InlineData("/api/dev/knowledge/documents")]
    [InlineData("/api/dev/linkedin/status")]
    [InlineData("/api/dev/facebook/runtime")]
    [InlineData("/api/visual-identities")]
    [InlineData("/api/posts/11111111-1111-1111-1111-111111111111/visuals")]
    [InlineData("/api/posts/11111111-1111-1111-1111-111111111111/visuals/22222222-2222-2222-2222-222222222222")]
    public async Task OrdinaryConfirmedAccount_CannotReadHistoricalData(string route)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        await Confirmed(); await Login();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/posts", new { title = "Forbidden", platform = 0 })).StatusCode);
        foreach (var action in new[] { "/api/dev/ai/generate", "/api/dev/linkedin/connect-ticket", "/api/dev/facebook/connect", "/api/posts/generate-draft" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(action, new { })).StatusCode);
    }

    [Fact]
    public async Task Csrf_IsRequired_AndBoundToBrowser_AndHttpsIsRequired()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email = Email, password = Password })).StatusCode);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = client.BaseAddress! });
        other.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/auth/register", new { email = Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("http://localhost/api/auth/csrf")).StatusCode);
        Assert.Empty(mail.Messages);
    }

    [Fact]
    public async Task Lockout_AfterFiveFailedPasswords_RejectsCorrectPassword()
    {
        await Confirmed();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Login("Incorrect-password!")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login()).StatusCode);
    }

    [Fact]
    public async Task AccountRateLimit_StopsRepeatedRequests()
    {
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.Accepted, (await Post("forgot-password", new { email = Email })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post("forgot-password", new { email = Email })).StatusCode);
    }

    [Fact]
    public async Task InvalidTokensAndWeakPasswords_AreRejected()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("register", new { email = Email, password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("register", new { email = Email, password = "Abcdefghij1é" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("confirm-email", new { email = Email, code = "invalid!" })).StatusCode);
        Assert.Empty(mail.Messages);
    }

    [Fact]
    public async Task Session_ExpiresAfterEightHours_WithoutSlidingRenewal()
    {
        await Confirmed(); await Login();
        clock.Now = clock.Now.AddHours(7);
        var active = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.False(active.Headers.Contains("Set-Cookie"));
        clock.Now = clock.Now.AddHours(2);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ResendConfirmation_AndLoginErrors_DoNotDiscloseExistence()
    {
        await Post("register", new { email = Email, password = Password });
        var resend = await Post("resend-confirmation", new { email = Email });
        var unknown = await Post("resend-confirmation", new { email = "unknown@example.test" });
        Assert.Equal(await resend.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(2, mail.Messages.Count);
        var unconfirmed = await Login();
        var missing = await Post("login", new { email = "unknown@example.test", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        var first = await unconfirmed.Content.ReadFromJsonAsync<JsonElement>();
        var second = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first.GetProperty("title").GetString(), second.GetProperty("title").GetString());
    }

    public async Task DisposeAsync()
    {
        client.Dispose(); await factory.DisposeAsync();
        if (File.Exists(path)) File.Delete(path);
    }
    private sealed class Mailbox : IAccountEmailSender
    {
        public List<(string Purpose, string Link)> Messages { get; } = [];
        public Task SendAsync(string email, string purpose, string link, CancellationToken token)
        { Messages.Add((purpose, link)); return Task.CompletedTask; }
    }
    private sealed class SessionClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
