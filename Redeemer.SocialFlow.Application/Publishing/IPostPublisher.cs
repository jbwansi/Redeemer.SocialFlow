namespace Redeemer.SocialFlow.Application.Publishing;

/// <summary>Provider-independent external publication boundary; does not change SocialPost state.</summary>
public interface IPostPublisher
{
    /// <summary>
    /// Returns Confirmed only when the provider attests publication, Failed only when
    /// non-publication is certain, and Indeterminate when publication remains possible.
    /// A timeout, lost response, or cancellation after possible dispatch MUST NOT be
    /// classified as Failed without evidence that publication did not occur.
    /// </summary>
    /// <remarks>
    /// Observe cancellation. Cancellation before dispatch may throw OperationCanceledException;
    /// after possible dispatch, report Indeterminate unless the outcome is known.
    /// An exception alone is never proof of non-publication.
    /// OperationId is caller-supplied correlation identity, not a promise of provider idempotency.
    /// Reusing it does not by itself make a repeated call safe. No automatic retry is implied.
    /// </remarks>
    Task<PostPublicationResult> PublishAsync(PostPublicationRequest request, CancellationToken cancellationToken = default);
}
