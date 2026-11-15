using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Infrastructure.Social;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests;

public sealed class FacebookWorkspaceOAuthTests
{
    private const string Prefix = "SocialConnections:Providers:Facebook:";
    private static IConfigurationRoot Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Authentication:PublicOrigin"] = "https://localhost:65474", [Prefix+"Enabled"] = "true",
        [Prefix+"ClientId"] = "test-app", [Prefix+"ClientSecret"] = "test-secret", [Prefix+"ApiVersion"] = "v26.0",
        [Prefix+"Scopes"] = "pages_show_list pages_read_engagement pages_manage_posts", [Prefix+"PermissionsApproved"] = "false"
    }).Build();

    [Fact]
    public void ConfigurationIsNotConsent_LegacyReferencesAreExplicitAndPairsNeverMix()
    {
        var config = Config();
        using var provider = new FacebookWorkspaceOAuthProvider(new(), config, TimeProvider.System);
        Assert.True(provider.Available);
        config[Prefix+"ClientId"] = null; config[Prefix+"ClientSecret"] = null;
        config["Facebook:AppId"] = "historical-app"; config["Facebook:AppSecret"] = "historical-secret";
        config["Facebook:LoginConfigId"] = "123456";
        Assert.False(provider.Available);
        config[Prefix+"UseLegacyClientCredentials"] = "true";
        Assert.True(provider.Available);
        Assert.DoesNotContain("config_id", provider.AuthorizationUrl("state", null));
        config[Prefix+"UseLegacyLoginConfigId"] = "true";
        var query = QueryHelpers.ParseQuery(new Uri(provider.AuthorizationUrl("state", null)).Query);
        Assert.Equal("123456", query["config_id"]); Assert.False(query.ContainsKey("scope"));
        Assert.Equal("https://localhost:65474/api/social-oauth/1/callback", query["redirect_uri"]);
        Assert.Equal("state", query["state"]); Assert.Equal("code", query["response_type"]);
        Assert.DoesNotContain("historical-secret", provider.AuthorizationUrl("state", null));
        config[Prefix+"ClientId"] = "another-app"; Assert.False(provider.Available);
        config[Prefix+"ClientSecret"] = "another-secret"; Assert.True(provider.Available);
        config[Prefix+"LoginConfigId"] = "invalid"; Assert.False(provider.Available);
    }

    [Theory]
    [InlineData(true, "CREATE_CONTENT", true)]
    [InlineData(false, "CREATE_CONTENT", false)]
    [InlineData(true, "ANALYZE", false)]
    [InlineData(true, "PROFILE_PLUS_CREATE_CONTENT", true)]
    [InlineData(true, "PROFILE_PLUS_FULL_CONTROL", false)]
    public async Task DiscoveryRequiresExplicitCreateTaskAndPublishingScopes(bool grant, string task, bool expected)
    {
        using var provider = Provider(grant: grant, task: task);
        var page = Assert.Single(await provider.DiscoverAsync("fake-code", null, default));
        Assert.Equal(expected, page.PublishingPermissionGranted);
        Assert.Equal("page-1", page.ExternalAccountId);
        Assert.Equal("page-token", page.Credential.AccessToken);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("negative")]
    [InlineData("expired")]
    [InlineData("wrong-app")]
    [InlineData("invalid")]
    [InlineData("wrong-type")]
    [InlineData("data-expired")]
    public async Task InvalidOrUnverifiableExpiryFailsClosed(string fault)
    {
        using var provider = Provider(fault: fault);
        await Assert.ThrowsAsync<SocialConnectionException>(() => provider.DiscoverAsync("fake-code", null, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRelativeExpiryUsesServerInspection_ZeroNeverInventsDeadline_ValidationRechecks(bool relativeZero)
    {
        using var provider = Provider(zero: true, relativeZero: relativeZero);
        var page = Assert.Single(await provider.DiscoverAsync("fake-code", null, default));
        Assert.Null(page.Credential.ExpiresAtUtc);
        var refreshed = await provider.ValidateAsync("page-1", "Page", page.Credential, default);
        Assert.Null(refreshed.ExpiresAtUtc);
        await Assert.ThrowsAsync<SocialConnectionException>(() => provider.ValidateAsync("another-page", "Page", page.Credential, default));
    }

    private static FacebookWorkspaceOAuthProvider Provider(bool grant = true, string task = "CREATE_CONTENT", string? fault = null, bool zero = false, bool relativeZero = false)
        => new(new(new Handler(request => {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post) return relativeZero ? """{"access_token":"user-token","expires_in":0}""" : """{"access_token":"user-token"}""";
            if (path.EndsWith("debug_token"))
            {
                Assert.Equal("test-app|test-secret", request.Headers.Authorization!.Parameter);
                var page = QueryHelpers.ParseQuery(request.RequestUri.Query)["input_token"] == "page-token";
                var data = new Dictionary<string,object> {
                    ["is_valid"] = fault != "invalid", ["app_id"] = fault == "wrong-app" ? "other-app" : "test-app",
                    ["type"] = fault == "wrong-type" ? "SYSTEM_USER" : page ? "PAGE" : "USER",
                    ["scopes"] = grant ? new[] {"pages_show_list", "pages_read_engagement", "pages_manage_posts"} : new[] {"pages_show_list"},
                    ["expires_at"] = fault == "negative" ? -1L : fault == "expired" ? 1L : zero ? 0L : DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
                };
                if (fault == "missing") data.Remove("expires_at");
                if (fault == "data-expired") data["data_access_expires_at"] = 1L;
                return JsonSerializer.Serialize(new { data });
            }
            if (path.EndsWith("permissions")) return JsonSerializer.Serialize(new { data = new[] {"pages_show_list", "pages_read_engagement", "pages_manage_posts"}.Select(permission => new { permission, status = "granted" }) });
            if (path.EndsWith("accounts")) return JsonSerializer.Serialize(new { data = new[] {new { id = "page-1", name = "Test page", access_token = "page-token", tasks = new[] {task} }} });
            return """{"id":"page-1"}""";
        })), Config(), TimeProvider.System);

    private sealed class Handler(Func<HttpRequestMessage,string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request)) });
    }
}
