namespace Redeemer.SocialFlow.Application.Publishing;

/// <summary>The caller must supply the identity of the configured publisher and a stable operation ID.</summary>
public sealed record PublishScheduledPostRequest(Guid PostId, Guid OperationId, string Provider, string Destination);
