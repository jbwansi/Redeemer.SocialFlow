using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public sealed class PublicationOperation
{
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
    public string? ExternalId { get; private set; }

    public static PublicationOperation Claim(SocialPost post, Guid operationId, string provider,
        string destination, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(post);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        if (operationId == Guid.Empty) throw new DomainException("An operation ID is required.");
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(post.Content))
            throw new DomainException("Provider, destination and publication content are required.");
        if (post.IsDeleted || post.Status != SocialPostStatus.Scheduled || post.ScheduledAt is null || post.ScheduledAt > now)
            throw new DomainException("Only a scheduled post that is due can be published.");
        return new PublicationOperation
        {
            OperationId = operationId, SocialPostId = post.Id, Provider = provider, Destination = destination,
            Content = post.Content, Platform = post.Platform, State = PublicationOperationState.Indeterminate,
            ClaimedAt = now, UpdatedAt = now
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
