using System.Buffers.Binary;
using Redeemer.SocialFlow.Application.Visuals;
using SkiaSharp;

namespace Redeemer.SocialFlow.Infrastructure.Visuals;

public sealed class SkiaVisualValidator(VisualLimits limits) : IVisualValidator, IDisposable
{
    private readonly SemaphoreSlim _decoders = new(2);
    public async Task<ValidatedImage> ValidateAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
    {
        if (bytes.Length == 0 || bytes.Length > limits.MaxBytes) throw Invalid();
        await _decoders.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            using var data = SKData.CreateCopy(bytes.Span);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg))
                throw Invalid();
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || info.Width > limits.MaxDimension ||
                info.Height > limits.MaxDimension || (long)info.Width * info.Height > limits.MaxPixels ||
                codec.FrameCount > 1) throw Invalid();
            if (codec.EncodedFormat == SKEncodedImageFormat.Png) CheckPng(bytes.Span);
            else if (bytes.Length < 4 || bytes.Span[0] != 0xff || bytes.Span[1] != 0xd8 ||
                bytes.Span[^2] != 0xff || bytes.Span[^1] != 0xd9) throw Invalid();
            using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (bitmap.GetPixels() == IntPtr.Zero || codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                throw Invalid();
            token.ThrowIfCancellationRequested();
            return new(codec.EncodedFormat == SKEncodedImageFormat.Png ? "image/png" : "image/jpeg", info.Width, info.Height);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (InvalidVisualException) { throw; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException) { throw Invalid(); }
        finally { _decoders.Release(); }
    }
    private static InvalidVisualException Invalid() => new("Unsupported, corrupt or oversized image.");
    private static void CheckPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })) throw Invalid();
        int offset = 8;
        bool header = false, imageData = false;
        while (offset <= bytes.Length - 12)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (length > bytes.Length - offset - 12) throw Invalid();
            int size = (int)length;
            var type = bytes.Slice(offset + 4, 4);
            if (!header)
            {
                if (!type.SequenceEqual("IHDR"u8) || size != 13) throw Invalid();
                header = true;
            }
            else if (type.SequenceEqual("IHDR"u8)) throw Invalid();
            if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8)) throw Invalid();
            uint crc = uint.MaxValue;
            foreach (byte b in bytes.Slice(offset + 4, size + 4))
            {
                crc ^= b;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0);
            }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 8 + size, 4))) throw Invalid();
            if (type.SequenceEqual("IDAT"u8)) imageData = true;
            offset += size + 12;
            if (type.SequenceEqual("IEND"u8))
            {
                if (!imageData || size != 0 || offset != bytes.Length) throw Invalid();
                return;
            }
        }
        throw Invalid();
    }
    public void Dispose() => _decoders.Dispose();
}
