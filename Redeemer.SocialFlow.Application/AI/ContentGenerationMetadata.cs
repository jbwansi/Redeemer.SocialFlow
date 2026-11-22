namespace Redeemer.SocialFlow.Application.AI;

public sealed record ContentGenerationMetadata(string Provider, string Model)
{
    public EditorialAngle? EditorialAngle { get; init; }
    public Terminology.TerminologyEvidence? Terminology { get; init; }
}
