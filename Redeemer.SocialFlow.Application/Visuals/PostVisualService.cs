using System.Security.Cryptography;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Application.Terminology;

namespace Redeemer.SocialFlow.Application.Visuals;

public sealed class PostVisualService(IPostVisualStore store, IVisualStorage storage,
    IVisualValidator validator, VisualLimits limits, ITerminologyReader? terminology = null)
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

    public async Task<VisualConcept> PrepareAsync(Guid postId, VisualIdentity identity, string? visualBrief,
        CancellationToken token = default) =>
        VisualConcept.Create(await store.EditablePostAsync(postId, token), identity, visualBrief,
            terminology is null ? null : await terminology.ReadAsync(token));

    public async Task<VisualDto> GenerateAsync(Guid postId, VisualIdentity identity, string? visualBrief,
        IVisualGenerator generator, CancellationToken token = default, string? expectedFingerprint = null)
    {
        if (string.IsNullOrWhiteSpace(expectedFingerprint)) throw new VisualConceptException("VisualConceptRequired");
        var post = await store.EditablePostAsync(postId, token);
        var concept = VisualConcept.Create(post, identity, visualBrief, terminology is null ? null : await terminology.ReadAsync(token));
        if (concept.Fingerprint != expectedFingerprint)
            throw new VisualConceptException("VisualConceptStale");
        var request = new GenerateVisualRequest(concept.Brief, post.Platform, identity, title: concept.Subject, content: concept.Message,
            terms: concept.Terminology?.Terms);
        var result = await generator.GenerateAsync(request, token);
        return await SaveAsync(postId, result.Bytes, VisualOrigin.Generated, result.Metadata with
            { EffectiveIdentity = identity.Snapshot(), Terminology = concept.Terminology }, token);
    }

    public async Task<VisualTextReview> ReviewTextAsync(Guid postId, VisualTextOptions text, CancellationToken token) =>
        VisualTextValidation.Review(await store.EditablePostAsync(postId, token), text,
            terminology is null ? null : await terminology.ReadAsync(token));

    public async Task<VisualDto> ComposeAsync(Guid postId, Guid visualId, VisualIdentity identity,
        BrandCompositionOptions options, IVisualBrandComposer composer, CancellationToken token)
    {
        var post = await store.EditablePostAsync(postId, token);
        VisualTextReview? review = null;
        if (options.Text is not null)
        {
            review = VisualTextValidation.Review(post, options.Text, terminology is null ? null : await terminology.ReadAsync(token));
            if (!options.TextConfirmed || review.Fingerprint != options.TextFingerprint)
                throw new BrandCompositionException("TextReviewRequired");
            if (review.CanonicalCorrectionsRequired) throw new BrandCompositionException("TextCanonicalRequired");
        }
        var (source, _) = await store.GetAsync(postId, visualId, token);
        if (source.Generation?.Composition is not null) throw new BrandCompositionException("AlreadyComposed");
        var content = await ReadAsync(postId, visualId, token);
        var result = await composer.ComposeAsync(content.Bytes, identity, options, token);
        var metadata = new VisualGenerationMetadata("DeterministicComposition", options.Text is null ? "skia-banner-v2" : "skia-text-v1", identity.Id, post.Platform,
            post.VisualBrief ?? "", "image/png", source.Width, source.Height, result.Bytes.Length, "Lossless", DateTimeOffset.UtcNow,
            identity.Snapshot(), review?.Terminology ?? source.Generation?.Terminology,
            new(source.Id, source.Sha256, result.LogoSha256, result.EffectiveMode, options.LogoContainsSlogan,
                options.Text is null ? "skia-banner-v2" : "skia-text-v1", result.FontFamily, options.Text, review?.Fingerprint,
                Convert.ToHexString(SHA256.HashData(result.Bytes)).ToLowerInvariant(), options.Layout));
        var dimensions = await validator.ValidateAsync(result.Bytes, token);
        return await SaveAsync(postId, result.Bytes, VisualOrigin.Generated, metadata with { Width = dimensions.Width, Height = dimensions.Height }, token);
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
