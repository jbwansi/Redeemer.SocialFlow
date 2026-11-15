using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Social;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed class SocialDiscoveryDiagnosticTests
{
    [Theory]
    [InlineData(WorkspaceRole.Owner,true)] [InlineData(WorkspaceRole.Admin,true)]
    [InlineData(WorkspaceRole.Editor,true)] [InlineData(WorkspaceRole.Viewer,true)]
    [InlineData(WorkspaceRole.Owner,false)] [InlineData(WorkspaceRole.Admin,false)]
    [InlineData(WorkspaceRole.Editor,false)] [InlineData(WorkspaceRole.Viewer,false)]
    public async Task DiagnosticsRemainBoundToInitiatorWorkspaceRoleAndExpiry_EvenWhenDiscoveryFails(WorkspaceRole role,bool success)
    {
        await using var sqlite=new SqliteConnection("Data Source=:memory:"); await sqlite.OpenAsync();
        var options=new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(sqlite).Options;
        var first=Workspace.Create("First",WorkspaceType.Organization,"alice");
        var second=Workspace.Create("Second",WorkspaceType.Organization,"alice");
        await using var db=new SocialFlowDbContext(options,new ExplicitWorkspaceContext(first.Id));
        await db.Database.MigrateAsync();
        db.Workspaces.AddRange(first,second);
        db.WorkspaceMemberships.AddRange(WorkspaceMembership.Join(first.Id,"alice"),WorkspaceMembership.Join(first.Id,"other-owner"),WorkspaceMembership.Join(first.Id,"bob",WorkspaceRole.Admin),WorkspaceMembership.Join(second.Id,"alice"));
        await db.SaveChangesAsync();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["SocialConnections:Encryption:ActiveKeyId"]="test",["SocialConnections:Encryption:Keys:test"]=Convert.ToBase64String(new byte[32])
        }).Build();
        var cipher=new SocialSecretProtector(config); var provider=new Provider(success);
        var alice=new SocialConnections(options,new Actor("alice"),[provider],cipher,TimeProvider.System);
        var bob=new SocialConnections(options,new Actor("bob"),[provider],cipher,TimeProvider.System);
        var url=await alice.StartAsync(first.Id,SocialPlatform.Facebook,default);
        var state=QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
        var result=await alice.CallbackAsync(SocialPlatform.Facebook,state,"fake-code",false,default);
        var report=await alice.DiagnosticsAsync(first.Id,result.SessionId,default);
        Assert.Equal(success?"no_pages":"missing_discovery_permission",report!.Outcome);
        Assert.Empty(await alice.CandidatesAsync(first.Id,result.SessionId,default));
        await Assert.ThrowsAsync<SocialConnectionException>(()=>alice.SelectAsync(first.Id,result.SessionId,["forged"],default));
        await Assert.ThrowsAsync<SocialConnectionException>(()=>alice.CallbackAsync(SocialPlatform.Facebook,state,"fake-code",false,default));
        await Assert.ThrowsAsync<SocialConnectionException>(()=>bob.DiagnosticsAsync(first.Id,result.SessionId,default));
        await Assert.ThrowsAsync<SocialConnectionException>(()=>alice.DiagnosticsAsync(second.Id,result.SessionId,default));
        Assert.DoesNotContain("token",JsonSerializer.Serialize(report),StringComparison.OrdinalIgnoreCase);
        var member=await db.WorkspaceMemberships.SingleAsync(x=>x.UserId=="alice" && x.WorkspaceId==first.Id);
        member.Change(role,MembershipStatus.Active,2); await db.SaveChangesAsync();
        if(role is WorkspaceRole.Editor or WorkspaceRole.Viewer)
            Assert.Equal(403,(await Assert.ThrowsAsync<SocialConnectionException>(()=>alice.DiagnosticsAsync(first.Id,result.SessionId,default))).Status);
        else Assert.NotNull(await alice.DiagnosticsAsync(first.Id,result.SessionId,default));
        var session=await db.SocialOAuthSessions.SingleAsync();
        Assert.Equal(success?SocialOAuthStatus.Selection:SocialOAuthStatus.Failed,session.Status);
        Assert.DoesNotContain("missing_discovery_permission",session.ProtectedData);
        session.ExpiresAtUtc=DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<SocialConnectionException>(()=>alice.DiagnosticsAsync(first.Id,result.SessionId,default));
        Assert.Empty(await db.SocialConnections.ToListAsync());
    }
    private sealed record Actor(string UserId):ICollaborationActor { public string Email=>UserId+"@example.test"; }
    private sealed class Provider(bool success):IWorkspaceOAuthProvider,IDiagnosticWorkspaceOAuthProvider
    {
        public SocialPlatform Platform=>SocialPlatform.Facebook; public bool Available=>true; public bool SupportsPkce=>false;
        public string AuthorizationUrl(string state,string? challenge)=>"https://provider.example.test/?state="+state;
        public Task<IReadOnlyList<SocialCandidate>> DiscoverAsync(string code,string? verifier,CancellationToken ct)=>throw new InvalidOperationException("Diagnostic path required");
        public Task<SocialDiscoveryResult> DiscoverWithDiagnosticsAsync(string code,CancellationToken ct)=>Task.FromResult(new SocialDiscoveryResult([],new(success?"no_pages":"missing_discovery_permission",success?"complete":"permissions",0,0,0,success?1:0,success,null,new Dictionary<string,int>()),success));
        public Task<SocialCredential> ValidateAsync(string id,string type,SocialCredential credential,CancellationToken ct)=>throw new NotSupportedException();
    }
}
