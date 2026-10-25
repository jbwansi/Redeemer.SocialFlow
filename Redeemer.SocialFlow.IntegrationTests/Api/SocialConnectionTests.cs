using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Xunit;
namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed partial class WorkspaceIsolationTests
{
    private readonly FakeWorkspaceOAuth socialProvider = new();
    [Fact]
    public async Task ReconnectionUpdatesConfirmedPermissionsWithoutDuplicatingOrChangingOtherWorkspaces()
    {
        socialProvider.Publishing = false;
        var original = await Connect(alice, a.Id);
        var foreign = await Connect(bob, b.Id);
        Assert.All(original, x => Assert.False(x.PublishingPermissionGranted));
        socialProvider.Publishing = true;
        var updated = await Connect(alice, a.Id);
        Assert.Equal(original.Select(x => x.Id), updated.Select(x => x.Id));
        Assert.All(updated, x => Assert.True(x.PublishingPermissionGranted));
        Assert.All(updated, x => Assert.NotEqual(original.Single(o => o.Id == x.Id).Revision, x.Revision));
        Assert.Equal(foreign, (await bob.GetFromJsonAsync<SocialConnectionDto[]>(Social(b.Id)))!);
    }
    private static string Social(Guid id) => $"/api/workspaces/{id}/social-connections";
    private async Task<string> OAuthStart(HttpClient client, Guid id)
    {
        var response=await Send(client,HttpMethod.Post,Social(id)+"/start",new { platform=1 });
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var url=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizationUrl").GetString()!;
        var query=QueryHelpers.ParseQuery(new Uri(url).Query);
        Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"]));
        return query["state"].ToString();
    }
    private async Task<Guid> OAuthCallback(HttpClient client, string state)
    {
        var response=await client.GetAsync("/api/social-oauth/1/callback?state="+state+"&code=fake-code&workspace="+b.Id);
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        var fragment=response.Headers.Location!.OriginalString.Split('?')[1];
        var query=QueryHelpers.ParseQuery("?"+fragment);
        Assert.True(Guid.TryParse(query["session"],out var session),response.Headers.Location.ToString());
        return session;
    }
    private async Task<SocialConnectionDto[]> Connect(HttpClient client,Guid id)
    {
        var session=await OAuthCallback(client,await OAuthStart(client,id));
        var choices=await client.GetStringAsync(Social(id)+$"/sessions/{session}");
        Assert.DoesNotContain("secret",choices,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken",choices,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NoContent,(await Send(client,HttpMethod.Post,Social(id)+$"/sessions/{session}/select",new { externalAccountIds=new[]{"page-one","page-two"} })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,HttpMethod.Post,Social(id)+$"/sessions/{session}/select",new { externalAccountIds=new[]{"page-one"} })).StatusCode);
        return (await client.GetFromJsonAsync<SocialConnectionDto[]>(Social(id)))!;
    }
    [Fact]
    public async Task MultipleAccounts_AreIsolated_Encrypted_AndDuplicatesDoNotAccumulate()
    {
        var own=await Connect(alice,a.Id); Assert.Equal(2,own.Length);
        var other=await Connect(bob,b.Id); Assert.Equal(2,other.Length);
        Assert.NotEqual(own[0].Id,other[0].Id);
        Assert.Equal(HttpStatusCode.Forbidden,(await alice.GetAsync(Social(b.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Send(alice,HttpMethod.Post,Social(a.Id)+$"/{other[0].Id}/disconnect",new { other[0].Revision })).StatusCode);
        Assert.Equal(2,(await Connect(alice,a.Id)).Length);
        await using var db=Db(a.Id);
        var row=await db.SocialConnections.FirstAsync();
        Assert.DoesNotContain("provider-secret",row.ProtectedCredential);
        Assert.All(await db.SocialOAuthSessions.Where(x=>x.Status==SocialOAuthStatus.Completed).ToListAsync(),x=>Assert.Empty(x.ProtectedData));
        db.SocialConnections.Add(new() { WorkspaceId=a.Id,Platform=row.Platform,ExternalAccountId=row.ExternalAccountId,ConnectionStatus=SocialConnectionStatus.Connected });
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task OAuthStateIsBoundToInitiator_Expires_IsSingleUse_AndProviderErrorsAreSanitized()
    {
        var state=await OAuthStart(alice,a.Id);
        Assert.Contains("oauth=failed",(await bob.GetAsync("/api/social-oauth/1/callback?state="+state+"&code=x")).Headers.Location!.ToString());
        var session=await OAuthCallback(alice,state);
        Assert.Equal(1,socialProvider.Calls); Assert.Equal(43,socialProvider.Verifier!.Length);
        Assert.Contains("oauth=failed",(await alice.GetAsync("/api/social-oauth/1/callback?state="+state+"&code=x")).Headers.Location!.ToString());
        Assert.Equal(1,socialProvider.Calls);
        Assert.Equal(HttpStatusCode.Forbidden,(await bob.GetAsync(Social(a.Id)+$"/sessions/{session}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await alice.GetAsync(Social(b.Id)+$"/sessions/{session}")).StatusCode);
        var expired=await OAuthStart(alice,a.Id);
        await using(var db=Db(a.Id)) { var attempt=await db.SocialOAuthSessions.SingleAsync(x=>x.Status==SocialOAuthStatus.Started); attempt.ExpiresAtUtc=DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); }
        Assert.Contains("oauth=failed",(await alice.GetAsync("/api/social-oauth/1/callback?state="+expired+"&code=x")).Headers.Location!.ToString());
        socialProvider.Fail=true;
        var failed=await alice.GetAsync("/api/social-oauth/1/callback?state="+await OAuthStart(alice,a.Id)+"&code=x");
        Assert.Contains("oauth=failed",failed.Headers.Location!.ToString());
        Assert.DoesNotContain("provider-secret",await failed.Content.ReadAsStringAsync());
        Assert.Empty((await alice.GetFromJsonAsync<SocialConnectionDto[]>(Social(a.Id)))!);
    }
    [Theory]
    [InlineData(WorkspaceRole.Owner,true)] [InlineData(WorkspaceRole.Admin,true)]
    [InlineData(WorkspaceRole.Editor,false)] [InlineData(WorkspaceRole.Viewer,false)]
    public async Task SocialRolePermissionsAreEnforced(WorkspaceRole role,bool manage)
    {
        var id=await Organization();
        await using(var db=Db(id)) { db.WorkspaceMemberships.Add(WorkspaceMembership.Join(id,bobId,role)); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.OK,(await bob.GetAsync(Social(id))).StatusCode);
        Assert.Equal(manage?HttpStatusCode.OK:HttpStatusCode.Forbidden,(await Send(bob,HttpMethod.Post,Social(id)+"/start",new {platform=1})).StatusCode);
        if(!manage) {
            var connection=(await Connect(alice,id))[0];
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(bob,HttpMethod.Post,Social(id)+$"/{connection.Id}/disconnect",new { connection.Revision })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest,(await alice.PostAsJsonAsync(Social(id)+"/start",new {platform=1})).StatusCode);
    }
    [Fact]
    public async Task ExpirationRevocationAndDestinationsStayInsideWorkspace()
    {
        var own=await Connect(alice,a.Id); var foreign=await Connect(bob,b.Id);
        var post=await Create(alice,"Destinations");
        Assert.Equal(HttpStatusCode.NotFound,(await Send(alice,HttpMethod.Put,Social(a.Id)+$"/posts/{post.Id}/destinations",new { connectionIds=new[]{foreign[0].Id} })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await Send(alice,HttpMethod.Put,Social(a.Id)+$"/posts/{post.Id}/destinations",new { connectionIds=own.Select(x=>x.Id).ToArray() })).StatusCode);
        await using(var db=Db(a.Id)) {
            db.PostSocialDestinations.Add(new(){WorkspaceId=a.Id,SocialPostId=post.Id,SocialConnectionId=foreign[0].Id});
            await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        }
        await using(var db=Db(a.Id)) { var row=await db.SocialConnections.SingleAsync(x=>x.Id==own[0].Id); row.ExpiresAtUtc=DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); }
        Assert.Contains((await alice.GetFromJsonAsync<SocialConnectionDto[]>(Social(a.Id)))!,x=>x.ConnectionStatus==SocialConnectionStatus.Expired);
        Assert.Equal(HttpStatusCode.NoContent,(await Send(alice,HttpMethod.Post,Social(a.Id)+$"/{own[1].Id}/disconnect",new {own[1].Revision})).StatusCode);
        await using(var db=Db(a.Id)) { var row=await db.SocialConnections.SingleAsync(x=>x.Id==own[1].Id); Assert.Empty(row.ProtectedCredential); Assert.Equal(SocialConnectionStatus.Revoked,row.ConnectionStatus); }
        Assert.Equal(HttpStatusCode.Conflict,(await Send(alice,HttpMethod.Post,Social(a.Id)+$"/{own[1].Id}/validate",new {own[1].Revision})).StatusCode);
    }
    [Fact]
    public async Task ChangedRolesAndConcurrentCallbacksCannotBypassAuthorization()
    {
        var id=await Organization();
        await using(var db=Db(id)) { db.WorkspaceMemberships.Add(WorkspaceMembership.Join(id,bobId,WorkspaceRole.Admin)); await db.SaveChangesAsync(); }
        var state=await OAuthStart(bob,id);
        await using(var db=Db(id)) { var member=await db.WorkspaceMemberships.SingleAsync(x=>x.WorkspaceId==id && x.UserId==bobId); member.Change(WorkspaceRole.Editor,MembershipStatus.Active,1); await db.SaveChangesAsync(); }
        Assert.Contains("oauth=failed",(await bob.GetAsync("/api/social-oauth/1/callback?state="+state+"&code=x")).Headers.Location!.ToString());
        Assert.Equal(0,socialProvider.Calls);
        state=await OAuthStart(alice,id);
        var responses=await Task.WhenAll(alice.GetAsync("/api/social-oauth/1/callback?state="+state+"&code=x"),alice.GetAsync("/api/social-oauth/1/callback?state="+state+"&code=x"));
        Assert.Single(responses,x=>x.Headers.Location!.ToString().Contains("session="));
        Assert.Single(responses,x=>x.Headers.Location!.ToString().Contains("oauth=failed"));
        Assert.Equal(1,socialProvider.Calls);
    }
    [Fact]
    public async Task ProviderRevocationErasesSecrets_AndConsentDenialConsumesState()
    {
        var connection=(await Connect(alice,a.Id))[0];
        socialProvider.Revoked=true;
        Assert.Equal(HttpStatusCode.NoContent,(await Send(alice,HttpMethod.Post,Social(a.Id)+$"/{connection.Id}/validate",new { connection.Revision })).StatusCode);
        await using(var db=Db(a.Id)) { var stored=await db.SocialConnections.SingleAsync(x=>x.Id==connection.Id); Assert.Empty(stored.ProtectedCredential); Assert.Equal(SocialConnectionStatus.ReauthorizationRequired,stored.ConnectionStatus); }
        var state=await OAuthStart(alice,a.Id); var calls=socialProvider.Calls;
        Assert.Contains("oauth=failed",(await alice.GetAsync("/api/social-oauth/1/callback?state="+state+"&error=access_denied&error_description=secret")).Headers.Location!.ToString());
        Assert.Contains("oauth=failed",(await alice.GetAsync("/api/social-oauth/1/callback?state="+state+"&code=x")).Headers.Location!.ToString());
        Assert.Equal(calls,socialProvider.Calls);
        var list=await alice.GetStringAsync(Social(a.Id));
        Assert.DoesNotContain("provider-secret",list); Assert.DoesNotContain("protectedCredential",list,StringComparison.OrdinalIgnoreCase);
    }
    private sealed class FakeWorkspaceOAuth : IWorkspaceOAuthProvider
    {
        public SocialPlatform Platform=>SocialPlatform.Facebook; public bool Available=>true; public bool SupportsPkce=>true;
        public bool Publishing = true; public int Calls; public bool Fail; public bool Revoked; public string? Verifier;
        public string AuthorizationUrl(string state,string? challenge)=>"https://oauth.example.test/authorize?state="+state+"&code_challenge="+challenge;
        public Task<IReadOnlyList<SocialCandidate>> DiscoverAsync(string code,string? verifier,CancellationToken ct)
        {
            Calls++; Verifier=verifier; if(Fail) throw new Exception("provider-secret must never escape");
            return Task.FromResult<IReadOnlyList<SocialCandidate>>(new[]{"page-one","page-two"}.Select(id=>new SocialCandidate(id,id,"Page",new("provider-secret-"+id,null,DateTimeOffset.UtcNow.AddHours(1),null,["pages_manage_posts"]),Publishing)).ToArray());
        }
        public Task<SocialCredential> ValidateAsync(string id,string type,SocialCredential credential,CancellationToken ct)=>Revoked ? throw new SocialConnectionException(401,"Provider authorization revoked") : Task.FromResult(credential);
    }
}

