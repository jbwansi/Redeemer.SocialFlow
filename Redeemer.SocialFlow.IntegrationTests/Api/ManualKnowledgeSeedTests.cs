using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Redeemer.SocialFlow.Api.Development;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed class ManualKnowledgeSeedTests
{
    [Fact]
    public async Task Seed_PersistsFictionalEligibleDocumentAndTwoChunks_OnlyOnce()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(connection).Options;
        await using (var setup = new SocialFlowDbContext(options))
            await setup.Database.MigrateAsync();

        Guid originalId;
        await using (var first = new SocialFlowDbContext(options))
        {
            await ManualKnowledgeSeed.RunAsync([ManualKnowledgeSeed.Argument], new Environment("Development"), first, TimeProvider.System);
            originalId = (await first.KnowledgeDocuments.SingleAsync()).Id;
        }
        await using (var second = new SocialFlowDbContext(options))
            await ManualKnowledgeSeed.RunAsync([ManualKnowledgeSeed.Argument], new Environment("Development"), second, TimeProvider.System);

        await using var read = new SocialFlowDbContext(options);
        var document = Assert.Single(await read.KnowledgeDocuments.ToListAsync());
        Assert.Equal(originalId, document.Id);
        Assert.Equal(ManualKnowledgeSeed.Title, document.Title);
        Assert.Equal("fr", document.Language);
        Assert.Equal("accompagnement des bénévoles", document.PrimaryTheme);
        Assert.Equal(new[] { document.PrimaryTheme }, document.Themes);
        Assert.Equal(new[] { KnowledgeUsage.Generation }, document.Usages);
        Assert.Equal(KnowledgeDocumentStatus.Approved, document.Status);
        Assert.True(document.IsActive);
        Assert.True(document.CanBeUsedForGeneration);
        Assert.Equal(SourceType.Other, document.SourceType);
        Assert.Equal(AuthorityLevel.Low, document.AuthorityLevel);
        var chunks = await read.KnowledgeChunks.OrderBy(chunk => chunk.ChunkIndex).ToListAsync();
        Assert.Equal(new[] { 0, 1 }, chunks.Select(chunk => chunk.ChunkIndex));
        Assert.Equal(new[] {
            "Pour accompagner les bénévoles, clarifier leur rôle, écouter leurs attentes et identifier leurs besoins de soutien.",
            "Prévoir des échanges réguliers avec les bénévoles, reconnaître leur contribution et adapter les responsabilités à leurs disponibilités."
        }, chunks.Select(chunk => chunk.Content));
        Assert.All(chunks, chunk => Assert.Equal(document.Id, chunk.KnowledgeDocumentId));
        Assert.Empty(await read.SocialPosts.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await read.AiGenerations.ToListAsync());
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", false)]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    public async Task Seed_IsOptInAndDevelopmentOnly_BeforeAnyDatabaseAccess(string environment, bool requested)
    {
        // Unopened, unmigrated database: any database access would fail this test.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = new SocialFlowDbContext(new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(connection).Options);
        var args = requested ? new[] { ManualKnowledgeSeed.Argument } : Array.Empty<string>();
        Assert.Equal(requested, ManualKnowledgeSeed.IsRequested(args));
        if (requested)
            await Assert.ThrowsAsync<InvalidOperationException>(() => ManualKnowledgeSeed.RunAsync(args, new Environment(environment), db, TimeProvider.System));
        else
            await ManualKnowledgeSeed.RunAsync(args, new Environment(environment), db, TimeProvider.System);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
