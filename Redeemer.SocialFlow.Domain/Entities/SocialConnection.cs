using Redeemer.SocialFlow.Domain.Enums;
namespace Redeemer.SocialFlow.Domain.Entities;

public enum SocialConnectionStatus { Connected = 1, Expired = 2, Revoked = 3, ReauthorizationRequired = 4 }
public sealed class SocialConnection : IWorkspaceResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public SocialPlatform Platform { get; set; }
    public string ExternalAccountId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AccountType { get; set; } = "";
    public SocialConnectionStatus ConnectionStatus { get; set; }
    public string ConnectedByUserId { get; set; } = "";
    public DateTimeOffset ConnectedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? LastValidatedAtUtc { get; set; }
    public string ProtectedCredential { get; set; } = "";
    public bool PublishingPermissionGranted { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
public enum SocialOAuthStatus { Started = 1, Exchanging = 2, Selection = 3, Completed = 4, Failed = 5 }
public sealed class SocialOAuthSession : IWorkspaceResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public SocialPlatform Platform { get; set; }
    public string UserId { get; set; } = "";
    public string StateHash { get; set; } = "";
    public string ProtectedData { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public SocialOAuthStatus Status { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
public sealed class PostSocialDestination : IWorkspaceResource
{
    public Guid WorkspaceId { get; set; }
    public Guid SocialPostId { get; set; }
    public Guid SocialConnectionId { get; set; }
}
