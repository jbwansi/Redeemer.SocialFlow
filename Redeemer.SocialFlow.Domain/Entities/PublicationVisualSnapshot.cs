namespace Redeemer.SocialFlow.Domain.Entities;

/// <summary>Immutable metadata captured in the same transaction as the publication claim.</summary>
public sealed record PublicationVisualSnapshot(Guid VisualId, string StorageKey, string ContentType,
    long ByteLength, string Sha256, int Width, int Height, DateTimeOffset ValidatedAt)
{
    public static PublicationVisualSnapshot From(PostVisual visual) => new(visual.Id, visual.StorageKey,
        visual.ContentType, visual.ByteLength, visual.Sha256, visual.Width, visual.Height, visual.ValidatedAt!.Value);
}
