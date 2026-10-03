namespace Redeemer.SocialFlow.Application.Publishing;

public sealed record PublicationClaim(PublicationOperationDto Operation, bool Acquired);

/// <summary>Atomic persistence boundary. No database transaction may span the external call.</summary>
public interface IPublicationOperationStore
{
    /// <summary>
    /// Atomically validates the current post and persists its immutable snapshot before granting ownership.
    /// At most one operation per post; all subsequent calls return Acquired=false, regardless of outcome.
    /// Reusing an OperationId for a different post must fail without granting ownership.
    /// </summary>
    Task<PublicationClaim> TryClaimAsync(PublishScheduledPostRequest request, CancellationToken cancellationToken = default);
    /// <summary>Atomically records the result and the permitted SocialPost transition; never dispatches or retries.</summary>
    Task<PublicationOperationDto> CompleteAsync(Guid operationId, PostPublicationResult result, CancellationToken cancellationToken = default);
}
