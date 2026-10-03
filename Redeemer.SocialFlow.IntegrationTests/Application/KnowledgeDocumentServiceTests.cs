using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Knowledge;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Knowledge;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Application;

public sealed class KnowledgeDocumentServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<SocialFlowDbContext> _options = null!;
    private static CreateKnowledgeDocumentRequest Request => new("Title", "Primary", ["Primary", "Secondary"],
        [KnowledgeUsage.Generation, KnowledgeUsage.Research], SourceType.Other, AuthorityLevel.Low, "fr",
        [new("Second", 1, 3, "Section"), new(" First\n", 0)], Version: 2);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(_connection).Options;
        await using var db = new SocialFlowDbContext(_options);
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();

    [Fact]
    public async Task CreateAndRead_PreserveDraftMetadataAndChunksWithoutTrackingReads()
    {
        KnowledgeDocumentDetailsDto created;
        var clock = new Clock();
        await using (var write = new SocialFlowDbContext(_options))
        {
            created = await new KnowledgeDocumentService(write, clock).CreateAsync(Request);
            Assert.Equal(KnowledgeDocumentStatus.Draft, created.Document.Status);
            Assert.False((await write.KnowledgeDocuments.SingleAsync()).CanBeUsedForGeneration);
            Assert.Equal(clock.GetUtcNow(), created.Document.CreatedAt);
        }
        await using var read = new SocialFlowDbContext(_options);
        var service = new KnowledgeDocumentService(read);
        var result = (await service.GetByIdAsync(created.Document.Id))!;
        Assert.Equal(created.Document.Id, result.Document.Id);
        Assert.Equal(Request.Title, result.Document.Title);
        Assert.Equal(Request.PrimaryTheme, result.Document.PrimaryTheme);
        Assert.Equal(Request.Themes, result.Document.Themes);
        Assert.Equal(Request.Usages, result.Document.Usages);
        Assert.Equal(Request.SourceType, result.Document.SourceType);
        Assert.Equal(Request.AuthorityLevel, result.Document.AuthorityLevel);
        Assert.Equal(Request.Language, result.Document.Language);
        Assert.Equal(Request.Version, result.Document.Version);
        Assert.True(result.Document.IsActive);
        Assert.Equal(KnowledgeDocumentStatus.Draft, result.Document.Status);
        Assert.Equal(created.Document.UpdatedAt, result.Document.UpdatedAt);
        Assert.Equal(created.Chunks, result.Chunks);
        Assert.Equal(new[] { " First\n", "Second" }, result.Chunks.Select(x => x.Content));
        Assert.All(result.Chunks, chunk => Assert.Equal(result.Document.Id, chunk.KnowledgeDocumentId));
        Assert.Equal(3, result.Chunks[1].PageNumber);
        Assert.Equal("Section", result.Chunks[1].Section);
        Assert.Empty(read.ChangeTracker.Entries());
        Assert.Null(await service.GetByIdAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task DomainValidation_DoesNotAttachOrPersistPartialCreation(int kind)
    {
        await using var db = new SocialFlowDbContext(_options);
        var request = kind switch
        {
            0 => Request with { Title = " " },
            1 => Request with { Themes = ["Other"] },
            2 => Request with { SourceType = (SourceType)999 },
            _ => Request with { Chunks = [new("Valid", 0), new(" ", 1)] }
        };
        await Assert.ThrowsAsync<DomainException>(() => new KnowledgeDocumentService(db).CreateAsync(request));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.KnowledgeDocuments.ToListAsync());
        Assert.Empty(await db.KnowledgeChunks.ToListAsync());
    }

    [Fact]
    public async Task SaveFailure_RollsBackDocumentAndChunks()
    {
        await using (var db = new SocialFlowDbContext(_options))
        {
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_chunk BEFORE INSERT ON KnowledgeChunks WHEN NEW.ChunkIndex = 1 BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
            await Assert.ThrowsAsync<DbUpdateException>(() => new KnowledgeDocumentService(db).CreateAsync(Request));
        }
        await using var read = new SocialFlowDbContext(_options);
        Assert.Empty(await read.KnowledgeDocuments.ToListAsync());
        Assert.Empty(await read.KnowledgeChunks.ToListAsync());
    }

    [Fact]
    public async Task List_FiltersBeforePaging_WithStableTiesAndTotalCount()
    {
        var ids = new List<Guid>();
        await using (var db = new SocialFlowDbContext(_options))
        {
            var service = new KnowledgeDocumentService(db, new Clock());
            for (var i = 0; i < 5; i++)
                ids.Add((await service.CreateAsync(Request)).Document.Id);
            await service.CreateAsync(Request with { Language = "en" });
            await service.CreateAsync(Request with { Themes = ["Primary"] });
            var approved = await service.CreateAsync(Request);
            (await db.KnowledgeDocuments.SingleAsync(x => x.Id == approved.Document.Id)).ChangeStatus(KnowledgeDocumentStatus.Approved);
            await db.SaveChangesAsync();
        }
        await using var read = new SocialFlowDbContext(_options);
        var reader = new KnowledgeDocumentService(read);
        var filter = new ListKnowledgeDocumentsRequest(1, 2, KnowledgeDocumentStatus.Draft, " Secondary ", " fr ");
        var first = await reader.ListAsync(filter);
        var second = await reader.ListAsync(filter with { PageNumber = 2 });
        var third = await reader.ListAsync(filter with { PageNumber = 3 });
        Assert.Equal(5, first.TotalCount);
        Assert.Equal(1, first.PageNumber);
        Assert.Equal(2, first.PageSize);
        Assert.Equal(ids.OrderBy(x => x), first.Items.Concat(second.Items).Concat(third.Items).Select(x => x.Id));
        Assert.Equal(first.Items.Select(x => x.Id), (await reader.ListAsync(filter)).Items.Select(x => x.Id));
        Assert.Empty((await reader.ListAsync(filter with { PageNumber = int.MaxValue })).Items);
        Assert.Equal(8, (await reader.ListAsync()).TotalCount);
        Assert.Single((await reader.ListAsync(new(Status: KnowledgeDocumentStatus.Approved))).Items);
        Assert.Empty((await reader.ListAsync(new(Theme: "secondary"))).Items);
        Assert.Empty(read.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(0, 20, null, null, null)]
    [InlineData(1, 0, null, null, null)]
    [InlineData(1, 101, null, null, null)]
    [InlineData(1, 20, (KnowledgeDocumentStatus)999, null, null)]
    [InlineData(1, 20, null, " ", null)]
    [InlineData(1, 20, null, null, " ")]
    public async Task List_RejectsInvalidArguments(int page, int size, KnowledgeDocumentStatus? status, string? theme, string? language)
    {
        await using var db = new SocialFlowDbContext(_options);
        await Assert.ThrowsAsync<ArgumentException>(() => new KnowledgeDocumentService(db).ListAsync(new(page, size, status, theme, language)));
    }

    [Fact]
    public async Task CancellationAndNullRequests_AreRejectedWithoutChanges()
    {
        await using var db = new SocialFlowDbContext(_options);
        var service = new KnowledgeDocumentService(db);
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(Request, source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetByIdAsync(Guid.NewGuid(), source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ListAsync(cancellationToken: source.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateAsync(Request with { Chunks = null! }));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task List_OrdersByCreationInstantBeforeId()
    {
        await using var db = new SocialFlowDbContext(_options);
        var old = await new KnowledgeDocumentService(db, new Clock()).CreateAsync(Request);
        var recent = await new KnowledgeDocumentService(db, new Clock { Now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero) }).CreateAsync(Request);
        var result = await new KnowledgeDocumentService(db).ListAsync(new(PageSize: 1));
        Assert.Equal(recent.Document.Id, Assert.Single(result.Items).Id);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(old.Document.Id, Assert.Single((await new KnowledgeDocumentService(db).ListAsync(new(PageNumber: 2, PageSize: 1))).Items).Id);
    }

    [Theory]
    [InlineData(KnowledgeDocumentStatus.Draft)]
    [InlineData(KnowledgeDocumentStatus.Review)]
    [InlineData(KnowledgeDocumentStatus.Approved)]
    [InlineData(KnowledgeDocumentStatus.Rejected)]
    [InlineData(KnowledgeDocumentStatus.Archived)]
    public async Task Lifecycle_UsesDomainPermissionsAndPersists(KnowledgeDocumentStatus initial)
    {
        Guid id;
        DateTimeOffset createdAt;
        await using (var setup = new SocialFlowDbContext(_options))
        {
            var created = await new KnowledgeDocumentService(setup, new Clock()).CreateAsync(Request);
            id = created.Document.Id;
            createdAt = created.Document.CreatedAt;
            (await setup.KnowledgeDocuments.SingleAsync()).ChangeStatus(initial);
            await setup.SaveChangesAsync();
        }
        await using (var write = new SocialFlowDbContext(_options))
        {
            var service = new KnowledgeDocumentService(write);
            var inactive = await service.SetActiveAsync(id, false);
            Assert.False(inactive.IsActive);
            Assert.Equal(initial, inactive.Status);
            var approved = await service.ApproveAsync(id);
            Assert.Equal(KnowledgeDocumentStatus.Approved, approved.Status);
            Assert.False(approved.IsActive);
            Assert.Equal(Request.Usages, approved.Usages);
            var active = await service.SetActiveAsync(id, true);
            Assert.True(active.IsActive);
            Assert.Equal(KnowledgeDocumentStatus.Approved, active.Status);
        }
        await using var read = new SocialFlowDbContext(_options);
        var saved = (await new KnowledgeDocumentService(read).GetByIdAsync(id))!;
        Assert.Equal(KnowledgeDocumentStatus.Approved, saved.Document.Status);
        Assert.True(saved.Document.IsActive);
        Assert.Equal(createdAt, saved.Document.CreatedAt);
        Assert.NotEqual(createdAt, saved.Document.UpdatedAt);
        Assert.Equal(Request.Usages, saved.Document.Usages);
        Assert.Equal(2, saved.Chunks.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_ObservesPersistedLifecycleAndDoesNotGrantGenerationUsage(bool generationUsage)
    {
        Guid id;
        await using (var setup = new SocialFlowDbContext(_options))
            id = (await new KnowledgeDocumentService(setup).CreateAsync(Request with
            { Usages = generationUsage ? [KnowledgeUsage.Generation] : [KnowledgeUsage.Research] })).Document.Id;
        await AssertSearch(false);
        await using (var write = new SocialFlowDbContext(_options))
        {
            var approved = await new KnowledgeDocumentService(write).ApproveAsync(id);
            Assert.Equal(generationUsage, approved.Usages.Contains(KnowledgeUsage.Generation));
        }
        await AssertSearch(generationUsage);
        await using (var write = new SocialFlowDbContext(_options))
            await new KnowledgeDocumentService(write).SetActiveAsync(id, false);
        await AssertSearch(false);
        await using (var write = new SocialFlowDbContext(_options))
            await new KnowledgeDocumentService(write).SetActiveAsync(id, true);
        await AssertSearch(generationUsage);

        async Task AssertSearch(bool included)
        {
            await using var read = new SocialFlowDbContext(_options);
            var passages = await new SqliteKnowledgePassageSearch(read).SearchForGenerationAsync(new("First"));
            if (included) Assert.Equal(id, Assert.Single(passages).KnowledgeDocumentId);
            else Assert.Empty(passages);
        }
    }

    [Fact]
    public async Task Domain_InvalidStatusIsRejectedWithoutChangingPersistedDocument()
    {
        await using (var write = new SocialFlowDbContext(_options))
        {
            await new KnowledgeDocumentService(write).CreateAsync(Request);
            var document = await write.KnowledgeDocuments.SingleAsync();
            var updated = document.UpdatedAt;
            Assert.Throws<DomainException>(() => document.ChangeStatus((KnowledgeDocumentStatus)999));
            Assert.Equal(updated, document.UpdatedAt);
            Assert.Equal(0, await write.SaveChangesAsync());
        }
        await using var read = new SocialFlowDbContext(_options);
        Assert.Equal(KnowledgeDocumentStatus.Draft, (await read.KnowledgeDocuments.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Lifecycle_MissingAndCancellationFollowMutationConventions(bool approve)
    {
        await using var db = new SocialFlowDbContext(_options);
        var service = new KnowledgeDocumentService(db);
        var missing = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<KnowledgeDocumentNotFoundException>(() => Mutate(missing, default));
        Assert.Equal(missing, error.DocumentId);
        var created = await service.CreateAsync(Request);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Mutate(created.Document.Id, cancellation.Token));
        db.ChangeTracker.Clear();
        var saved = (await service.GetByIdAsync(created.Document.Id))!;
        Assert.Equal(KnowledgeDocumentStatus.Draft, saved.Document.Status);
        Assert.True(saved.Document.IsActive);

        Task<KnowledgeDocumentDto> Mutate(Guid id, CancellationToken token) => approve
            ? service.ApproveAsync(id, token) : service.SetActiveAsync(id, false, token);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; init; } = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
