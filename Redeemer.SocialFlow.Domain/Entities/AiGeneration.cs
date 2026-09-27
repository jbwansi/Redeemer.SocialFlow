using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public sealed class AiGeneration
{
    private AiGeneration() { }

    public Guid Id { get; private set; }
    public Guid SocialPostId { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Objective { get; private set; } = string.Empty;
    public string Audience { get; private set; } = string.Empty;
    public SocialPlatform Platform { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; private set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; private set; }

    public static AiGeneration Create(Guid socialPostId, string subject, string objective,
        string audience, SocialPlatform platform, string provider, string model,
        IReadOnlyList<string> warnings, TimeProvider? timeProvider = null)
    {
        if (socialPostId == Guid.Empty) throw new DomainException("A social post is required.");
        if (!Enum.IsDefined(platform)) throw new DomainException("Invalid platform.");
        if (new[] { subject, objective, audience, provider, model }.Any(string.IsNullOrWhiteSpace))
            throw new DomainException("The generation brief, provider and model are required.");
        ArgumentNullException.ThrowIfNull(warnings);
        return new AiGeneration
        {
            Id = Guid.NewGuid(), SocialPostId = socialPostId,
            Subject = subject, Objective = objective, Audience = audience, Platform = platform,
            Provider = provider, Model = model,
            Warnings = Array.AsReadOnly(warnings.ToArray()),
            GeneratedAt = (timeProvider ?? TimeProvider.System).GetUtcNow()
        };
    }
}
