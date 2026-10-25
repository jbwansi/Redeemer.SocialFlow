namespace Redeemer.SocialFlow.Application.Publishing;

/// <summary>The caller must supply the identity of the configured publisher and a stable operation ID.</summary>
public sealed record PublishScheduledPostRequest(Guid PostId, Guid OperationId, string Provider, string Destination,
    WorkspacePublicationProof? WorkspaceProof = null, Guid? AutomaticPlanRevision = null);

public sealed record WorkspacePublicationProof(string UserId, Guid ConnectionId, Guid ConnectionRevision,
    string Content, Guid? VisualId, DateTimeOffset UpdatedAt);
