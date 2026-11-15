using System.Text.Json;
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

public sealed class FacebookCapabilityStateTests
{
    [Fact]
    public async Task RevalidationPersistsTransitionsEvidenceEncryptedContextAndRevocation()
    {
        await using var fixture = await Fixture.Create();
        var service = fixture.Service;
        var c = Assert.Single(await service.ListAsync(fixture.Workspace.Id, default));
        Assert.False(c.PublishingPermissionGranted);
        fixture.Provider.Status = PublishingCapabilityStatus.Verified;
        await service.ValidateAsync(fixture.Workspace.Id, c.Id, c.Revision, default);
        c = Assert.Single(await service.ListAsync(fixture.Workspace.Id, default));
        Assert.True(c.PublishingPermissionGranted);
        Assert.Equal(PublishingCapabilityStatus.Verified, c.PublishingCapability!.Status);
        Assert.Equal("MetaUserAccounts", c.PublishingCapability.Source);
        Assert.Equal("PROFILE_PLUS_CREATE_CONTENT", c.PublishingCapability.Task);
        Assert.InRange(c.PublishingCapability.CheckedAtUtc!.Value, fixture.Clock.GetUtcNow().AddMinutes(-1), fixture.Clock.GetUtcNow());
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(c));
        await service.SetDestinationsAsync(fixture.Workspace.Id, fixture.Post.Id, [c.Id], default);
        await using (var db = fixture.Db())
        {
            var row = await db.SocialConnections.SingleAsync();
            Assert.DoesNotContain("SECRET", row.ProtectedCredential);
            Assert.Contains("SECRET-user", fixture.Secrets.Unprotect(row.ProtectedCredential, fixture.Purpose));
        }
        fixture.Provider.Status = PublishingCapabilityStatus.Denied;
        await service.ValidateAsync(fixture.Workspace.Id, c.Id, c.Revision, default);
        c = Assert.Single(await service.ListAsync(fixture.Workspace.Id, default));
        Assert.False(c.PublishingPermissionGranted);
        Assert.Equal(PublishingCapabilityStatus.Denied, c.PublishingCapability!.Status);
        Assert.Null(c.PublishingCapability.Task);
        await Assert.ThrowsAsync<SocialConnectionException>(() => service.SetDestinationsAsync(fixture.Workspace.Id, fixture.Post.Id, [c.Id], default));
        await service.DisconnectAsync(fixture.Workspace.Id, c.Id, c.Revision, default);
        c = Assert.Single(await service.ListAsync(fixture.Workspace.Id, default));
        Assert.Equal("ConnectionRevoked", c.PublishingCapability!.Reason);
        Assert.Equal(PublishingCapabilityStatus.Denied, c.PublishingCapability.Status);
        await using var revokedDb = fixture.Db();
        Assert.Empty((await revokedDb.SocialConnections.SingleAsync()).ProtectedCredential);
        await Assert.ThrowsAsync<SocialConnectionException>(() => service.ValidateAsync(fixture.Workspace.Id, c.Id, c.Revision, default));
    }

    [Theory]
    [InlineData(401, PublishingCapabilityStatus.Denied, "AuthorizationInvalid")]
    [InlineData(502, PublishingCapabilityStatus.Unverified, "MetaVerificationUnavailable")]
    public async Task FailedChecksRemovePreviousAuthorizationAndPreserveError(int error, PublishingCapabilityStatus expected, string reason)
    {
        await using var fixture = await Fixture.Create();
        fixture.Provider.Status = PublishingCapabilityStatus.Verified;
        var c = Assert.Single(await fixture.Service.ListAsync(fixture.Workspace.Id, default));
        await fixture.Service.ValidateAsync(fixture.Workspace.Id, c.Id, c.Revision, default);
        c = Assert.Single(await fixture.Service.ListAsync(fixture.Workspace.Id, default));
        fixture.Provider.Error = error;
        var attempt = () => fixture.Service.ValidateAsync(fixture.Workspace.Id, c.Id, c.Revision, default);
        if (error == 502) Assert.Equal(502, (await Assert.ThrowsAsync<SocialConnectionException>(attempt)).Status);
        else await attempt();
        c = Assert.Single(await fixture.Service.ListAsync(fixture.Workspace.Id, default));
        Assert.False(c.PublishingPermissionGranted);
        Assert.Equal(expected, c.PublishingCapability!.Status);
        Assert.Equal(reason, c.PublishingCapability.Reason);
        await Assert.ThrowsAsync<SocialConnectionException>(() => fixture.Service.SetDestinationsAsync(fixture.Workspace.Id, fixture.Post.Id, [c.Id], default));
        await using var db = fixture.Db();
        var stored = await db.SocialConnections.SingleAsync();
        Assert.Equal(error != 401, stored.ProtectedCredential.Length > 0);
        if (error == 502)
        {
            fixture.Provider.Error = null;
            await fixture.Service.ValidateAsync(fixture.Workspace.Id, c.Id, c.Revision, default);
            Assert.True(Assert.Single(await fixture.Service.ListAsync(fixture.Workspace.Id, default)).PublishingPermissionGranted);
        }
    }

    [Theory]
    [InlineData(PublishingCapabilityStatus.Unverified)] [InlineData(PublishingCapabilityStatus.Denied)]
    public async Task ExplicitStateBlocksEvenIfLegacyBooleanIsInconsistent(PublishingCapabilityStatus status)
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Db();
        var row = await db.SocialConnections.SingleAsync(); row.PublishingPermissionGranted = true;
        row.PublishingCapabilityStatus = status; await db.SaveChangesAsync();
        Assert.False(Assert.Single(await fixture.Service.ListAsync(fixture.Workspace.Id, default)).PublishingPermissionGranted);
        await Assert.ThrowsAsync<SocialConnectionException>(() => fixture.Service.SetDestinationsAsync(fixture.Workspace.Id, fixture.Post.Id, [row.Id], default));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection sqlite = new("Data Source=:memory:");
        public readonly Workspace Workspace = Workspace.Create("Capabilities", WorkspaceType.Organization, "actor");
        public readonly SocialPost Post = SocialPost.Create("Draft", "No publication", SocialPlatform.Facebook);
        public readonly TimeProvider Clock = TimeProvider.System;
        public readonly Provider Provider = new();
        public SocialSecretProtector Secrets = null!;
        public SocialConnections Service = null!;
        private DbContextOptions<SocialFlowDbContext> options = null!;
        public string Purpose = "";
        public SocialFlowDbContext Db() => new(options, new ExplicitWorkspaceContext(Workspace.Id));
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); await f.sqlite.OpenAsync();
            f.options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(f.sqlite).Options;
            f.Secrets = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["SocialConnections:Encryption:ActiveKeyId"] = "test",
                ["SocialConnections:Encryption:Keys:test"] = Convert.ToBase64String(new byte[32]) }).Build());
            await using var db = f.Db(); await db.Database.MigrateAsync();
            db.Workspaces.Add(f.Workspace); db.WorkspaceMemberships.Add(WorkspaceMembership.Join(f.Workspace.Id, "actor"));
            db.SocialPosts.Add(f.Post);
            var row = new SocialConnection { WorkspaceId = f.Workspace.Id, Platform = SocialPlatform.Facebook, ExternalAccountId = "123",
                AccountType = "Page", ConnectionStatus = SocialConnectionStatus.Connected };
            f.Purpose = $"SocialFlow/SaaS/v1/{f.Workspace.Id}/{row.Id}/credential";
            row.ProtectedCredential = f.Secrets.Protect(JsonSerializer.Serialize(new SocialCredential("SECRET-page", null, null, null, [], new("SECRET-user", null))), f.Purpose);
            db.SocialConnections.Add(row); await db.SaveChangesAsync();
            f.Service = new(f.options, new Actor(), [f.Provider], f.Secrets, f.Clock);
            return f;
        }
        public ValueTask DisposeAsync() => sqlite.DisposeAsync();
    }
    private sealed class Actor : ICollaborationActor { public string UserId => "actor"; public string Email => "actor@example.test"; }
    private sealed class Provider : IWorkspaceOAuthProvider, IPublishingCapabilityValidator
    {
        public PublishingCapabilityStatus Status; public int? Error;
        public SocialPlatform Platform => SocialPlatform.Facebook; public bool Available => true; public bool SupportsPkce => false;
        public string AuthorizationUrl(string state, string? challenge) => throw new NotSupportedException();
        public Task<IReadOnlyList<SocialCandidate>> DiscoverAsync(string code, string? verifier, CancellationToken ct) => throw new NotSupportedException();
        public Task<SocialCredential> ValidateAsync(string id, string type, SocialCredential credential, CancellationToken ct) => throw new NotSupportedException();
        public Task<PublishingCapabilityValidation> ValidatePublishingCapabilityAsync(string id, string type, SocialCredential credential, CancellationToken ct)
        {
            if (Error is { } error) throw new SocialConnectionException(error, "Provider verification failed.");
            return Task.FromResult(new PublishingCapabilityValidation(credential, new(Status,
                Status == PublishingCapabilityStatus.Verified ? "PageCreateContentTaskVerified" : "PageCreateContentTaskMissing",
                "MetaUserAccounts", DateTimeOffset.UtcNow, Status == PublishingCapabilityStatus.Verified ? "PROFILE_PLUS_CREATE_CONTENT" : null)));
        }
    }
}
