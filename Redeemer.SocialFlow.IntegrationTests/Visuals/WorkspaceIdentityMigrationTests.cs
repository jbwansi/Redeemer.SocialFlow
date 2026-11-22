using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Visuals;

public sealed class WorkspaceIdentityMigrationTests
{
    [Fact]
    public async Task MigrationIsAdditivePreservesExistingIdentitiesAndDoesNotApproveHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), "identity-migration-" + Guid.NewGuid() + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
            await using var db = TestWorkspace.Db(options);
            var migrations = db.GetService<IMigrationsAssembly>();
            var pair = migrations.Migrations.Single(x => x.Key.EndsWith("_AddWorkspaceIdentities"));
            var migration = migrations.CreateMigration(pair.Value, db.Database.ProviderName!);
            Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation));
            var previous = migrations.Migrations.Keys.Where(x => string.CompareOrdinal(x, pair.Key) < 0).Last();
            await db.GetService<IMigrator>().MigrateAsync(previous);
            var before = JsonSerializer.Serialize(await db.VisualIdentities.AsNoTracking().ToListAsync());
            await db.Database.MigrateAsync();
            Assert.Equal(before, JsonSerializer.Serialize(await db.VisualIdentities.AsNoTracking().ToListAsync()));
            Assert.Empty(await db.WorkspaceEditorialProfiles.ToListAsync());
            Assert.Empty(await db.VisualIdentityApprovals.ToListAsync());
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Contains(pair.Key, await db.Database.GetAppliedMigrationsAsync());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
