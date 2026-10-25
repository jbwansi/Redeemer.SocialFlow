namespace Redeemer.SocialFlow.Domain.Entities;

public enum InvitationStatus { Pending = 1, Accepted = 2, Revoked = 3, Expired = 4 }
public sealed class WorkspaceInvitation : IWorkspaceResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public WorkspaceRole Role { get; set; }
    public string TokenHash { get; set; } = "";
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string InvitedByUserId { get; set; } = "";
}
