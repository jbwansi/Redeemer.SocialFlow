using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public enum WorkspaceType { Personal = 1, Organization = 2 }
public enum WorkspaceStatus { Active = 1, Suspended = 2 }
public enum WorkspaceRole { Owner = 1, Member = 2 }
public enum MembershipStatus { Active = 1, Inactive = 2 }

public interface IWorkspaceResource { Guid WorkspaceId { get; } }

public sealed class Workspace
{
    // Reserved identity, never an implicit tenant for users or background processes.
    public static readonly Guid HistoricalId = new("a661b775-1b98-4c97-8ec0-2a461f0c7001");
    private Workspace() { }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public WorkspaceType Type { get; private set; }
    public WorkspaceStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? PersonalOwnerUserId { get; private set; }
    public static Workspace Create(string name, WorkspaceType type, string ownerUserId, TimeProvider? clock = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120 || !Enum.IsDefined(type) || string.IsNullOrWhiteSpace(ownerUserId))
            throw new DomainException("Invalid workspace.");
        return new() { Id = Guid.NewGuid(), Name = name.Trim(), Type = type, Status = WorkspaceStatus.Active,
            CreatedAtUtc = (clock ?? TimeProvider.System).GetUtcNow(), PersonalOwnerUserId = type == WorkspaceType.Personal ? ownerUserId : null };
    }
    public void Activate() => Status = WorkspaceStatus.Active;
}

public sealed class WorkspaceMembership
{
    private WorkspaceMembership() { }
    public Guid WorkspaceId { get; private set; }
    public string UserId { get; private set; } = "";
    public WorkspaceRole Role { get; private set; }
    public MembershipStatus Status { get; private set; }
    public DateTimeOffset JoinedAtUtc { get; private set; }
    public static WorkspaceMembership Join(Guid workspaceId, string userId, WorkspaceRole role = WorkspaceRole.Owner, TimeProvider? clock = null)
    {
        if (workspaceId == Guid.Empty || string.IsNullOrWhiteSpace(userId) || userId.Length > 450 || !Enum.IsDefined(role))
            throw new DomainException("Invalid workspace membership.");
        return new() { WorkspaceId = workspaceId, UserId = userId, Role = role, Status = MembershipStatus.Active,
            JoinedAtUtc = (clock ?? TimeProvider.System).GetUtcNow() };
    }
    public void Change(WorkspaceRole role, MembershipStatus status, int activeOwnerCount)
    {
        if (!Enum.IsDefined(role) || !Enum.IsDefined(status)) throw new DomainException("Invalid membership.");
        if (Role == WorkspaceRole.Owner && Status == MembershipStatus.Active &&
            (role != WorkspaceRole.Owner || status != MembershipStatus.Active) && activeOwnerCount <= 1)
            throw new DomainException("The last active owner cannot be removed.");
        Role = role; Status = status;
    }
}
