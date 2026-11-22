using Redeemer.SocialFlow.Application.AI;
namespace Redeemer.SocialFlow.Application.Visuals;
public sealed record VisualTextOptions(string Display = "Illustration", string? Title = null, string? Subtitle = null,
    string[]? Messages = null, string Layout = "Landscape");
public sealed record VisualTextReview(VisualTextOptions Text, Terminology.TerminologyEvidence? Terminology, string Fingerprint,
    bool CanonicalCorrectionsRequired = false);
public sealed record BrandCompositionOptions(string Mode, bool? LogoContainsSlogan = null,
    VisualTextOptions? Text = null, string? TextFingerprint = null, bool TextConfirmed = false, string Layout = "Classic");
public sealed record BrandCompositionEvidence(Guid SourceVisualId, string SourceSha256, string? LogoSha256,
    string Mode, bool? LogoContainsSlogan, string Renderer, string? FontFamily,
    VisualTextOptions? Text = null, string? TextFingerprint = null, string? ComposedSha256 = null, string Layout = "Classic");
public sealed record ComposedImage(byte[] Bytes, string? LogoSha256, string EffectiveMode, string? FontFamily);
public interface IVisualBrandComposer
{
    Task<ComposedImage> ComposeAsync(byte[] source, VisualIdentity identity, BrandCompositionOptions options, CancellationToken token);
}
public sealed class BrandCompositionException(string code) : Exception(code) { public string Code { get; } = code; }
