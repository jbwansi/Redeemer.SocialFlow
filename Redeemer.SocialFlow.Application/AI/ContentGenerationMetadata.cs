namespace Redeemer.SocialFlow.Application.AI;

public sealed record ContentGenerationMetadata(string Provider, string Model)
{
    public Terminology.TerminologyEvidence? Terminology { get; init; }
}
