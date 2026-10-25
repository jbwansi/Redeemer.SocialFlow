using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Infrastructure.Accounts;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Api.Development;

public static class HistoricalWorkspaceOwner
{
    public const string Prefix = "--assign-historical-owner=";
    public static async Task AssignAsync(string userId, SocialFlowDbContext db, UserManager<ApplicationUser> users, CancellationToken token = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is not { EmailConfirmed: true }) throw new InvalidOperationException("Designate an existing confirmed Identity user ID.");
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var workspace = await db.Workspaces.SingleAsync(x => x.Id == Workspace.HistoricalId, token);
        var members = await db.WorkspaceMemberships.Where(x => x.WorkspaceId == Workspace.HistoricalId).ToListAsync(token);
        if (members.Count > 0)
        {
            if (members.Any(x => x.UserId == userId && x.Role == WorkspaceRole.Owner && x.Status == MembershipStatus.Active)) return;
            throw new InvalidOperationException("Historical workspace already has membership. Automatic reassignment is prohibited.");
        }
        db.WorkspaceMemberships.Add(WorkspaceMembership.Join(workspace.Id, userId));
        workspace.Activate();
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
    }
}
