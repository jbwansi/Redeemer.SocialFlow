using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.Knowledge;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure;
using Redeemer.SocialFlow.Infrastructure.Knowledge;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Persistence;

public sealed class KnowledgePassageSearchTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private SocialFlowDbContext _context = null!;
    private SqliteKnowledgePassageSearch _search = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _context = new(new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(_connection).Options);
        await _context.Database.MigrateAsync();
        _search = new(_context);
    }

    private KnowledgeDocument Document(KnowledgeDocumentStatus status = KnowledgeDocumentStatus.Approved,
        bool active = true, bool generation = true, string theme = "Theme", string language = "fr")
    {
        var document = KnowledgeDocument.Create("Document", "Primary", ["Primary", theme],
            generation ? [KnowledgeUsage.Research, KnowledgeUsage.Generation] : [KnowledgeUsage.Research, KnowledgeUsage.Internal],
            SourceType.Academic, AuthorityLevel.High, language, 2, active);
        document.ChangeStatus(status);
        _context.KnowledgeDocuments.Add(document);
        return document;
    }

    private KnowledgeChunk Chunk(KnowledgeDocument document, string content, int index = 0)
    {
        var chunk = KnowledgeChunk.Create(document.Id, content, index, 3, "Section");
        _context.KnowledgeChunks.Add(chunk);
        return chunk;
    }

    public static IEnumerable<object[]> EligibilityCases() =>
        from status in Enum.GetValues<KnowledgeDocumentStatus>()
        from active in new[] { true, false }
        from generation in new[] { true, false }
        select new object[] { status, active, generation };

    [Theory]
    [MemberData(nameof(EligibilityCases))]
    public async Task Eligibility_IsRequiredWithoutFallback(KnowledgeDocumentStatus status, bool active, bool generation)
    {
        var document = Document(status, active, generation);
        var chunk = Chunk(document, "knowledge");
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        var result = await _search.SearchForGenerationAsync(new("knowledge"));
        if (document.CanBeUsedForGeneration) Assert.Equal(chunk.Id, Assert.Single(result).KnowledgeChunkId);
        else Assert.Empty(result);
        Assert.Empty(_context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task FiltersAndEligibility_PrecedeLimit_AndProvenanceIsFaithful()
    {
        Chunk(Document(generation: false), "alpha beta gamma");
        Chunk(Document(theme: "Different"), "alpha beta gamma");
        Chunk(Document(language: "en"), "alpha beta gamma");
        Chunk(Document(active: false), "alpha beta gamma");
        var document = Document();
        var chunk = Chunk(document, " Original ALPHA\ntext ", 4);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        var result = Assert.Single(await _search.SearchForGenerationAsync(new("alpha beta gamma", 1, " Theme ", " fr ")));
        Assert.Equal(new KnowledgePassage(chunk.Id, document.Id, document.Title, document.Version,
            document.SourceType, document.AuthorityLevel, document.Language, chunk.Content,
            chunk.ChunkIndex, chunk.PageNumber, chunk.Section), result);
        Assert.Empty(await _search.SearchForGenerationAsync(new("alpha", 5, "theme", "fr")));
        Assert.Empty(await _search.SearchForGenerationAsync(new("alpha", 5, "Theme", "FR")));
        Assert.Empty(_context.ChangeTracker.Entries());
        Assert.Equal(0, await _context.SaveChangesAsync());
    }

    [Fact]
    public async Task Ranking_CountsDistinctWholeWordsAndHasStableTieBreaks()
    {
        var document = Document();
        var lower = Chunk(document, "alpha alpha ALPHA", 0);
        var best = Chunk(document, "ALPHA beta", 3);
        var tie = Chunk(document, "beta alpha", 1);
        var sameIndex = Chunk(document, "alpha beta", 1);
        var otherDocument = Chunk(Document(), "alpha beta", 1);
        Chunk(document, "alphabet unrelated", 5);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        var expected = new[] { best, tie, sameIndex, otherDocument }
            .OrderBy(x => x.KnowledgeDocumentId).ThenBy(x => x.ChunkIndex).ThenBy(x => x.Id)
            .Select(x => x.Id).Append(lower.Id).ToArray();
        var result = await _search.SearchForGenerationAsync(new("alpha ALPHA, beta!", 10));
        Assert.Equal(expected, result.Select(x => x.KnowledgeChunkId));
        Assert.Equal(expected.Take(2), (await _search.SearchForGenerationAsync(new("alpha beta", 2))).Select(x => x.KnowledgeChunkId));
        Assert.Equal(result, await _search.SearchForGenerationAsync(new("alpha beta", 10)));
        Assert.Empty(await _search.SearchForGenerationAsync(new("missing")));
        Assert.Empty(await _search.SearchForGenerationAsync(new("!!!")));
    }

    [Fact]
    public async Task Search_UsesCurrentEligibilityAndPreservesNullableLocation()
    {
        var document = Document();
        var chunk = KnowledgeChunk.Create(document.Id, "ÉTHIQUE 2026", 0);
        _context.KnowledgeChunks.Add(chunk);
        await _context.SaveChangesAsync();
        var result = Assert.Single(await _search.SearchForGenerationAsync(new("éthique")));
        Assert.Null(result.PageNumber);
        Assert.Null(result.Section);
        Assert.Empty(await _search.SearchForGenerationAsync(new("ethique")));
        document.SetActive(false);
        await _context.SaveChangesAsync();
        Assert.Empty(await _search.SearchForGenerationAsync(new("éthique")));
    }

    [Theory]
    [InlineData(null, 5, null, null)]
    [InlineData("", 5, null, null)]
    [InlineData(" \t", 5, null, null)]
    [InlineData("word", 0, null, null)]
    [InlineData("word", -1, null, null)]
    [InlineData("word", 5, " ", null)]
    [InlineData("word", 5, null, " ")]
    public async Task InvalidRequests_AreRejected(string? query, int limit, string? theme, string? language) =>
        await Assert.ThrowsAsync<ArgumentException>(() => _search.SearchForGenerationAsync(new(query!, limit, theme, language)));

    [Fact]
    public async Task NullAndCancellation_AreRespected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _search.SearchForGenerationAsync(null!));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _search.SearchForGenerationAsync(new("word"), cancelled.Token));
    }

    [Fact]
    public void DependencyInjection_ResolvesScopedSearch()
    {
        using var provider = new ServiceCollection().AddPersistence("Data Source=:memory:").BuildServiceProvider();
        using var scope = provider.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IKnowledgePassageSearch>();
        Assert.IsType<SqliteKnowledgePassageSearch>(search);
        Assert.Same(search, scope.ServiceProvider.GetRequiredService<IKnowledgePassageSearch>());
        using var other = provider.CreateScope();
        Assert.NotSame(search, other.ServiceProvider.GetRequiredService<IKnowledgePassageSearch>());
    }

    public async Task DisposeAsync()
    {
        if (_context is not null) await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
