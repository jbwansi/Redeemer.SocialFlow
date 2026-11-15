using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Infrastructure.Social;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests;

public sealed class FacebookAuthorizedPageDiscoveryTests
{
    [Theory]
    [InlineData("empty", 2, true)]
    [InlineData("standard", 1, true)]
    [InlineData("filtered", 0, true)]
    [InlineData("none", 0, true)]
    [InlineData("declined", 0, true)]
    [InlineData("uninspected-scope", 0, true)]
    [InlineData("invalid-target", 0, true)]
    [InlineData("too-many", 0, true)]
    [InlineData("global-limit", 0, false)]
    [InlineData("missing-token", 0, true)]
    [InlineData("wrong-id", 0, false)]
    [InlineData("wrong-identity", 0, false)]
    [InlineData("user-token", 0, false)]
    [InlineData("wrong-type", 0, false)]
    [InlineData("wrong-app", 0, false)]
    [InlineData("expired", 0, false)]
    [InlineData("graph", 0, false)]
    [InlineData("http", 0, false)]
    [InlineData("accounts-error", 0, false)]
    [InlineData("invalid-accounts", 0, false)]
    [InlineData("partial-error", 0, false)]
    public async Task DiscoveryIsBoundedAndFailClosed(string scenario, int count, bool success)
    {
        var handler = new MetaHandler(scenario);
        using var provider = new FacebookWorkspaceOAuthProvider(new(handler), Config(), TimeProvider.System);
        var result = await provider.DiscoverWithDiagnosticsAsync("SECRET-code", default);
        Assert.Equal(success, result.Succeeded);
        Assert.Equal(count, result.Candidates.Count);
        if (scenario is "standard" or "filtered" or "accounts-error" or "invalid-accounts") Assert.Empty(handler.Targets);
        if (scenario == "empty")
        {
            Assert.Equal(new[] { "810001", "810002" }, handler.Targets);
            Assert.All(result.Candidates, c => Assert.False(c.PublishingPermissionGranted));
            Assert.All(result.Diagnostic.Pages!, p => Assert.Equal("retained_publishing_unverified", p.Outcome));
        }
        if (scenario == "standard") Assert.True(Assert.Single(result.Candidates).PublishingPermissionGranted);
        var safe = JsonSerializer.Serialize(result.Diagnostic);
        var frontend = JsonSerializer.Serialize(result.Candidates.Select(c => new SocialCandidateDto(c.ExternalAccountId, c.DisplayName, c.AccountType, c.PublishingPermissionGranted)));
        Assert.DoesNotContain("SECRET", safe + frontend);
        Assert.DoesNotContain("access_token", safe + frontend);
    }

