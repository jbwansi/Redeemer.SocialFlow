namespace Redeemer.SocialFlow.Application.Publishing;

/// <summary>
/// Immutable outcome, constructed through factories to prevent invalid combinations.
/// No raw provider response or exception details are part of this contract.
/// </summary>
public sealed record PostPublicationResult
{
    public PostPublicationOutcome Outcome { get; }
    /// <summary>Opaque identifier of a confirmed publication; null for other outcomes.</summary>
    public string? ExternalId { get; }

    private PostPublicationResult(PostPublicationOutcome outcome, string? externalId)
    {
        Outcome = outcome;
        ExternalId = externalId;
    }

    public static PostPublicationResult Confirmed(string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        return new(PostPublicationOutcome.Confirmed, externalId);
    }

    public static PostPublicationResult Failed() => new(PostPublicationOutcome.Failed, null);
    public static PostPublicationResult Indeterminate() => new(PostPublicationOutcome.Indeterminate, null);
}
