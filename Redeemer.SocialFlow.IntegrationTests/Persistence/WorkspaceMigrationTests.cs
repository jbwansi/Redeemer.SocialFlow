using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Persistence;

public sealed class WorkspaceMigrationTests
{
    [Fact]
    public async Task UpgradePreservesEveryHistoricalColumnAndAllRelations_WithoutAssigningAnOwner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(connection).Options;
        await using var db = new SocialFlowDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync("20261010024512_AddVisualIdentityLibrary");
        var post = Guid.NewGuid(); var visual = Guid.NewGuid(); var logo = Guid.NewGuid();
        var audit = Guid.NewGuid(); var document = Guid.NewGuid(); var chunk = Guid.NewGuid(); var operation = Guid.NewGuid();
        var identity = new Guid("8f116275-3b02-442f-a930-719e33b10601");
        var now = "2024-02-03 12:34:56.1234567+02:00"; var hash = new string('c', 64);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SocialPosts (Id,Title,Content,Platform,Status,CreatedAt,UpdatedAt,IsDeleted,ScheduledAt,PublishedAt) VALUES ({post},'Historical','Original content',2,5,{now},{now},0,{now},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AiGenerations (Id,SocialPostId,Subject,Objective,Audience,Platform,Provider,Model,Warnings,GeneratedAt) VALUES ({audit},{post},'S','O','A',2,'Fake','Original','[]',{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO KnowledgeDocuments (Id,Title,PrimaryTheme,Themes,Status,Usages,SourceType,AuthorityLevel,Language,Version,IsActive,CreatedAt,UpdatedAt) VALUES ({document},'Source','theme','[\"theme\"]',2,'[1]',1,1,'fr',7,1,{now},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO KnowledgeChunks (Id,KnowledgeDocumentId,Content,ChunkIndex,CreatedAt) VALUES ({chunk},{document},'Original passage',0,{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO IdentityLogos (Id,IdentityId,StorageKey,ContentType,Width,Height,ByteLength,Sha256,IsReady,CreatedAt) VALUES ({logo},{identity},'original-logo','image/png',10,20,100,{hash},1,{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE VisualIdentities SET Name='Customized historical brand', Style='Custom style', Revision=17, LogoId={logo} WHERE Id={identity}");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PostVisuals (Id,SocialPostId,Origin,StorageKey,ContentType,Width,Height,ByteLength,Sha256,CreatedAt,ValidatedAt,IsReady,IsDeleted,SourceIdentityId,GenerationMetadataJson) VALUES ({visual},{post},1,'original-file','image/png',10,20,100,{hash},{now},{now},1,0,{identity},'{{}}')");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SocialPosts SET SelectedVisualId={visual} WHERE Id={post}");
        var snapshot = JsonSerializer.Serialize(new { VisualId = visual, StorageKey = "original-file", Sha256 = hash, ByteLength = 100 });
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PublicationOperations (OperationId,SocialPostId,Provider,Destination,Content,Platform,State,ClaimedAt,UpdatedAt,CompletedAt,ExternalId,Visual) VALUES ({operation},{post},'LinkedIn','private-target','Original publication',2,2,{now},{now},{now},'private-result',{snapshot})");
        var tables = new[] { "SocialPosts", "AiGenerations", "KnowledgeDocuments", "KnowledgeChunks", "PostVisuals", "VisualIdentities", "IdentityLogos", "PublicationOperations" };
        var before = new Dictionary<string, string>();
        foreach (var table in tables) before[table] = await Snapshot(connection, table);
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        foreach (var table in tables)
        {
            Assert.Equal(before[table], await Snapshot(connection, table));
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT count(*) FROM \"{table}\" WHERE WorkspaceId <> $workspace";
            command.Parameters.AddWithValue("$workspace", Workspace.HistoricalId);
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
        Assert.Empty(await db.WorkspaceMemberships.ToListAsync());
        Assert.Equal("Redeemer Holding", (await db.Workspaces.SingleAsync()).Name);
        Assert.Equal(WorkspaceStatus.Suspended, (await db.Workspaces.SingleAsync()).Status);
        using var check = connection.CreateCommand(); check.CommandText = "PRAGMA foreign_key_check";
        using (var reader = await check.ExecuteReaderAsync()) Assert.False(await reader.ReadAsync());
        check.CommandText = "PRAGMA foreign_keys"; Assert.Equal(1L, await check.ExecuteScalarAsync());
        check.CommandText = "SELECT dflt_value FROM pragma_table_info('SocialPosts') WHERE name='WorkspaceId'";
        Assert.Equal(DBNull.Value, await check.ExecuteScalarAsync());
    }
    private static async Task<string> Snapshot(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM \"{table}\"";
        using var reader = await command.ExecuteReaderAsync(); var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = new SortedDictionary<string, string?>();
            for (var i = 0; i < reader.FieldCount; i++) if (reader.GetName(i) != "WorkspaceId")
                values[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i).ToString();
            rows.Add(JsonSerializer.Serialize(values));
        }
        return string.Join("\n", rows.Order());
    }
}
