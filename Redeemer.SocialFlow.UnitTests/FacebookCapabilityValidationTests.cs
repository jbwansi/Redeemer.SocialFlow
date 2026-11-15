using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Infrastructure.Social;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests;

public sealed class FacebookCapabilityValidationTests
{
    private static readonly string[] Scopes = ["pages_show_list", "pages_manage_posts", "pages_read_engagement"];
    private static SocialCredential Credential(bool context = true) => new("SECRET-page", null, null, null, [],
        context ? new("SECRET-user", null) : null);
    private static FacebookWorkspaceOAuthProvider Provider(Meta handler) => new(new(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["SocialConnections:Providers:Facebook:ClientId"] = "app",
            ["SocialConnections:Providers:Facebook:ClientSecret"] = "SECRET-app",
            ["SocialConnections:Providers:Facebook:ApiVersion"] = "v26.0"
        }).Build(), TimeProvider.System);

    [Theory]
    [InlineData("CREATE_CONTENT")]
    [InlineData("PROFILE_PLUS_CREATE_CONTENT")]
    public async Task FreshMatchingUserAccountEvidenceCanUpgradeAndDowngrade(string task)
    {
        var meta = new Meta(); using var provider = Provider(meta);
        var unknown = await provider.ValidatePublishingCapabilityAsync("123", "Page", Credential(), default);
        Assert.Equal(PublishingCapabilityStatus.Unverified, unknown.Evidence.Status);
        Assert.Equal("PageTaskEvidenceUnavailable", unknown.Evidence.Reason);
        meta.Accounts = JsonSerializer.Serialize(new { data = new[] { new { id = "123", tasks = new[] { task } } } });
        var verified = await provider.ValidatePublishingCapabilityAsync("123", "Page", unknown.Credential, default);
        Assert.Equal(PublishingCapabilityStatus.Verified, verified.Evidence.Status);
        Assert.Equal(task, verified.Evidence.Task);
        Assert.Equal("MetaUserAccounts", verified.Evidence.Source);
        Assert.NotNull(verified.Evidence.CheckedAtUtc);
        meta.Accounts = """{"data":[{"id":"123","tasks":["ANALYZE"]}]}""";
        var denied = await provider.ValidatePublishingCapabilityAsync("123", "Page", verified.Credential, default);
        Assert.Equal(PublishingCapabilityStatus.Denied, denied.Evidence.Status);
        Assert.Equal("PageCreateContentTaskMissing", denied.Evidence.Reason);
        Assert.Null(denied.Evidence.Task);
    }

    [Theory]
    [InlineData("{\"data\":[{\"id\":\"999\",\"tasks\":[\"CREATE_CONTENT\"]}]}", PublishingCapabilityStatus.Unverified)]
    [InlineData("{\"data\":[{\"id\":\"123\"}]}", PublishingCapabilityStatus.Unverified)]
    [InlineData("{\"data\":[{\"id\":\"123\",\"tasks\":null}]}", PublishingCapabilityStatus.Unverified)]
    [InlineData("{\"data\":[{\"id\":\"123\",\"tasks\":[\"CREATE_CONTENT\",42]}]}", PublishingCapabilityStatus.Unverified)]
    [InlineData("{\"data\":[{\"id\":\"123\",\"tasks\":[]}]}", PublishingCapabilityStatus.Denied)]
    [InlineData("{\"data\":[{\"id\":\"123\",\"tasks\":[\"PROFILE_PLUS_FULL_CONTROL\"]}]}", PublishingCapabilityStatus.Denied)]
    public async Task TasksMustComeFromMatchingAccountAndBeExplicit(string accounts, PublishingCapabilityStatus status)
    {
        using var provider = Provider(new() { Accounts = accounts });
        var result = await provider.ValidatePublishingCapabilityAsync("123", "Page", Credential(), default);
        Assert.Equal(status, result.Evidence.Status);
        Assert.Null(result.Evidence.Task);
    }

    [Fact]
    public async Task LegacyPageOnlyCredentialNeverInfersTaskFromScopes()
    {
        var meta = new Meta(); using var provider = Provider(meta);
        var result = await provider.ValidatePublishingCapabilityAsync("123", "Page", Credential(false), default);
        Assert.Equal(PublishingCapabilityStatus.Unverified, result.Evidence.Status);
        Assert.Equal(0, meta.UserCalls);
    }

    [Fact]
    public async Task CurrentPermissionsReplaceCachedScopesAndCanRecover()
    {
        var meta = new Meta { Granted = false, Accounts = """{"data":[{"id":"123","tasks":["CREATE_CONTENT"]}]}""" };
        using var provider = Provider(meta);
        var denied = await provider.ValidatePublishingCapabilityAsync("123", "Page", Credential(), default);
        Assert.Equal("PublishingScopeMissing", denied.Evidence.Reason);
        meta.Granted = true;
        var recovered = await provider.ValidatePublishingCapabilityAsync("123", "Page", denied.Credential, default);
        Assert.Equal(PublishingCapabilityStatus.Verified, recovered.Evidence.Status);
    }

    [Theory]
    [InlineData("revoked", 401)] [InlineData("user-revoked", 401)]
    [InlineData("identity", 401)] [InlineData("type", 401)]
    [InlineData("graph", 502)] [InlineData("http", 502)]
    [InlineData("duplicate", 502)] [InlineData("pagination", 502)]
    public async Task InvalidOrAmbiguousEvidenceFailsWithoutLeakingSecrets(string fault, int status)
    {
        using var provider = Provider(new() { Fault = fault });
        var error = await Assert.ThrowsAsync<SocialConnectionException>(() => provider.ValidatePublishingCapabilityAsync("123", "Page", Credential(), default));
        Assert.Equal(status, error.Status);
        Assert.DoesNotContain("SECRET", error.ToString());
        Assert.DoesNotContain("SECRET", Credential().ToString());
        Assert.DoesNotContain("SECRET", Credential().FacebookVerification!.ToString());
    }

    private sealed class Meta : HttpMessageHandler
    {
        public string Accounts = """{"data":[]}""";
        public bool Granted = true;
        public string? Fault;
        public int UserCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method); // No publishing or token mutation in revalidation.
            Assert.Equal("graph.facebook.com", request.RequestUri!.Host);
            Assert.StartsWith("/v26.0/", request.RequestUri.AbsolutePath);
            var path = request.RequestUri.AbsolutePath;
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            string body;
            if (path.EndsWith("debug_token"))
            {
                var user = query["input_token"] == "SECRET-user";
                body = JsonSerializer.Serialize(new { data = new { is_valid = Fault != "revoked" && !(user && Fault == "user-revoked"),
                    app_id = "app", type = user || Fault == "type" ? "USER" : "PAGE", expires_at = 0, scopes = Scopes } });
            }
            else
            {
                Assert.True(query.ContainsKey("appsecret_proof"));
                Assert.False(query.ContainsKey("access_token"));
                var user = request.Headers.Authorization!.Parameter == "SECRET-user";
                if (user) UserCalls++;
                if (path.EndsWith("/me")) { Assert.False(user); body = JsonSerializer.Serialize(new { id = Fault == "identity" ? "999" : "123" }); }
                else if (path.EndsWith("permissions"))
                {
                    Assert.True(user);
                    body = JsonSerializer.Serialize(new { data = Scopes.Select(permission => new { permission, status = Granted ? "granted" : "declined" }) });
                }
                else
                {
                    Assert.True(user); Assert.EndsWith("/me/accounts", path); Assert.Equal("id,tasks", query["fields"]);
                    body = Fault == "graph" ? """{"error":{"code":100,"message":"SECRET-provider"}}""" :
                        Fault == "duplicate" ? """{"data":[{"id":"123","tasks":["CREATE_CONTENT"]},{"id":"123","tasks":[]}]}""" :
                        Fault == "pagination" ? """{"data":[],"paging":{"next":"https://unsafe.test/SECRET","cursors":{"after":"same"}}}""" : Accounts;
                }
            }
            return Task.FromResult(new HttpResponseMessage(Fault == "http" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
