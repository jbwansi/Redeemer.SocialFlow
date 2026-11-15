using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Persistence;

public sealed class PublishingCapabilityMigrationTests
{
    [Fact]
    public async Task AdditiveMigrationPreservesLegacyGrantsAndCiphertextWithoutInventingEvidence()
    {
        await using var sqlite = new SqliteConnection("Data Source=:memory:"); await sqlite.OpenAsync();
        var options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(sqlite).Options;
        var workspace = Workspace.Create("Legacy", WorkspaceType.Organization, "actor");
        await using var db = new SocialFlowDbContext(options, new ExplicitWorkspaceContext(workspace.Id));
        await db.GetService<IMigrator>().MigrateAsync("20261103152618_ConsolidateSocialConnectionIdentities");
        db.Workspaces.Add(workspace); await db.SaveChangesAsync();
        foreach (var granted in new[] { true, false })
        {
            var id = Guid.NewGuid(); var revision = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SocialConnections (Id,WorkspaceId,Platform,ExternalAccountId,DisplayName,AccountType,ConnectionStatus,ConnectedByUserId,ConnectedAtUtc,ProtectedCredential,PublishingPermissionGranted,Revision) VALUES ({id},{workspace.Id},1,{granted.ToString()},'Legacy','Page',1,'actor',{now},'unchanged-ciphertext',{granted},{revision})");
        }
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var rows = await db.SocialConnections.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => {
            Assert.Equal("unchanged-ciphertext", row.ProtectedCredential);
            Assert.Null(row.PublishingCapabilityStatus); Assert.Null(row.PublishingCapabilitySource);
            Assert.Null(row.PublishingCapabilityTask); Assert.Null(row.PublishingCapabilityCheckedAtUtc);
            Assert.Equal(bool.Parse(row.ExternalAccountId), row.PublishingPermissionGranted);
            Assert.Equal(row.PublishingPermissionGranted, row.CanPublish);
        });
    }
}
