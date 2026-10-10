using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public enum VisualOrigin { Generated = 1, Uploaded = 2 }

public sealed class PostVisual
{
    private PostVisual() { }
    public Guid Id { get; private set; }
    public Guid SocialPostId { get; private set; }
    public VisualOrigin Origin { get; private set; }
    public string StorageKey { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public int Width { get; private set; }
    public int Height { get; private set; }
    public long ByteLength { get; private set; }
    public string Sha256 { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ValidatedAt { get; private set; }
    public bool IsReady { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? PurgedAt { get; private set; }
    public Guid? SourceIdentityId { get; private set; }
    public void SetSourceIdentity(Guid id) { if (Origin != VisualOrigin.Generated || IsReady || SourceIdentityId.HasValue) throw new DomainException("Invalid source identity."); SourceIdentityId = id; }
    public string? GenerationMetadataJson { get; private set; }

    public static PostVisual Create(SocialPost post, VisualOrigin origin, string contentType,
        int width, int height, long byteLength, string sha256, string? generationMetadataJson,
        DateTimeOffset now)
    {
        post.EnsureCanManageVisuals();
        if (!Enum.IsDefined(origin) || contentType is not ("image/png" or "image/jpeg") ||
            width <= 0 || height <= 0 || byteLength <= 0 || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new DomainException("Invalid visual metadata.");
        if (origin == VisualOrigin.Uploaded && generationMetadataJson is not null)
            throw new DomainException("Uploaded images cannot have generation metadata.");
        var id = Guid.NewGuid();
        return new() { Id = id, SocialPostId = post.Id, Origin = origin, StorageKey = id.ToString("N"),
            ContentType = contentType, Width = width, Height = height, ByteLength = byteLength,
            Sha256 = sha256.ToLowerInvariant(), CreatedAt = now, GenerationMetadataJson = generationMetadataJson };
    }

    public void Complete()
    {
        if (IsDeleted || IsReady) throw new DomainException("Visual is no longer pending.");
        IsReady = true;
    }
    public void Validate(DateTimeOffset now)
    {
        if (IsDeleted || !IsReady) throw new DomainException("Visual is unavailable.");
        ValidatedAt ??= now;
    }
    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted) return;
        IsDeleted = true;
        DeletedAt = now;
    }
    public void MarkPurged(DateTimeOffset now)
    {
        if (!IsDeleted) throw new DomainException("Only deleted visuals can be purged.");
        PurgedAt = now;
    }
}
