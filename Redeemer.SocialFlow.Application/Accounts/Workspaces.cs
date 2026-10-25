using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Application.Accounts;

public sealed record WorkspaceDto(Guid Id, string Name, WorkspaceType Type, WorkspaceStatus Status, WorkspaceRole Role);
public interface IWorkspaceDirectory
{
    Task<WorkspaceDto> EnsurePersonalAsync(string confirmedUserId, CancellationToken token = default);
    Task<IReadOnlyList<WorkspaceDto>> ListAsync(string userId, CancellationToken token = default);
    Task<WorkspaceDto?> GetAsync(string userId, Guid workspaceId, CancellationToken token = default);
    Task<WorkspaceDto> CreateOrganizationAsync(string userId, string name, CancellationToken token = default);
}

// Future publications must resolve an explicitly owned connection. No implementation exposes global tokens.
public sealed record WorkspaceSocialConnection(Guid WorkspaceId, Guid ConnectionId, string Provider);
public interface IWorkspaceSocialConnectionResolver
{
    Task<WorkspaceSocialConnection?> ResolveAsync(Guid workspaceId, Guid connectionId, CancellationToken token);
}
