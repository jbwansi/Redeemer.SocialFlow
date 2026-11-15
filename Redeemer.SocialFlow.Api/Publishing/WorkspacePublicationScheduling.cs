using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Social;

namespace Redeemer.SocialFlow.Api.Publishing;

public sealed partial class WorkspacePublicationService
{
    public async Task AuthorizeAutomatic(SocialPlatform platform, string userId, Guid id, Guid connectionId, CancellationToken ct)
    {
        var post = await Post(platform, id, ct);
        if (post.Status != SocialPostStatus.Scheduled || post.ScheduledAt is null ||
            await db.PublicationOperations.AnyAsync(x => x.SocialPostId == id, ct)) throw Invalid();
        var connection = await Connection(platform, connectionId, ct);
        await Credential(platform, userId, connection.Id, connection.Revision, ct);
        // Persist explicit consent for this exact content, visual, destination and scheduled instant.
        // Retain the existing table/model name for database compatibility; no second scheduling queue.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await Post(platform, id, ct);
        if (current.UpdatedAt != post.UpdatedAt || current.Status != SocialPostStatus.Scheduled ||
            await db.PublicationOperations.AnyAsync(x => x.SocialPostId == id, ct)) throw Invalid();
        var plan = await db.LinkedInAutomations.SingleOrDefaultAsync(x => x.SocialPostId == id, ct);
        if (plan is null) { plan = new() { SocialPostId = id }; db.LinkedInAutomations.Add(plan); }
        plan.SocialConnectionId = connectionId; plan.AuthorizedByUserId = userId;
        plan.AuthorizedAtUtc = clock.GetUtcNow(); plan.ScheduledAtUtc = post.ScheduledAt.Value;
        plan.PostUpdatedAtUtc = post.UpdatedAt; plan.Revision = Guid.NewGuid();
        plan.State = LinkedInAutomationState.Authorized; plan.BlockReason = null;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    public async Task CancelAutomatic(Guid id, CancellationToken ct)
    {
        await Post(SocialPlatform.Facebook, id, ct);
        var plan = await db.LinkedInAutomations.SingleOrDefaultAsync(x => x.SocialPostId == id, ct);
        if (plan is null) return;
        plan.State = LinkedInAutomationState.Blocked; plan.BlockReason = "automatic_cancelled";
        plan.Revision = Guid.NewGuid(); await db.SaveChangesAsync(ct);
    }

    public async Task ProcessDueAsync(CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("SocialConnections:Publication:WorkerEnabled") ||
            !configuration.GetValue<bool>("SocialConnections:Providers:Facebook:PublicationEnabled")) return;
        var plans = await db.LinkedInAutomations.AsNoTracking().Where(x => x.State == LinkedInAutomationState.Authorized &&
            db.SocialPosts.Any(p => p.Id == x.SocialPostId && p.Platform == SocialPlatform.Facebook) &&
            !db.PublicationOperations.Any(o => o.SocialPostId == x.SocialPostId)).ToListAsync(ct);
        foreach (var plan in plans.Where(x => x.ScheduledAtUtc <= clock.GetUtcNow()).OrderBy(x => x.ScheduledAtUtc).ThenBy(x => x.SocialPostId).Take(20))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var post = await Post(SocialPlatform.Facebook, plan.SocialPostId, ct);
                if (post.Status != SocialPostStatus.Scheduled || post.ScheduledAt != plan.ScheduledAtUtc || post.UpdatedAt != plan.PostUpdatedAtUtc) throw Invalid();
                var connection = await Connection(SocialPlatform.Facebook, plan.SocialConnectionId, ct);
                var credential = await Credential(SocialPlatform.Facebook, plan.AuthorizedByUserId, connection.Id, connection.Revision, ct);
                // A failed preflight has no publication side effect and creates no durable dispatch claim.
                var provider = services.GetServices<IWorkspaceOAuthProvider>().OfType<FacebookWorkspaceOAuthProvider>().Single();
                await provider.PublicationCredentialAsync(connection.ExternalAccountId, credential, ct);
                var proof = new Application.Publishing.WorkspacePublicationProof(plan.AuthorizedByUserId, connection.Id,
                    connection.Revision, post.Content, post.SelectedVisualId, post.UpdatedAt);
                await Execute(SocialPlatform.Facebook, new(connection.WorkspaceId, post.Id, Guid.NewGuid(), proof,
                    connection.ExternalAccountId, clock.GetUtcNow().AddMinutes(5)), plan.Revision, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // No raw provider errors or secrets. Do not retry after a durable claim, including uncertainty.
                if (await db.PublicationOperations.AnyAsync(x => x.SocialPostId == plan.SocialPostId, ct)) continue;
                var current = await db.LinkedInAutomations.SingleOrDefaultAsync(x => x.SocialPostId == plan.SocialPostId, ct);
                if (current is null || current.Revision != plan.Revision) continue;
                current.State = LinkedInAutomationState.Blocked; current.BlockReason = "authorization_or_validation_failed";
                current.Revision = Guid.NewGuid(); await db.SaveChangesAsync(ct);
            }
        }
    }
}
