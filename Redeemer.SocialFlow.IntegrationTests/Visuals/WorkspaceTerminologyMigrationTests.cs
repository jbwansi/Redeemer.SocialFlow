using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Visuals;

public sealed class WorkspaceTerminologyMigrationTests
{
    [Fact]
    public async Task AdditiveMigrationPreservesPostsAndAuditAndCreatesNoOfficialTerms()
    {
        var path = Path.Combine(Path.GetTempPath(), "terminology-migration-" + Guid.NewGuid() + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
            await using var db = TestWorkspace.Db(options);
            var assembly = db.GetService<IMigrationsAssembly>();
            var pair = assembly.Migrations.Single(x => x.Key.EndsWith("_AddWorkspaceTerminology"));
            var migration = assembly.CreateMigration(pair.Value, db.Database.ProviderName!);
            Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation or AddColumnOperation { IsNullable: true }));
            var previous = assembly.Migrations.Keys.Where(x => string.CompareOrdinal(x, pair.Key) < 0).Last();
            await db.GetService<IMigrator>().MigrateAsync(previous);
            var post = SocialPost.Create("scrum historical", "Existing content", SocialPlatform.Facebook);
            db.Add(post); await db.SaveChangesAsync();
            var auditId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var workspaceId = post.WorkspaceId;
            await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO AiGenerations
                (Id,WorkspaceId,SocialPostId,Subject,Objective,Audience,Platform,Provider,Model,Warnings,GeneratedAt)
                VALUES ({auditId},{workspaceId},{post.Id},'Subject','Goal','Audience',1,'Fake','Fake','[]',{now})");
            var before = JsonSerializer.Serialize(await db.SocialPosts.AsNoTracking().ToListAsync());
            await db.Database.MigrateAsync();
            Assert.Equal(before, JsonSerializer.Serialize(await db.SocialPosts.AsNoTracking().ToListAsync()));
            var audit = await db.AiGenerations.SingleAsync();
            Assert.Equal(auditId, audit.Id); Assert.Null(audit.TerminologyEvidenceJson);
            Assert.Empty(await db.WorkspaceTerms.ToListAsync()); Assert.Empty(await db.WorkspaceTermRevisions.ToListAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
