using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Application.Accounts;

public enum WorkspacePermission { Read, Edit, Approve, ManageTeam }
public static class WorkspacePermissions
{
    public static bool Allows(WorkspaceRole role, WorkspacePermission permission) => permission switch
    {
        WorkspacePermission.Read => Enum.IsDefined(role),
        WorkspacePermission.Edit or WorkspacePermission.Approve => role is WorkspaceRole.Owner or WorkspaceRole.Admin or WorkspaceRole.Editor,
        WorkspacePermission.ManageTeam => role is WorkspaceRole.Owner or WorkspaceRole.Admin,
        _ => false
    };
    public static bool CanManage(WorkspaceRole actor, WorkspaceRole target, WorkspaceRole proposed) =>
        Enum.IsDefined(target) && Enum.IsDefined(proposed) &&
        (actor == WorkspaceRole.Owner || actor == WorkspaceRole.Admin && target != WorkspaceRole.Owner && proposed != WorkspaceRole.Owner);
}
public sealed class CollaborationException(int status, string message) : Exception(message)
{ public int Status { get; } = status; }
public interface ICollaborationActor { string UserId { get; } string Email { get; } }
public sealed record TeamMember(string UserId, string Email, WorkspaceRole Role, Guid Revision);
public sealed record TeamInvitation(Guid Id, string Email, WorkspaceRole Role, DateTimeOffset ExpiresAtUtc, Guid Revision);
public sealed record WorkspaceTeam(IReadOnlyList<TeamMember> Members, IReadOnlyList<TeamInvitation> Invitations, WorkspaceRole MyRole);
public interface IWorkspaceCollaboration
{
    Task<WorkspaceTeam> GetAsync(Guid workspaceId, CancellationToken token);
    Task InviteAsync(Guid workspaceId, string email, WorkspaceRole role, CancellationToken token);
    Task ChangeAsync(Guid workspaceId, string userId, WorkspaceRole role, Guid revision, CancellationToken token);
    Task RemoveAsync(Guid workspaceId, string userId, Guid revision, CancellationToken token);
    Task LeaveAsync(Guid workspaceId, Guid revision, CancellationToken token);
    Task RevokeAsync(Guid workspaceId, Guid invitationId, Guid revision, CancellationToken token);
    Task<Guid> AcceptAsync(string code, CancellationToken token);
}
