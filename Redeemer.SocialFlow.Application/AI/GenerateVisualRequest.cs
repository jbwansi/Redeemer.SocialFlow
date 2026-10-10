using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.AI;

public enum VisualLayout { Square, Landscape, Portrait }

/// <summary>VisualBrief comes from the existing draft/post or its edited value. No post mutation occurs.</summary>
public sealed class GenerateVisualRequest
{
    public const int MaximumBriefLength = 4000;
    public string VisualBrief { get; }
    public SocialPlatform Platform { get; }
    public VisualIdentity Identity { get; }
    public VisualLayout Layout { get; }

    public GenerateVisualRequest(string visualBrief, SocialPlatform platform, VisualIdentity identity,
        VisualLayout? layout = null)
    {
        VisualBrief = VisualIdentity.Required(visualBrief, MaximumBriefLength, nameof(visualBrief));
        if (!Enum.IsDefined(platform)) throw new ArgumentException("Invalid platform.", nameof(platform));
        ArgumentNullException.ThrowIfNull(identity);
        if (layout.HasValue && !Enum.IsDefined(layout.Value)) throw new ArgumentException("Invalid layout.", nameof(layout));
        Platform = platform;
        Identity = identity;
        Layout = layout ?? (platform == SocialPlatform.Facebook ? VisualLayout.Landscape : VisualLayout.Square);
    }
}
