using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Visuals;
using SkiaSharp;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Visuals;

public sealed class PostVisualIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "visual-integration-" + Guid.NewGuid().ToString("N"));
    private DbContextOptions<SocialFlowDbContext> _options = null!;
    private LocalVisualStorage _storage = null!;
    private readonly Clock _clock = new();
    private readonly VisualLimits _limits = new();
    private SocialFlowDbContext Db() => new(_options);
    private SqlitePostVisualStore Store(IVisualStorage? storage = null) => new(_options, storage ?? _storage, _clock);
    private PostVisualService Service(IVisualStorage? storage = null) => new(Store(storage), storage ?? _storage, new SkiaVisualValidator(_limits), _limits);
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "test.db")};Pooling=False;Default Timeout=10").Options;
        _storage = new(new() { RootPath = Path.Combine(_root, "files") }, _limits);
        await using var db = Db();
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() { Directory.Delete(_root, true); return Task.CompletedTask; }
    private async Task<Guid> Seed()
    {
        var post = SocialPost.Create("Title", "Content", SocialPlatform.LinkedIn, new FixedClock(_clock.Now.AddDays(-2)));
        post.Update("Title", "Content", null, "Existing brief", "https://example.com/legacy.png");
        await using var db = Db();
        db.SocialPosts.Add(post);
        await db.SaveChangesAsync();
        return post.Id;
    }
    private Task<VisualDto> Upload(Guid id, PostVisualService? service = null) =>
        (service ?? Service()).UploadAsync(id, new MemoryStream(Image()));

    [Fact]
    public async Task ImportSelectReadAndRegeneratePreserveOriginalAndHistory()
    {
        var post = await Seed();
        var first = await Upload(post);
        Assert.Equal(VisualOrigin.Uploaded, first.Origin);
        Assert.Null(first.Generation);
        Assert.Null(first.ValidatedAt);
        var selected = await Service().SelectAsync(post, first.Id);
        Assert.True(selected.Selected);
        Assert.NotNull(selected.ValidatedAt);
        var generator = new Generator();
        var second = await Service().GenerateAsync(post, new("brand", "Brand", "Simple", ["#123456"]), null, generator);
        Assert.Equal("Existing brief", generator.Brief);
        Assert.Equal(VisualOrigin.Generated, second.Origin);
        Assert.False(second.Selected);
        Assert.NotNull(second.Generation);
        var list = await Service().ListAsync(post);
        Assert.Equal(2, list.Count);
        Assert.Single(list, v => v.Selected);
        Assert.Equal(Image(), (await Service().ReadAsync(post, first.Id)).Bytes);
        await using var db = Db();
        var entity = await db.SocialPosts.SingleAsync();
        Assert.Equal("https://example.com/legacy.png", entity.VisualUrl);
        Assert.Equal("Existing brief", entity.VisualBrief);
        Assert.Equal(first.Id, entity.SelectedVisualId);
    }

    [Fact]
    public async Task OwnershipAndSelectedDeletionAreRejected()
    {
        var post = await Seed();
        var other = await Seed();
        var visual = await Upload(post);
        await Assert.ThrowsAsync<VisualNotFoundException>(() => Service().SelectAsync(other, visual.Id));
        await Assert.ThrowsAsync<VisualNotFoundException>(() => Service().ReadAsync(other, visual.Id));
        await Service().SelectAsync(post, visual.Id);
        await Assert.ThrowsAsync<DomainException>(() => Service().DeleteAsync(post, visual.Id));
    }

    [Fact]
    public async Task ConcurrentSelectionHasExactlyOneWinnerInCurrentState()
    {
        var post = await Seed();
        var a = await Upload(post);
        var b = await Upload(post);
        await Task.WhenAll(Task.Run(() => Service().SelectAsync(post, a.Id)), Task.Run(() => Service().SelectAsync(post, b.Id)));
        Assert.Single(await Service().ListAsync(post), v => v.Selected);
    }

    [Fact]
    public async Task SelectionAndDeletionRaceCannotSelectDeletedVisual()
    {
        var post = await Seed();
        var visual = await Upload(post);
        var outcomes = await Task.WhenAll(
            Task.Run(async () => await Record.ExceptionAsync(() => Service().SelectAsync(post, visual.Id))),
            Task.Run(async () => await Record.ExceptionAsync(() => Service().DeleteAsync(post, visual.Id))));
        Assert.Single(outcomes, e => e is null);
        Assert.IsType<DomainException>(outcomes.Single(e => e is not null));
        await using var db = Db();
        var state = await db.PostVisuals.SingleAsync();
        var entity = await db.SocialPosts.SingleAsync();
        Assert.False(state.IsDeleted && entity.SelectedVisualId == state.Id);
    }

    [Fact]
    public async Task StorageFailureLeavesRecoverableTombstone()
    {
        var post = await Seed();
        var broken = new FailingStorage(_storage) { FailWrite = true };
        await Assert.ThrowsAsync<VisualStorageException>(() => Upload(post, Service(broken)));
        Assert.Empty(await Service().ListAsync(post));
        await using var db = Db();
        Assert.True((await db.PostVisuals.SingleAsync()).IsDeleted);
        _clock.Now = _clock.Now.AddDays(3);
        Assert.Equal(1, await Store().CleanupAsync(_clock.Now.AddDays(-1), default));
    }

    [Fact]
    public async Task ConcurrentWorkflowChangeRejectsCompletionAndRetainsFileUntilCleanup()
    {
        var post = await Seed();
        var storage = new FailingStorage(_storage);
        storage.AfterWrite = async () =>
        {
            await using var db = Db();
            (await db.SocialPosts.SingleAsync()).SubmitForReview();
            await db.SaveChangesAsync();
        };
        await Assert.ThrowsAsync<DomainException>(() => Upload(post, Service(storage)));
        await using var verify = Db();
        var visual = await verify.PostVisuals.SingleAsync();
        Assert.True(visual.IsDeleted);
        Assert.NotEmpty(await _storage.ReadAsync(visual.StorageKey));
        _clock.Now = _clock.Now.AddDays(3);
        await Store().CleanupAsync(_clock.Now.AddDays(-1), default);
        await Assert.ThrowsAsync<VisualStorageException>(() => _storage.ReadAsync(visual.StorageKey));
    }

    [Fact]
    public async Task DeleteIsLogicalAndPurgeRespectsRetentionAndRetries()
    {
        var post = await Seed();
        var visual = await Upload(post);
        await Service().DeleteAsync(post, visual.Id);
        var key = visual.Id.ToString("N");
        Assert.NotEmpty(await _storage.ReadAsync(key));
        Assert.Empty(await Service().ListAsync(post));
        await Assert.ThrowsAsync<VisualNotFoundException>(() => Service().ReadAsync(post, visual.Id));
        Assert.Equal(0, await Store().CleanupAsync(_clock.Now.AddDays(-1), default));
        _clock.Now = _clock.Now.AddDays(3);
        var broken = new FailingStorage(_storage) { FailDelete = true };
        await Assert.ThrowsAsync<VisualStorageException>(() => Store(broken).CleanupAsync(_clock.Now.AddDays(-1), default));
        Assert.NotEmpty(await _storage.ReadAsync(key));
        Assert.Equal(1, await Store().CleanupAsync(_clock.Now.AddDays(-1), default));
        await Assert.ThrowsAsync<VisualStorageException>(() => _storage.ReadAsync(key));
    }

    [Fact]
    public async Task DurablePublicationRetainsEvenDeletedCandidates()
    {
        var postId = await Seed();
        var visual = await Upload(postId);
        await Service().DeleteAsync(postId, visual.Id);
        await using (var db = Db())
        {
            var post = await db.SocialPosts.SingleAsync();
            post.SubmitForReview(); post.Approve(); post.Schedule(_clock.Now.AddDays(1));
            _clock.Now = _clock.Now.AddDays(2);
            db.PublicationOperations.Add(PublicationOperation.Claim(post, Guid.NewGuid(), "LinkedIn", "member", _clock));
            await db.SaveChangesAsync();
        }
        _clock.Now = _clock.Now.AddDays(3);
        Assert.Equal(0, await Store().CleanupAsync(_clock.Now.AddDays(-1), default));
        Assert.NotEmpty(await _storage.ReadAsync(visual.Id.ToString("N")));
    }

    [Fact]
    public async Task PendingCrashCanBeCleanedAndNeverCompletedAfterRetirement()
    {
        var post = await Seed();
        var reservation = await Store().ReserveAsync(post, new(VisualOrigin.Uploaded, new("image/png", 16, 12), 10, new string('a',64), null), default);
        await _storage.WriteAsync(reservation.StorageKey, Image());
        _clock.Now = _clock.Now.AddDays(3);
        await Store().CleanupAsync(_clock.Now.AddDays(-1), default);
        await Assert.ThrowsAsync<DomainException>(() => Store().CompleteAsync(post, reservation.Id, default));
        Assert.Empty(await Service().ListAsync(post));
    }

    [Fact]
    public async Task ReadyCandidatesAndSoftDeletedPostsAreRetained()
    {
        var post = await Seed();
        var visual = await Upload(post);
        await using (var db = Db()) { (await db.SocialPosts.SingleAsync()).SoftDelete(); await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<PostNotFoundException>(() => Service().ReadAsync(post, visual.Id));
        _clock.Now = _clock.Now.AddDays(3);
        Assert.Equal(0, await Store().CleanupAsync(_clock.Now.AddDays(-1), default));
        Assert.NotEmpty(await _storage.ReadAsync(visual.Id.ToString("N")));
    }

    [Fact]
    public async Task CorruptionAndOversizeUploadAreRejectedWithoutCandidates()
    {
        var post = await Seed();
        await Assert.ThrowsAsync<InvalidVisualException>(() => Service().UploadAsync(post, new MemoryStream([1,2,3])));
        _limits.MaxBytes = 10;
        await Assert.ThrowsAsync<InvalidVisualException>(() => Upload(post));
        await using var db = Db();
        Assert.Empty(await db.PostVisuals.ToListAsync());
    }

    [Fact]
    public async Task ReadDetectsTampering()
    {
        var post = await Seed();
        var visual = await Upload(post);
        await File.WriteAllBytesAsync(Path.Combine(_root,"files",visual.Id.ToString("N")), [1,2,3]);
        await Assert.ThrowsAsync<VisualStorageException>(() => Service().ReadAsync(post, visual.Id));
    }

    [Fact]
    public async Task MigrationPreservesLegacyRowsAndMatchesModel()
    {
        var path = Path.Combine(_root, "legacy.db");
        await using var db = new SocialFlowDbContext(new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261002235255_AddDurablePublicationOperations");
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO SocialPosts
            (Id,Title,Content,Platform,Status,VisualBrief,VisualUrl,CreatedAt,UpdatedAt,IsDeleted)
            VALUES ({id},'Legacy','Content',2,1,'Brief','https://example.com/old.png',{_clock.Now},{_clock.Now},0)");
        await migrator.MigrateAsync();
        var legacy = await db.SocialPosts.SingleAsync();
        Assert.Equal(id, legacy.Id);
        Assert.Equal("Brief", legacy.VisualBrief);
        Assert.Equal("https://example.com/old.png", legacy.VisualUrl);
        Assert.Null(legacy.SelectedVisualId);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static byte[] Image()
    {
        using var bitmap = new SKBitmap(16,12);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png,90);
        return data.ToArray();
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = new(2030,1,1,0,0,0,TimeSpan.Zero); public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow()=>now; }
    private sealed class Generator : IVisualGenerator
    {
        public string? Brief;
        public Task<GeneratedVisual> GenerateAsync(GenerateVisualRequest request, CancellationToken cancellationToken = default)
        {
            Brief = request.VisualBrief;
            var bytes = Image();
            return Task.FromResult(new GeneratedVisual(bytes, new("Fake","test",request.Identity.Id,request.Platform,
                request.VisualBrief,"image/png",16,12,bytes.Length,"medium",DateTimeOffset.UtcNow)));
        }
    }
    private sealed class FailingStorage(IVisualStorage inner) : IVisualStorage
    {
        public bool FailWrite, FailDelete;
        public Func<Task>? AfterWrite;
        public async Task WriteAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken token=default)
        {
            if(FailWrite) throw new VisualStorageException();
            await inner.WriteAsync(key,bytes,token);
            if(AfterWrite is not null) await AfterWrite();
        }
        public Task<byte[]> ReadAsync(string key,CancellationToken token=default)=>inner.ReadAsync(key,token);
        public Task DeleteAsync(string key,CancellationToken token=default)=>FailDelete ? throw new VisualStorageException() : inner.DeleteAsync(key,token);
    }
}
