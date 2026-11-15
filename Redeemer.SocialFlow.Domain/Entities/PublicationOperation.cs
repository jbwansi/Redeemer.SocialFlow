using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public sealed class PublicationOperation : IWorkspaceResource
{
    public Guid WorkspaceId { get; private set; }
    private PublicationOperation() { }
    public Guid OperationId { get; private set; }
    public Guid SocialPostId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Destination { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public SocialPlatform Platform { get; private set; }
    public PublicationOperationState State { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public PublicationVisualSnapshot? Visual { get; private set; }
    public string? ExternalId { get; private set; }

    public static PublicationOperation Claim(SocialPost post, Guid operationId, string provider,
        string destination, TimeProvider? timeProvider = null, PostVisual? visual = null, bool publishApproved = false)
    {
        ArgumentNullException.ThrowIfNull(post);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        if (operationId == Guid.Empty) throw new DomainException("An operation ID is required.");
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(post.Content))
            throw new DomainException("Provider, destination and publication content are required.");
        var eligible = post.Status == SocialPostStatus.Scheduled && post.ScheduledAt is not null && post.ScheduledAt <= now;
        if (post.IsDeleted || !(eligible || (publishApproved && post.Status == SocialPostStatus.Approved)))
            throw new DomainException("Publication requires an explicitly confirmed approved post or a scheduled post that is due.");
        if (post.SelectedVisualId is not null &&
            (visual is null || visual.Id != post.SelectedVisualId || visual.SocialPostId != post.Id ||
             !visual.IsReady || visual.IsDeleted || visual.ValidatedAt is null))
            throw new DomainException("The selected visual must be ready and validated.");
        return new PublicationOperation
        {
            OperationId = operationId, SocialPostId = post.Id, Provider = provider, Destination = destination,
            Content = post.Content, Platform = post.Platform, State = PublicationOperationState.Indeterminate,
            ClaimedAt = now, UpdatedAt = now,
            Visual = post.SelectedVisualId is null ? null : PublicationVisualSnapshot.From(visual!)
        };
    }

    public void Complete(PublicationOperationState state, string? externalId, TimeProvider? timeProvider = null)
    {
        if (CompletedAt is not null) throw new DomainException("The operation already has a recorded result.");
        if (!Enum.IsDefined(state)) throw new DomainException("Invalid publication state.");
        if (state == PublicationOperationState.Confirmed ? string.IsNullOrWhiteSpace(externalId) : externalId is not null)
            throw new DomainException("Only a confirmed publication requires an external ID.");
        State = state;
        ExternalId = externalId;
        CompletedAt = UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow();
    }
}
