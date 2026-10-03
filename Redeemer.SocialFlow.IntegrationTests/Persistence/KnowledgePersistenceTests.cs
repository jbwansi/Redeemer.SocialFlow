using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Persistence;

public sealed class KnowledgePersistenceTests
{
    [Theory]
    [InlineData(SourceType.InternalOfficial, AuthorityLevel.Low, KnowledgeDocumentStatus.Draft, false)]
    [InlineData(SourceType.OfficialExternal, AuthorityLevel.Medium, KnowledgeDocumentStatus.Review, true)]
    [InlineData(SourceType.Book, AuthorityLevel.High, KnowledgeDocumentStatus.Approved, true)]
    [InlineData(SourceType.Academic, AuthorityLevel.Low, KnowledgeDocumentStatus.Rejected, true)]
    [InlineData(SourceType.ProfessionalArticle, AuthorityLevel.Medium, KnowledgeDocumentStatus.Archived, false)]
    [InlineData(SourceType.PersonalNotes, AuthorityLevel.High, KnowledgeDocumentStatus.Approved, false)]
    [InlineData(SourceType.Other, AuthorityLevel.Low, KnowledgeDocumentStatus.Approved, true)]
    public async Task RoundTrip_PreservesMetadataProtectedCollectionsAndChunks(SourceType source, AuthorityLevel authority,
        KnowledgeDocumentStatus status, bool active)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(connection).Options;
        var clock = new Clock(new DateTimeOffset(2026, 10, 2, 12, 30, 0, TimeSpan.FromHours(2)).AddTicks(1234));
        var document = KnowledgeDocument.Create("Titre", "Éthique", ["Éthique", "Citation \"texte\"\n"],
            [KnowledgeUsage.Generation, KnowledgeUsage.Research, KnowledgeUsage.Internal], source, authority, "fr", 3, active, clock);
        clock.Now = clock.Now.AddHours(1);
        document.ChangeStatus(status);
        var chunks = new[] {
            KnowledgeChunk.Create(document.Id, " Contenu\noriginal ", 0, 2, "Introduction", clock),
            KnowledgeChunk.Create(document.Id, "Suite", 1, timeProvider: clock)
        };
        await using (var write = new SocialFlowDbContext(options))
        {
            // Only the isolated in-memory test database is migrated.
            await write.Database.MigrateAsync();
            Assert.False(write.Database.HasPendingModelChanges());
            write.KnowledgeDocuments.Add(document);
            write.KnowledgeChunks.AddRange(chunks);
            Assert.Equal(3, await write.SaveChangesAsync());
        }
        await using (var read = new SocialFlowDbContext(options))
        {
            var saved = await read.KnowledgeDocuments.SingleAsync();
            Assert.Equal(document.Id, saved.Id);
            Assert.Equal(document.Title, saved.Title);
            Assert.Equal(document.PrimaryTheme, saved.PrimaryTheme);
            Assert.Equal(document.Themes, saved.Themes);
            Assert.Equal(document.Usages, saved.Usages);
            Assert.Equal(source, saved.SourceType);
            Assert.Equal(authority, saved.AuthorityLevel);
            Assert.Equal(status, saved.Status);
            Assert.Equal(active, saved.IsActive);
            Assert.Equal("fr", saved.Language);
            Assert.Equal(3, saved.Version);
            Assert.Equal(document.CreatedAt.ToString("O"), saved.CreatedAt.ToString("O"));
            Assert.Equal(document.UpdatedAt.ToString("O"), saved.UpdatedAt.ToString("O"));
            Assert.Equal(document.CanBeUsedForGeneration, saved.CanBeUsedForGeneration);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)saved.Themes).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<KnowledgeUsage>)saved.Usages).Clear());
            var storedChunks = await read.KnowledgeChunks.OrderBy(x => x.ChunkIndex).ToListAsync();
            Assert.Equal(2, storedChunks.Count);
            for (var i = 0; i < chunks.Length; i++)
            {
                Assert.Equal(chunks[i].Id, storedChunks[i].Id);
                Assert.Equal(document.Id, storedChunks[i].KnowledgeDocumentId);
                Assert.Equal(chunks[i].Content, storedChunks[i].Content);
                Assert.Equal(chunks[i].ChunkIndex, storedChunks[i].ChunkIndex);
                Assert.Equal(chunks[i].PageNumber, storedChunks[i].PageNumber);
                Assert.Equal(chunks[i].Section, storedChunks[i].Section);
                Assert.Equal(chunks[i].CreatedAt.ToString("O"), storedChunks[i].CreatedAt.ToString("O"));
            }
            Assert.False(read.ChangeTracker.HasChanges());
            Assert.Equal(0, await read.SaveChangesAsync());
            var before = DateTimeOffset.UtcNow;
            saved.Update("Revised", "New", ["New", "Other"], [], source, authority, "en", 4);
            Assert.InRange(saved.UpdatedAt, before, DateTimeOffset.UtcNow);
            await read.SaveChangesAsync();
        }
        await using var verify = new SocialFlowDbContext(options);
        var revised = await verify.KnowledgeDocuments.SingleAsync();
        Assert.Equal(new[] { "New", "Other" }, revised.Themes);
        Assert.Empty(revised.Usages);
        Assert.Equal("Revised", revised.Title);
        Assert.Equal(4, revised.Version);
        Assert.Equal("en", revised.Language);
        Assert.Contains(revised.PrimaryTheme, revised.Themes);
    }

    [Fact]
    public async Task ForeignKey_RejectsOrphanChunkAndRestrictsDocumentDeletion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(connection).Options;
        await using var context = new SocialFlowDbContext(options);
        await context.Database.MigrateAsync();
        context.KnowledgeChunks.Add(KnowledgeChunk.Create(Guid.NewGuid(), "Orphan", 0));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        var document = KnowledgeDocument.Create("T", "Theme", ["Theme"], [], SourceType.Other, AuthorityLevel.Low, "fr");
        context.KnowledgeDocuments.Add(document);
        context.KnowledgeChunks.Add(KnowledgeChunk.Create(document.Id, "Body", 0));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        context.KnowledgeDocuments.Remove(await context.KnowledgeDocuments.SingleAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        Assert.Single(await context.KnowledgeDocuments.ToListAsync());
        Assert.Single(await context.KnowledgeChunks.ToListAsync());
    }

    [Fact]
    public void DependencyInjection_SharesExistingScopedContext()
    {
        using var provider = new ServiceCollection().AddPersistence("Data Source=:memory:").BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IKnowledgeDbContext>();
        Assert.Same(scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>(), context);
        Assert.Same(scope.ServiceProvider.GetRequiredService<ISocialFlowDbContext>(), context);
        using var other = provider.CreateScope();
        Assert.NotSame(context, other.ServiceProvider.GetRequiredService<IKnowledgeDbContext>());
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
