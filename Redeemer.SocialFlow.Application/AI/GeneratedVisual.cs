using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.AI;

/// <summary>In-memory output only. Header dimensions are not a full image-decoding validation.</summary>
public sealed record GeneratedVisual(ReadOnlyMemory<byte> Bytes, VisualGenerationMetadata Metadata);

public sealed record VisualGenerationMetadata(
    string Provider, string Model, string IdentityId, SocialPlatform Platform, string VisualBrief,
    string ContentType, int Width, int Height, int ByteLength, string Quality,
    DateTimeOffset GeneratedAt, EffectiveVisualIdentity? EffectiveIdentity = null);

public sealed record EffectiveVisualIdentity(string IdentityId, string Name, string Style, string[] Colors,
    string? Slogan, string? Instructions, Guid? LibraryId, long? Revision, Guid? LogoId);
