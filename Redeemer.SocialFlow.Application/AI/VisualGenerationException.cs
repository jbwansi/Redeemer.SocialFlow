namespace Redeemer.SocialFlow.Application.AI;

public enum VisualGenerationFailure
{
    Authentication, RateLimited, Rejected, ProviderUnavailable, InvalidResponse,
    OutputTooLarge, Timeout, ConcurrencyLimit
}

/// <summary>Safe failure code; intentionally excludes provider payloads and inner exceptions.</summary>
public sealed class VisualGenerationException(VisualGenerationFailure failure)
    : Exception("Visual generation failed (" + failure + ").")
{
    public VisualGenerationFailure Failure { get; } = failure;
}
