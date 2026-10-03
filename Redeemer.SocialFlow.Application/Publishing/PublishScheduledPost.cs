namespace Redeemer.SocialFlow.Application.Publishing;

public interface IPublishScheduledPost
{
    Task<PublicationOperationDto> ExecuteAsync(PublishScheduledPostRequest request, CancellationToken cancellationToken = default);
}

public sealed class PublishScheduledPost(IPublicationOperationStore operations, IPostPublisher publisher) : IPublishScheduledPost
{
    public async Task<PublicationOperationDto> ExecuteAsync(PublishScheduledPostRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.PostId == Guid.Empty || request.OperationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Destination))
            throw new ArgumentException("Post, operation, provider and destination are required.", nameof(request));
        var claim = await operations.TryClaimAsync(request, cancellationToken);
        if (!claim.Acquired) return claim.Operation;
        var operation = claim.Operation;
        PostPublicationResult result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = await publisher.PublishAsync(new(operation.Content, operation.Platform,
                operation.Destination, operation.OperationId), cancellationToken);
        }
        catch (Exception)
        {
            // A timeout/cancellation/exception cannot prove that no publication occurred.
            // No exception payload is persisted: provider errors can contain credentials.
            result = PostPublicationResult.Indeterminate();
        }
        // Finalize even if the client cancelled after dispatch. The claim is already durable;
        // if finalization fails or the process dies, its stored state remains Indeterminate.
        return await operations.CompleteAsync(operation.OperationId, result, CancellationToken.None);
    }
}
