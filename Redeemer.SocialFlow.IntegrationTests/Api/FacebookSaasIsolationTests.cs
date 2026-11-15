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

// Exercises the current model/service in memory, independently of migration compatibility tests.
public sealed class FacebookSaasIsolationTests
{
    [Theory]
    [InlineData(WorkspaceRole.Owner)]
    [InlineData(WorkspaceRole.Admin)]
    [InlineData(WorkspaceRole.Editor)]
    [InlineData(WorkspaceRole.Viewer)]
    public async Task StateSelectionSecretsAndConfigurationRemainBoundToActorAndWorkspace(WorkspaceRole role)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(connection).Options;
        var workspace = Workspace.Create("First", WorkspaceType.Organization, "alice");
        var other = Workspace.Create("Second", WorkspaceType.Organization, "alice");
        await using (var db = new SocialFlowDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Workspaces.AddRange(workspace, other);
            db.WorkspaceMemberships.AddRange(WorkspaceMembership.Join(workspace.Id,"alice"), WorkspaceMembership.Join(workspace.Id,"bob",role), WorkspaceMembership.Join(other.Id,"alice"));
            await db.SaveChangesAsync();
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["SocialConnections:Encryption:ActiveKeyId"] = "test",
            ["SocialConnections:Encryption:Keys:test"] = Convert.ToBase64String(new byte[32])
        }).Build();
        var protector = new SocialSecretProtector(config);
        var fake = new Provider();
        var alice = new SocialConnections(options,new Actor("alice"),[fake],protector,TimeProvider.System);
        var bob = new SocialConnections(options,new Actor("bob"),[fake],protector,TimeProvider.System);
        var start = await alice.StartAsync(workspace.Id,SocialPlatform.Facebook,default);
        var state = QueryHelpers.ParseQuery(new Uri(start).Query)["state"].ToString();
        await Assert.ThrowsAsync<SocialConnectionException>(() => bob.CallbackAsync(SocialPlatform.Facebook,state,"fake-code",false,default));
        Assert.Equal(0,fake.Calls);
        var session = await alice.CallbackAsync(SocialPlatform.Facebook,state,"fake-code",false,default);
        await Assert.ThrowsAsync<SocialConnectionException>(() => alice.CallbackAsync(SocialPlatform.Facebook,state,"fake-code",false,default));
        await Assert.ThrowsAsync<SocialConnectionException>(() => bob.CandidatesAsync(workspace.Id,session.SessionId,default));
        await Assert.ThrowsAsync<SocialConnectionException>(() => bob.SelectAsync(workspace.Id,session.SessionId,["page"],default));
        await Assert.ThrowsAsync<SocialConnectionException>(() => alice.SelectAsync(other.Id,session.SessionId,["page"],default));
        await alice.SelectAsync(workspace.Id,session.SessionId,["page"],default);
        await Assert.ThrowsAsync<SocialConnectionException>(() => alice.SelectAsync(workspace.Id,session.SessionId,["page"],default));
        var rows = await alice.ListAsync(workspace.Id,default);
        Assert.Single(rows); Assert.DoesNotContain("secret",JsonSerializer.Serialize(rows));
        Assert.Empty(await alice.ListAsync(other.Id,default));
        if (role is WorkspaceRole.Editor or WorkspaceRole.Viewer)
        {
            await Assert.ThrowsAsync<SocialConnectionException>(() => bob.StartAsync(workspace.Id,SocialPlatform.Facebook,default));
            using var unavailable = new FacebookWorkspaceOAuthProvider(new(),config,TimeProvider.System);
            var reader = new SocialConnections(options,new Actor("bob"),[unavailable],protector,TimeProvider.System);
            var manager = new SocialConnections(options,new Actor("alice"),[unavailable],protector,TimeProvider.System);
            Assert.DoesNotContain("Enabled",JsonSerializer.Serialize(await reader.ProvidersAsync(workspace.Id,default)));
            Assert.Contains("Enabled",JsonSerializer.Serialize(await manager.ProvidersAsync(workspace.Id,default)));
        }
        await using var stored = new SocialFlowDbContext(options,new ExplicitWorkspaceContext(workspace.Id));
        Assert.DoesNotContain("page-secret",(await stored.SocialConnections.SingleAsync()).ProtectedCredential);
        Assert.Empty((await stored.SocialOAuthSessions.SingleAsync()).ProtectedData);
        Assert.Equal(1,fake.Calls);
        var expiredUrl = await alice.StartAsync(workspace.Id,SocialPlatform.Facebook,default);
        var expiredState = QueryHelpers.ParseQuery(new Uri(expiredUrl).Query)["state"].ToString();
        var expiredSession = await stored.SocialOAuthSessions.SingleAsync(x => x.Status == SocialOAuthStatus.Started);
        expiredSession.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await stored.SaveChangesAsync();
        await Assert.ThrowsAsync<SocialConnectionException>(() => alice.CallbackAsync(SocialPlatform.Facebook,expiredState,"fake-code",false,default));
        Assert.Equal(1,fake.Calls);
    }
    private sealed record Actor(string UserId) : ICollaborationActor { public string Email => UserId+"@example.test"; }
    private sealed class Provider : IWorkspaceOAuthProvider
    {
        public int Calls;
        public SocialPlatform Platform => SocialPlatform.Facebook;
        public bool Available => true;
        public bool SupportsPkce => false;
        public string AuthorizationUrl(string state,string? challenge) => "https://www.facebook.com/dialog/oauth?state="+state;
        public Task<IReadOnlyList<SocialCandidate>> DiscoverAsync(string code,string? verifier,CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<SocialCandidate>>([new("page","Test page","Page",new("page-secret",null,null,null,["pages_show_list"]),false)]);
        }
        public Task<SocialCredential> ValidateAsync(string id,string type,SocialCredential credential,CancellationToken ct) => Task.FromResult(credential);
    }
}
