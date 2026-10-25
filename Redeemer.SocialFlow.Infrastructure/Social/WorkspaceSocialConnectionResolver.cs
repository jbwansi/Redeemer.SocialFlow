using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Infrastructure.Persistence;
namespace Redeemer.SocialFlow.Infrastructure.Social;

// Metadata only. No publisher or worker is registered by this resolver.
public sealed class WorkspaceSocialConnectionResolver(DbContextOptions<SocialFlowDbContext> options, IWorkspaceAccessContext access, TimeProvider clock) : IWorkspaceSocialConnectionResolver
{
    public async Task<WorkspaceSocialConnection?> ResolveAsync(Guid workspaceId, Guid connectionId, CancellationToken token)
    {
        if (access.WorkspaceId != workspaceId) return null;
        await using var db = new SocialFlowDbContext(options, access);
        var connection = await db.SocialConnections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == connectionId, token);
        return connection is { ConnectionStatus: SocialConnectionStatus.Connected, PublishingPermissionGranted: true } && !(connection.ExpiresAtUtc <= clock.GetUtcNow())
            ? new(workspaceId, connection.Id, connection.Platform.ToString()) : null;
    }
}
