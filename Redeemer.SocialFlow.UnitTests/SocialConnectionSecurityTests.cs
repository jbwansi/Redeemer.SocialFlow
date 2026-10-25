using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Infrastructure.Social;
using Xunit;
namespace Redeemer.SocialFlow.UnitTests;

public sealed class SocialConnectionSecurityTests
{
    [Theory]
    [InlineData("openid profile w_member_social", true)]
    [InlineData("openid profile", false)]
    [InlineData("", false)]
    public async Task LinkedInPublishingRequiresConfirmedGrant_NotRequestedScopes(string granted, bool publishing)
    {
        var config = Settings("LinkedIn", "openid profile w_member_social");
        using var provider = new LinkedInWorkspaceOAuthProvider(new(new FakeHttp(request => Task.FromResult(
            request.Method == HttpMethod.Post
                ? System.Text.Json.JsonSerializer.Serialize(new { access_token = "test-secret", expires_in = 3600, scope = granted })
                : """{"sub":"same-profile","name":"Personal profile"}"""))), config, TimeProvider.System);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(provider.AuthorizationUrl("state", null)).Query);
        Assert.Equal("openid profile w_member_social", query["scope"]);
        var account = Assert.Single(await provider.DiscoverAsync("code", null, default));
        Assert.Equal(publishing, account.PublishingPermissionGranted);
        Assert.Equal(publishing, account.Credential.Scopes.Contains("w_member_social"));
    }

    [Fact]
    public void LinkedInLegacyCredentialsRequireOptIn_AndNeverMixApplicationPairs()
    {
        var config = Settings("LinkedIn", "openid profile");
        config["SocialConnections:Providers:LinkedIn:ClientId"] = null;
        config["SocialConnections:Providers:LinkedIn:ClientSecret"] = null;
        config["LinkedIn:ClientId"] = "legacy-id";
        config["LinkedIn:ClientSecret"] = "legacy-secret";
        using var provider = new LinkedInWorkspaceOAuthProvider(new(), config, TimeProvider.System);
        Assert.False(provider.Available);
        config["SocialConnections:Providers:LinkedIn:UseLegacyClientCredentials"] = "true";
        Assert.True(provider.Available);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(provider.AuthorizationUrl("opaque-state", null)).Query);
        Assert.Equal("legacy-id", query["client_id"]);
        Assert.Equal("https://social.example.test/api/social-oauth/2/callback", query["redirect_uri"]);
        Assert.Equal("openid profile", query["scope"]);
        Assert.Equal("opaque-state", query["state"]);
        Assert.DoesNotContain("legacy-secret", provider.AuthorizationUrl("opaque-state", null));
        Assert.Contains("non demandée", provider.ConfigurationMessage);
        config["SocialConnections:Providers:LinkedIn:ClientId"] = "different-app";
        Assert.False(provider.Available);
        config["SocialConnections:Providers:LinkedIn:ClientSecret"] = "different-secret";
        Assert.True(provider.Available);
        Assert.Contains("client_id=different-app", provider.AuthorizationUrl("state", null));
        config["SocialConnections:Providers:LinkedIn:PermissionsApproved"] = "false";
        Assert.False(provider.Available);
        Assert.Contains("openid et profile", provider.ConfigurationMessage);
    }

    [Fact]
    public void EncryptionBindsWorkspaceAndRecord_RejectsTampering_AndSupportsKeyRotation()
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["SocialConnections:Encryption:ActiveKeyId"]="one",["SocialConnections:Encryption:Keys:one"]=Convert.ToBase64String(new byte[32]),
            ["SocialConnections:Encryption:Keys:two"]=Convert.ToBase64String(Enumerable.Repeat((byte)2,32).ToArray()) }).Build();
        var cipher=new SocialSecretProtector(config); Assert.True(cipher.Available);
        var encrypted=cipher.Protect("access-secret","workspace-a/connection-a");
        Assert.DoesNotContain("access-secret",encrypted);
        Assert.NotEqual(encrypted,cipher.Protect("access-secret","workspace-a/connection-a"));
        Assert.ThrowsAny<CryptographicException>(()=>cipher.Unprotect(encrypted,"workspace-b/connection-a"));
        Assert.ThrowsAny<CryptographicException>(()=>cipher.Unprotect(encrypted,"workspace-a/connection-b"));
        var parts=encrypted.Split('.'); var bytes=Convert.FromBase64String(parts[3]); bytes[0]^=1; parts[3]=Convert.ToBase64String(bytes);
        Assert.ThrowsAny<CryptographicException>(()=>cipher.Unprotect(string.Join('.',parts),"workspace-a/connection-a"));
        config["SocialConnections:Encryption:ActiveKeyId"]="two";
        Assert.Equal("access-secret",cipher.Unprotect(encrypted,"workspace-a/connection-a"));
        Assert.StartsWith("two.",cipher.Protect("access-secret","workspace-a/connection-a"));
        config["SocialConnections:Encryption:Keys:one"]=null;
        Assert.ThrowsAny<CryptographicException>(()=>cipher.Unprotect(encrypted,"workspace-a/connection-a"));
    }
    [Fact]
    public void MissingSecretsFailClosed_AndCredentialDiagnosticsAreRedacted()
    {
        Assert.False(new SocialSecretProtector(new ConfigurationBuilder().Build()).Available);
        Assert.DoesNotContain("access-secret",new SocialCredential("access-secret","refresh-secret",null,null,[]).ToString());
        Assert.False(WorkspacePermissions.Allows(WorkspaceRole.Editor,WorkspacePermission.ManageSocial));
        Assert.False(WorkspacePermissions.Allows(WorkspaceRole.Viewer,WorkspacePermission.ManageSocial));
        Assert.True(WorkspacePermissions.Allows(WorkspaceRole.Admin,WorkspacePermission.ManageSocial));
        Assert.True(WorkspacePermissions.Allows(WorkspaceRole.Owner,WorkspacePermission.ManageSocial));
    }
    private static IConfiguration Settings(string platform,string scopes) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Authentication:PublicOrigin"]="https://social.example.test",
        [$"SocialConnections:Providers:{platform}:Enabled"]="true",[$"SocialConnections:Providers:{platform}:PermissionsApproved"]="true",
        [$"SocialConnections:Providers:{platform}:ClientId"]="app-id",[$"SocialConnections:Providers:{platform}:ClientSecret"]="app-secret",
        [$"SocialConnections:Providers:{platform}:Scopes"]=scopes,[$"SocialConnections:Providers:{platform}:ApiVersion"]="v25.0" }).Build();
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task MetaDiscoversOnlyPagesOrLinkedProfessionalAccounts_AndKeepsTokensInServerCredentials(bool instagram)
    {
        var scopes="pages_show_list pages_read_engagement pages_manage_posts instagram_basic instagram_content_publish";
        var handler=new FakeHttp(async request=>{
            Assert.DoesNotContain("page-secret",request.RequestUri!.Query);
            Assert.DoesNotContain("app-secret",request.RequestUri.Query);
            if(request.Method==HttpMethod.Post) { Assert.Contains("client_secret=app-secret",await request.Content!.ReadAsStringAsync()); return "{\"access_token\":\"user-secret\",\"expires_in\":3600}"; }
            if(request.RequestUri.AbsolutePath.EndsWith("permissions")) return "{\"data\":["+string.Join(',',scopes.Split(' ').Select(s=>"{\"permission\":\""+s+"\",\"status\":\"granted\"}"))+"]}";
            Assert.Equal("user-secret",request.Headers.Authorization!.Parameter);
            return """{"data":[{"id":"p1","name":"Page One","access_token":"page-secret-1","tasks":["CREATE_CONTENT"],"instagram_business_account":{"id":"ig1","username":"brand-one"}},{"id":"p2","name":"Page Two","access_token":"page-secret-2","tasks":["CREATE_CONTENT"]}]}""";
        });
        using WorkspaceOAuthProvider provider=instagram ? new InstagramWorkspaceOAuthProvider(new(handler),Settings("Instagram",scopes),TimeProvider.System) : new FacebookWorkspaceOAuthProvider(new(handler),Settings("Facebook",scopes),TimeProvider.System);
        Assert.True(provider.Available);
        var results=await provider.DiscoverAsync("code",null,default);
        Assert.Equal(instagram?1:2,results.Count);
        Assert.All(results,r=>{Assert.True(r.PublishingPermissionGranted); Assert.StartsWith("page-secret",r.Credential.AccessToken); Assert.Equal(instagram?"Professional":"Page",r.AccountType);});
        Assert.DoesNotContain(results,r=>r.AccountType=="Profile");
    }
    [Fact]
    public async Task LinkedInRespectsGrantedScopes_AndRefreshesOnlyWithAnIssuedRefreshToken()
    {
        var refresh=false;
        var handler=new FakeHttp(async request=>{
            if(request.Method==HttpMethod.Post) {
                var body=await request.Content!.ReadAsStringAsync();
                refresh=body.Contains("grant_type=refresh_token");
                return """{"access_token":"li-secret","expires_in":3600,"scope":"openid profile","refresh_token":"refresh-secret","refresh_token_expires_in":7200}""";
            }
            return """{"sub":"subject-1","name":"LinkedIn Person"}""";
        });
        using var provider=new LinkedInWorkspaceOAuthProvider(new(handler),Settings("LinkedIn","openid profile w_member_social"),TimeProvider.System);
        var candidate=Assert.Single(await provider.DiscoverAsync("code",null,default));
        Assert.False(candidate.PublishingPermissionGranted);
        Assert.DoesNotContain("app-secret",provider.AuthorizationUrl("state",null));
        var updated=await provider.ValidateAsync(candidate.ExternalAccountId,candidate.AccountType,candidate.Credential with { ExpiresAtUtc=DateTimeOffset.UtcNow.AddMinutes(-1) },default);
        Assert.True(refresh); Assert.True(updated.ExpiresAtUtc>DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<SocialConnectionException>(()=>provider.ValidateAsync(candidate.ExternalAccountId,candidate.AccountType,candidate.Credential with { ExpiresAtUtc=DateTimeOffset.UtcNow.AddMinutes(-1),RefreshToken=null },default));
    }
    [Fact]
    public async Task ProviderErrorBodyNeverEscapes_AndUnconfiguredProvidersAreUnavailable()
    {
        using var provider=new FacebookWorkspaceOAuthProvider(new(new FakeHttp(_=>Task.FromResult("{\"error\":\"provider-secret\"}"),HttpStatusCode.BadRequest)),Settings("Facebook","pages_show_list"),TimeProvider.System);
        var error=await Assert.ThrowsAsync<SocialConnectionException>(()=>provider.DiscoverAsync("code-secret",null,default));
        Assert.DoesNotContain("provider-secret",error.ToString()); Assert.DoesNotContain("code-secret",error.ToString());
        using var unavailable=new LinkedInWorkspaceOAuthProvider(new(new FakeHttp(_=>throw new Exception())),new ConfigurationBuilder().Build(),TimeProvider.System);
        Assert.False(unavailable.Available);
    }
    private sealed class FakeHttp(Func<HttpRequestMessage,Task<string>> response,HttpStatusCode status=HttpStatusCode.OK) : HttpMessageHandler
    { protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>new(status){Content=new StringContent(await response(request))}; }
}
