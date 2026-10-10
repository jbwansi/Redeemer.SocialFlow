using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Visuals;
using SkiaSharp;
using Xunit;
namespace Redeemer.SocialFlow.IntegrationTests.Visuals;
public sealed class VisualIdentityLibraryTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "identity-tests-" + Guid.NewGuid().ToString("N"));
    private DbContextOptions<SocialFlowDbContext> options = null!;
    private IVisualStorage storage = null!;
    private readonly VisualLimits limits = new();
    private SocialFlowDbContext Db() => new(options);
    private VisualIdentityLibrary Library(IVisualStorage? files = null) => new(new SqliteVisualIdentityRepository(options), files ?? storage, new SkiaVisualValidator(limits), limits);
    private static IdentityInput Input(string code = "ACME", bool active = true) => new(code, "Acme", "Flat", ["#123456"], "Hello", "Clean shapes", active);
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite($"Data Source={Path.Combine(root, "test.db")};Pooling=False;Default Timeout=10").Options;
        storage = new LocalVisualStorage(new() { RootPath = Path.Combine(root, "files") }, limits);
        await using var db = Db(); await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() { Directory.Delete(root, true); return Task.CompletedTask; }
    [Fact] public async Task SeedsRhAndPersistsCrudDefaultAndUniqueIdentifier()
    {
        var rh = Assert.Single(await Library().ListAsync());
        Assert.Equal("RH", rh.IdentityId); Assert.Equal("Transformer par les valeurs", rh.Slogan);
        Assert.Equal(new[] { "#DA2E29", "#D4AF37", "#000000", "#FFFFFF" }, rh.Colors); Assert.Null(rh.LogoId);
        var added = await Library().CreateAsync(Input());
        await Assert.ThrowsAsync<DomainException>(() => Library().CreateAsync(Input("acme")));
        added = await Library().DefaultAsync(added.Id, added.Revision);
        Assert.Single(await Library().ListAsync(), x => x.IsDefault);
        added = await Library().UpdateAsync(added.Id, added.Revision, Input(active: false));
        Assert.False(added.IsDefault);
        await Assert.ThrowsAsync<DomainException>(() => Library().DefaultAsync(added.Id, added.Revision));
        await Library().DeleteAsync(added.Id, added.Revision);
        Assert.Single(await Library().ListAsync());
    }
    [Fact] public async Task ConcurrentEditorsCannotOverwriteEachOther()
    {
        var identity = await Library().CreateAsync(Input());
        async Task<bool> Change(string name)
        {
            try { await Library().UpdateAsync(identity.Id, identity.Revision, Input() with { Name = name }); return true; }
            catch (DomainException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Change("First")), Task.Run(() => Change("Second")));
        Assert.Single(results, x => x);
        Assert.Equal(2, (await Library().ListAsync()).Single(x => x.Id == identity.Id).Revision);
    }
    [Fact] public async Task LogoIsDecodedPrivateImmutableAndHistoricalVersionsRemainReadable()
    {
        var identity = await Library().CreateAsync(Input()); var first = Image();
        identity = await Library().UploadLogoAsync(identity.Id, identity.Revision, new MemoryStream(first));
        var oldLogo = identity.LogoId!.Value;
        Assert.Equal(first, (await Library().ReadLogoAsync(identity.Id, oldLogo)).Bytes);
        identity = await Library().UploadLogoAsync(identity.Id, identity.Revision, new MemoryStream(Image(SKColors.Red)));
        Assert.NotEqual(oldLogo, identity.LogoId);
        Assert.Equal(first, (await Library().ReadLogoAsync(identity.Id, oldLogo)).Bytes);
        await Assert.ThrowsAsync<IdentityNotFoundException>(() => Library().ReadLogoAsync(Guid.NewGuid(), oldLogo));
        await Assert.ThrowsAsync<InvalidVisualException>(() => Library().UploadLogoAsync(identity.Id, identity.Revision, new MemoryStream([1, 2, 3])));
        await using var db = Db(); Assert.Equal(2, await db.IdentityLogos.CountAsync());
        Assert.All(await db.IdentityLogos.ToListAsync(), l => Assert.Equal(l.Id.ToString("N"), l.StorageKey));
    }
    [Fact] public async Task StorageFailureLeavesTrackedInaccessibleReservationAndOriginalLogoUnchanged()
    {
        var identity = await Library().CreateAsync(Input());
        identity = await Library().UploadLogoAsync(identity.Id, identity.Revision, new MemoryStream(Image()));
        await Assert.ThrowsAsync<VisualStorageException>(() => Library(new BrokenStorage()).UploadLogoAsync(identity.Id, identity.Revision, new MemoryStream(Image())));
        var loaded = (await Library().ListAsync()).Single(x => x.Id == identity.Id);
        Assert.Equal(identity.LogoId, loaded.LogoId);
        await using var db = Db(); var pending = await db.IdentityLogos.SingleAsync(x => !x.IsReady);
        await Assert.ThrowsAsync<IdentityNotFoundException>(() => Library().ReadLogoAsync(identity.Id, pending.Id));
    }
    [Fact] public async Task GeneratedVisualKeepsEffectiveSettingsAndLogoAfterIdentityChanges()
    {
        var identity = await Library().CreateAsync(Input());
        identity = await Library().UploadLogoAsync(identity.Id, identity.Revision, new MemoryStream(Image()));
        var effective = await Library().ResolveAsync(identity.Id, identity.Revision, new("CUSTOM", "Temporary name", "Override style", ["#FEDCBA"], "Temporary slogan", "Temporary instruction"));
        await using var db = Db();
        var post = SocialPost.Create("Title", "Content", SocialPlatform.LinkedIn); db.SocialPosts.Add(post); await db.SaveChangesAsync();
        var service = new PostVisualService(new SqlitePostVisualStore(options, storage), storage, new SkiaVisualValidator(limits), limits);
        var visual = await service.GenerateAsync(post.Id, effective, "Garden", new Generator());
        var snapshot = visual.Generation!.EffectiveIdentity!;
        Assert.Equal(identity.Id, snapshot.LibraryId); Assert.Equal(identity.LogoId, snapshot.LogoId);
        Assert.Equal("Temporary name", snapshot.Name); Assert.Equal("Temporary instruction", snapshot.Instructions);
        Assert.Equal("#FEDCBA", snapshot.Colors[0]); Assert.Equal("Acme", (await Library().ListAsync()).Single(x => x.Id == identity.Id).Name);
        identity = await Library().UpdateAsync(identity.Id, identity.Revision, Input() with { Name = "Changed" });
        Assert.Equal("Temporary name", (await service.ListAsync(post.Id)).Single().Generation!.EffectiveIdentity!.Name);
        await Assert.ThrowsAsync<DomainException>(() => Library().DeleteAsync(identity.Id, identity.Revision));
        identity = await Library().UpdateAsync(identity.Id, identity.Revision, Input(active: false));
        await Assert.ThrowsAsync<DomainException>(() => Library().ResolveAsync(identity.Id, identity.Revision, effective));
        Assert.Equal(Image(), (await Library().ReadLogoAsync(identity.Id, snapshot.LogoId!.Value)).Bytes);
    }
    [Fact] public async Task DeletedSourceCannotBeAttachedByGenerationFinishingLate()
    {
        var identity = await Library().CreateAsync(Input());
        var effective = await Library().ResolveAsync(identity.Id, identity.Revision, new("ACME", "Acme", "Flat", ["#123456"]));
        await Library().DeleteAsync(identity.Id, identity.Revision);
        await using var db = Db(); var post = SocialPost.Create("Title", "Content", SocialPlatform.LinkedIn); db.Add(post); await db.SaveChangesAsync();
        var service = new PostVisualService(new SqlitePostVisualStore(options, storage), storage, new SkiaVisualValidator(limits), limits);
        await Assert.ThrowsAsync<DomainException>(() => service.GenerateAsync(post.Id, effective, "Garden", new Generator()));
        Assert.Empty(await db.PostVisuals.ToListAsync());
    }
    [Fact] public async Task MigrationPreservesExistingSelectedVisualAndLegacyMetadata()
    {
        var legacyOptions = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite($"Data Source={Path.Combine(root, "legacy.db")};Pooling=False").Options;
        await using var db = new SocialFlowDbContext(legacyOptions);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261010021112_AddPublicationVisualSnapshot");
        var post = Guid.NewGuid(); var visual = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var key = visual.ToString("N"); var hash = new string('a', 64);
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO SocialPosts
            (Id,Title,Content,Platform,Status,VisualBrief,VisualUrl,CreatedAt,UpdatedAt,IsDeleted)
            VALUES ({post},'Existing','Content',2,1,'Brief','https://example.com/legacy.png',{now},{now},0)");
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO PostVisuals
            (Id,SocialPostId,Origin,StorageKey,ContentType,Width,Height,ByteLength,Sha256,CreatedAt,ValidatedAt,IsReady,IsDeleted)
            VALUES ({visual},{post},2,{key},'image/png',16,12,100,{hash},{now},{now},1,0)");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SocialPosts SET SelectedVisualId={visual} WHERE Id={post}");
        await migrator.MigrateAsync();
        var oldPost = await db.SocialPosts.SingleAsync(); var oldVisual = await db.PostVisuals.SingleAsync();
        Assert.Equal(visual, oldPost.SelectedVisualId); Assert.Equal("https://example.com/legacy.png", oldPost.VisualUrl);
        Assert.Null(oldVisual.SourceIdentityId); Assert.Null(oldVisual.GenerationMetadataJson); Assert.Equal(key, oldVisual.StorageKey);
        Assert.True(oldVisual.IsReady); Assert.False(db.Database.HasPendingModelChanges());
        Assert.Single(await db.VisualIdentities.ToListAsync());
    }
    [Fact] public async Task ConcurrentLogoUploadsCannotOverwriteOrDeleteTheWinningOriginal()
    {
        var identity = await Library().CreateAsync(Input());
        async Task<bool> Upload(SKColor color)
        {
            try { await Library().UploadLogoAsync(identity.Id, identity.Revision, new MemoryStream(Image(color))); return true; }
            catch (DomainException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Upload(SKColors.Red)), Task.Run(() => Upload(SKColors.Blue)));
        Assert.Single(results, x => x);
        var current = (await Library().ListAsync()).Single(x => x.Id == identity.Id);
        Assert.NotEmpty((await Library().ReadLogoAsync(identity.Id, current.LogoId!.Value)).Bytes);
    }
    [Fact] public async Task StaleRevisionAndTamperedLogoAreRejected()
    {
        var identity = await Library().CreateAsync(Input());
        identity = await Library().UploadLogoAsync(identity.Id, identity.Revision, new MemoryStream(Image()));
        await Assert.ThrowsAsync<DomainException>(() => Library().ResolveAsync(identity.Id, 1, new("ACME", "Acme", "Flat", ["#000000"])));
        await File.WriteAllBytesAsync(Path.Combine(root, "files", identity.LogoId!.Value.ToString("N")), [1,2,3]);
        await Assert.ThrowsAsync<VisualStorageException>(() => Library().ReadLogoAsync(identity.Id, identity.LogoId.Value));
    }
    private static byte[] Image(SKColor? color = null)
    { using var bitmap = new SKBitmap(16,12); bitmap.Erase(color ?? SKColors.Blue); using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png,90); return data.ToArray(); }
    private sealed class Generator : IVisualGenerator
    {
        public Task<GeneratedVisual> GenerateAsync(GenerateVisualRequest request, CancellationToken cancellationToken = default)
        { var bytes = Image(); return Task.FromResult(new GeneratedVisual(bytes, new("Fake", "test", request.Identity.Id, request.Platform, request.VisualBrief, "image/png",16,12,bytes.Length,"medium",DateTimeOffset.UtcNow))); }
    }
    private sealed class BrokenStorage : IVisualStorage
    {
        public Task WriteAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken token = default) => throw new VisualStorageException();
        public Task<byte[]> ReadAsync(string key, CancellationToken token = default) => throw new VisualStorageException();
        public Task DeleteAsync(string key, CancellationToken token = default) => throw new VisualStorageException();
    }
}
