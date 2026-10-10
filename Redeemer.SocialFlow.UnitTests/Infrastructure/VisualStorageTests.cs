using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Infrastructure.Visuals;
using SkiaSharp;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Infrastructure;

public sealed class VisualStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "visual-storage-" + Guid.NewGuid().ToString("N"));
    private static readonly VisualLimits Limits = new();
    private LocalVisualStorage Store() => new(new() { RootPath = _root }, Limits);

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "image/png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    public async Task DecodeAndStorePreservesOriginalBytes(SKEncodedImageFormat format, string mime)
    {
        var image = Image(format);
        var original = image.ToArray();
        using var validator = new SkiaVisualValidator(Limits);
        var metadata = await validator.ValidateAsync(image);
        Assert.Equal(mime, metadata.ContentType);
        Assert.Equal((16, 12), (metadata.Width, metadata.Height));
        var key = Guid.NewGuid().ToString("N");
        await Store().WriteAsync(key, image);
        Assert.Equal(original, image);
        Assert.Equal(original, await Store().ReadAsync(key));
        await Assert.ThrowsAsync<VisualStorageException>(() => Store().WriteAsync(key, image));
        Assert.Equal(original, await Store().ReadAsync(key));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("C:\\escape")]
    [InlineData("/tmp/escape")]
    [InlineData("abc")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/")]
    public async Task TraversalAndNonOpaqueKeysAreRejected(string key)
    {
        var storage = Store();
        await Assert.ThrowsAsync<VisualStorageException>(() => storage.WriteAsync(key, Image()));
        await Assert.ThrowsAsync<VisualStorageException>(() => storage.ReadAsync(key));
        await Assert.ThrowsAsync<VisualStorageException>(() => storage.DeleteAsync(key));
    }

    [Fact]
    public void ApplicationOrPublicRootIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new LocalVisualStorage(
            new() { RootPath = Path.Combine(_root, "public", "files"), ForbiddenRoots = [Path.Combine(_root, "public")] }, Limits));
        Assert.Throws<ArgumentException>(() => new LocalVisualStorage(new() { RootPath = "relative" }, Limits));
    }

    [Fact]
    public async Task MissingFileAndCancellationAreSafe()
    {
        var storage = Store();
        var key = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<VisualStorageException>(() => storage.ReadAsync(key));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.WriteAsync(key, Image(), cancelled.Token));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("crc")]
    [InlineData("fake")]
    [InlineData("trailing")]
    [InlineData("gif")]
    [InlineData("jpeg-truncated")]
    public async Task CorruptOrUnsupportedImagesAreRejected(string scenario)
    {
        var image = Image();
        image = scenario switch
        {
            "truncated" => image[..^15],
            "crc" => Corrupt(image),
            "fake" => System.Text.Encoding.UTF8.GetBytes("<svg/>"),
            "trailing" => image.Concat(new byte[] { 0 }).ToArray(),
            "gif" => Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"),
            _ => Image(SKEncodedImageFormat.Jpeg)[..^2]
        };
        using var validator = new SkiaVisualValidator(Limits);
        await Assert.ThrowsAsync<InvalidVisualException>(() => validator.ValidateAsync(image));
    }
    private static byte[] Corrupt(byte[] image) { image[29] ^= 1; return image; }

    [Theory]
    [InlineData(10, 8192, 36000000)]
    [InlineData(20971520, 10, 36000000)]
    [InlineData(20971520, 8192, 100)]
    public async Task EncodedSizeDimensionsAndPixelsAreBounded(int bytes, int dimension, long pixels)
    {
        using var validator = new SkiaVisualValidator(new() { MaxBytes = bytes, MaxDimension = dimension, MaxPixels = pixels });
        await Assert.ThrowsAsync<InvalidVisualException>(() => validator.ValidateAsync(Image()));
    }

    [Fact]
    public async Task ValidatorHonorsPreCancellation()
    {
        using var validator = new SkiaVisualValidator(Limits);
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validator.ValidateAsync(Image(), source.Token));
    }

    private static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(16, 12);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
