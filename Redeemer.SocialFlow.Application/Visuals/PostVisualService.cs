using System.Security.Cryptography;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Application.Visuals;

public sealed class PostVisualService(IPostVisualStore store, IVisualStorage storage,
    IVisualValidator validator, VisualLimits limits)
{
    public async Task<VisualDto> UploadAsync(Guid postId, Stream input, CancellationToken token = default)
    {
        await store.EditablePostAsync(postId, token);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (memory.Length + count > limits.MaxBytes) throw new InvalidVisualException("Image is too large.");
            await memory.WriteAsync(buffer.AsMemory(0, count), token);
        }
        return await SaveAsync(postId, memory.ToArray(), VisualOrigin.Uploaded, null, token);
    }

    public async Task<VisualDto> GenerateAsync(Guid postId, VisualIdentity identity, string? visualBrief,
        IVisualGenerator generator, CancellationToken token = default)
    {
        var post = await store.EditablePostAsync(postId, token);
        var request = new GenerateVisualRequest(visualBrief ?? post.VisualBrief!, post.Platform, identity);
        var result = await generator.GenerateAsync(request, token);
        return await SaveAsync(postId, result.Bytes, VisualOrigin.Generated, result.Metadata with { EffectiveIdentity = identity.Snapshot() }, token);
    }

    private async Task<VisualDto> SaveAsync(Guid postId, ReadOnlyMemory<byte> bytes, VisualOrigin origin,
        VisualGenerationMetadata? generation, CancellationToken token)
    {
        if (bytes.Length == 0 || bytes.Length > limits.MaxBytes) throw new InvalidVisualException("Invalid image length.");
        var image = await validator.ValidateAsync(bytes, token);
        var hash = Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();
        // A durable reservation precedes the file write. Never delete a file on an ambiguous DB commit.
        var reservation = await store.ReserveAsync(postId, new(origin, image, bytes.Length, hash, generation), token);
        try
        {
            await storage.WriteAsync(reservation.StorageKey, bytes, token);
            return await store.CompleteAsync(postId, reservation.Id, token);
        }
        catch
        {
            try { await store.AbandonAsync(postId, reservation.Id, CancellationToken.None); }
            catch { /* The pending reservation remains recoverable by delayed maintenance. */ }
            throw;
        }
    }

    public Task<IReadOnlyList<VisualDto>> ListAsync(Guid postId, CancellationToken token = default) => store.ListAsync(postId, token);
    public Task<VisualDto> SelectAsync(Guid postId, Guid visualId, CancellationToken token = default) => store.SelectAsync(postId, visualId, token);
    public Task DeleteAsync(Guid postId, Guid visualId, CancellationToken token = default) => store.DeleteAsync(postId, visualId, token);
    public async Task<VisualContent> ReadAsync(Guid postId, Guid visualId, CancellationToken token = default)
    {
        var (visual, key) = await store.GetAsync(postId, visualId, token);
        var bytes = await storage.ReadAsync(key, token);
        if (bytes.LongLength != visual.ByteLength ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(visual.Sha256)))
            throw new VisualStorageException();
        return new(bytes, visual.ContentType);
    }
}