    [Fact]
    public async Task NonDiagnosticEntryPointAlsoDiscoversAuthorizedPages()
    {
        using var provider = new FacebookWorkspaceOAuthProvider(new(new MetaHandler("empty")), Config(), TimeProvider.System);
        var pages = await provider.DiscoverAsync("code", null, default);
        Assert.Equal(2, pages.Count);
        Assert.All(pages, p => Assert.False(p.PublishingPermissionGranted));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstagramNeverUsesFacebookFallback(bool hasPage)
    {
        var calls = new List<string>();
        using var provider = new InstagramWorkspaceOAuthProvider(new(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath; calls.Add(path);
            if (request.Method == HttpMethod.Post) return Ok("""{"access_token":"SECRET-user","expires_in":3600}""");
            if (path.EndsWith("permissions")) return Ok("""{"data":[{"permission":"pages_show_list","status":"granted"},{"permission":"instagram_basic","status":"granted"},{"permission":"instagram_content_publish","status":"granted"}]}""");
            Assert.EndsWith("/me/accounts", path);
            return Ok(hasPage ? """{"data":[{"id":"810001","access_token":"SECRET-page","instagram_business_account":{"id":"910001","username":"Example"}}]}""" : """{"data":[]}""");
        })), Config(), TimeProvider.System);
        var pages = await provider.DiscoverAsync("code", null, default);
        Assert.Equal(hasPage ? 1 : 0, pages.Count);
        if (hasPage) Assert.True(Assert.Single(pages).PublishingPermissionGranted);
        Assert.Equal(4, calls.Count);
    }

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SocialConnections:Providers:Facebook:ClientId"] = "app",
        ["SocialConnections:Providers:Facebook:ClientSecret"] = "SECRET-app",
        ["SocialConnections:Providers:Facebook:ApiVersion"] = "v26.0",
        ["SocialConnections:Providers:Instagram:ApiVersion"] = "v26.0"
    }).Build();
    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
    private sealed class MetaHandler(string scenario) : HttpMessageHandler
    {
        public List<string> Targets { get; } = [];
        private readonly string[] scopes = ["pages_show_list", "pages_read_engagement", "pages_manage_posts"];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(Respond(request));
        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            var bearer = request.Headers.Authorization?.Parameter;
            if (request.Method == HttpMethod.Post)
            {
                Assert.EndsWith("/oauth/access_token", path);
                return Ok("""{"access_token":"SECRET-user"}""");
            }
            if (path.EndsWith("debug_token"))
            {
                var page = query["input_token"] != "SECRET-user";
                var targets = scenario == "none" ? [] : scenario == "invalid-target" ? new[] { "../SECRET" } :
                    scenario is "too-many" or "global-limit" ? Enumerable.Range(1, scenario == "too-many" ? 501 : 500).Select(i => i.ToString()).ToArray() : ["810001", "810002", "810001"];
                return Ok(JsonSerializer.Serialize(new { data = new {
                    is_valid = true, app_id = page && scenario == "wrong-app" ? "other" : "app",
                    type = page && scenario != "wrong-type" ? "PAGE" : "USER",
                    expires_at = page && scenario == "expired" ? 1 : 0,
                    scopes = scenario == "uninspected-scope" ? [] : scopes,
                    granular_scopes = new[] {
                        new { scope = "pages_manage_posts", target_ids = targets },
                        new { scope = "pages_read_engagement", target_ids = scenario == "global-limit" ? new[] { "999999" } : Array.Empty<string>() }
                    }
                }}));
            }
            Assert.True(query.ContainsKey("appsecret_proof"));
            Assert.False(query.ContainsKey("access_token"));
            if (path.EndsWith("permissions")) return Ok(JsonSerializer.Serialize(new { data = scopes.Select(permission => new { permission, status = scenario == "declined" && permission == "pages_manage_posts" ? "declined" : "granted" }) }));
            if (path.EndsWith("accounts"))
            {
                if (scenario == "accounts-error") return Ok("""{"data":[],"error":{"code":100,"message":"SECRET"}}""");
                if (scenario == "invalid-accounts") return Ok("""{"data":{}}""");
                return Ok(scenario == "standard" ? """{"data":[{"id":"810001","name":"Page","access_token":"SECRET-810001","tasks":["CREATE_CONTENT"]}]}""" :
                    scenario == "filtered" ? """{"data":[{"id":"810001"}]}""" : """{"data":[]}""");
            }
            Assert.DoesNotContain("tasks", query["fields"].ToString());
            var id = path.Split('/').Last();
            if (id == "me") return Ok(JsonSerializer.Serialize(new { id = scenario == "wrong-identity" ? "999" : bearer![7..] }));
            if (bearer != "SECRET-user") return Ok(JsonSerializer.Serialize(new { id }));
            Assert.Equal("id,name,access_token", query["fields"]);
            Targets.Add(id);
            if (scenario == "graph" || scenario == "partial-error" && Targets.Count == 2) return Ok("""{"error":{"code":100,"message":"SECRET"}}""");
            if (scenario == "http") return new(HttpStatusCode.Forbidden) { Content = new StringContent("SECRET") };
            return Ok(JsonSerializer.Serialize(new { id = scenario == "wrong-id" ? "999" : id, name = "Example",
                access_token = scenario == "missing-token" ? null : scenario == "user-token" ? "SECRET-user" : "SECRET-" + id }));
        }
    }
}
