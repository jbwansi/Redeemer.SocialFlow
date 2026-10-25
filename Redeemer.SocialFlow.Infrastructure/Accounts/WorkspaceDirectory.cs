using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Infrastructure.Accounts;

// Directory queries always include the authenticated user. It never queries tenant resources.
public sealed class WorkspaceDirectory(DbContextOptions<SocialFlowDbContext> options) : IWorkspaceDirectory
{
    private static IQueryable<WorkspaceDto> Accessible(SocialFlowDbContext db, string userId, Guid? workspaceId = null) =>
        from workspace in db.Workspaces.AsNoTracking()
        join member in db.WorkspaceMemberships.AsNoTracking() on workspace.Id equals member.WorkspaceId
        where member.UserId == userId && member.Status == MembershipStatus.Active && workspace.Status == WorkspaceStatus.Active
            && (workspaceId == null || workspace.Id == workspaceId)
        orderby workspace.Name, workspace.Id
        select new WorkspaceDto(workspace.Id, workspace.Name, workspace.Type, workspace.Status, member.Role);

    public async Task<IReadOnlyList<WorkspaceDto>> ListAsync(string userId, CancellationToken token = default)
    {
        await using var db = new SocialFlowDbContext(options);
        return await Accessible(db, userId).ToListAsync(token);
    }
    public async Task<WorkspaceDto?> GetAsync(string userId, Guid workspaceId, CancellationToken token = default)
    {
        await using var db = new SocialFlowDbContext(options);
        return await Accessible(db, userId, workspaceId).SingleOrDefaultAsync(token);
    }
    public async Task<WorkspaceDto> EnsurePersonalAsync(string confirmedUserId, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmedUserId);
        await using var db = new SocialFlowDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var existing = await db.Workspaces.SingleOrDefaultAsync(x => x.PersonalOwnerUserId == confirmedUserId, token);
        if (existing is not null)
            return await Accessible(db, confirmedUserId, existing.Id).SingleOrDefaultAsync(token)
                ?? throw new DomainException("Personal workspace is unavailable.");
        var workspace = Workspace.Create("Mon espace personnel", WorkspaceType.Personal, confirmedUserId);
        db.Workspaces.Add(workspace);
        db.WorkspaceMemberships.Add(WorkspaceMembership.Join(workspace.Id, confirmedUserId));
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return new(workspace.Id, workspace.Name, workspace.Type, workspace.Status, WorkspaceRole.Owner);
    }
    public async Task<WorkspaceDto> CreateOrganizationAsync(string userId, string name, CancellationToken token = default)
    {
        var workspace = Workspace.Create(name, WorkspaceType.Organization, userId);
        await using var db = new SocialFlowDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        db.Workspaces.Add(workspace);
        db.WorkspaceMemberships.Add(WorkspaceMembership.Join(workspace.Id, userId));
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return new(workspace.Id, workspace.Name, workspace.Type, workspace.Status, WorkspaceRole.Owner);
    }
}
