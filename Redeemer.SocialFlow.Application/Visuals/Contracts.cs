using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.Visuals;

public interface IVisualStorage
{
    Task WriteAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken token = default);
    Task<byte[]> ReadAsync(string key, CancellationToken token = default);
    Task DeleteAsync(string key, CancellationToken token = default);
}
public sealed record ValidatedImage(string ContentType, int Width, int Height);
public interface IVisualValidator
{
    Task<ValidatedImage> ValidateAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default);
}
public sealed class VisualLimits
{
    public int MaxBytes { get; set; } = 20 * 1024 * 1024;
    public int MaxDimension { get; set; } = 8192;
    public long MaxPixels { get; set; } = 36_000_000;
}
public sealed record VisualPostContext(Guid Id, SocialPlatform Platform, string? VisualBrief);
public sealed record VisualDto(Guid Id, Guid SocialPostId, VisualOrigin Origin, string ContentType,
    int Width, int Height, long ByteLength, string Sha256, DateTimeOffset CreatedAt,
    DateTimeOffset? ValidatedAt, bool Selected, VisualGenerationMetadata? Generation);
public sealed record VisualContent(byte[] Bytes, string ContentType);
public sealed record NewVisual(VisualOrigin Origin, ValidatedImage Image, long ByteLength, string Sha256,
    VisualGenerationMetadata? Generation);
public sealed record VisualReservation(Guid Id, string StorageKey);

/// <summary>Atomic post/visual mutations; every mutation rechecks current post eligibility.</summary>
public interface IPostVisualStore
{
    Task<VisualPostContext> EditablePostAsync(Guid postId, CancellationToken token);
    Task<VisualReservation> ReserveAsync(Guid postId, NewVisual visual, CancellationToken token);
    Task<VisualDto> CompleteAsync(Guid postId, Guid visualId, CancellationToken token);
    Task AbandonAsync(Guid postId, Guid visualId, CancellationToken token);
    Task<IReadOnlyList<VisualDto>> ListAsync(Guid postId, CancellationToken token);
    Task<(VisualDto Visual, string Key)> GetAsync(Guid postId, Guid visualId, CancellationToken token);
    Task<VisualDto> SelectAsync(Guid postId, Guid visualId, CancellationToken token);
    Task DeleteAsync(Guid postId, Guid visualId, CancellationToken token);
    // Maintenance is explicit, not a hosted worker. Storage errors leave tombstones for retry.
    Task<int> CleanupAsync(DateTimeOffset cutoff, CancellationToken token);
}
public sealed class VisualNotFoundException() : Exception("Visual not found.");
public sealed class VisualStorageException() : Exception("Visual storage operation failed.");
public sealed class InvalidVisualException(string message) : Exception(message);
