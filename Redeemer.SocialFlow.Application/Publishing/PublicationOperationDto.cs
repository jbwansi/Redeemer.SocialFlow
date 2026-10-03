using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.Publishing;

public sealed record PublicationOperationDto(Guid OperationId, Guid SocialPostId, string Provider,
    string Destination, string Content, SocialPlatform Platform, PublicationOperationState State,
    DateTimeOffset ClaimedAt, DateTimeOffset UpdatedAt, DateTimeOffset? CompletedAt, string? ExternalId)
{
    public static PublicationOperationDto FromEntity(PublicationOperation operation) => new(operation.OperationId,
        operation.SocialPostId, operation.Provider, operation.Destination, operation.Content, operation.Platform,
        operation.State, operation.ClaimedAt, operation.UpdatedAt, operation.CompletedAt, operation.ExternalId);
}
