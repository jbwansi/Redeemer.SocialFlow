using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Social;

namespace Redeemer.SocialFlow.Api.Publishing;

public sealed partial class WorkspacePublicationService
{
    private async Task<WorkspacePublicationSettings> ReadPublicationSettings(CancellationToken ct)
    {
        var id = db.ActiveWorkspaceId;
        var settings = await db.Workspaces.AsNoTracking().Where(x => x.Id == id)
            .Select(x => x.PublicationSettingsJson).SingleOrDefaultAsync(ct);
        return WorkspacePublicationSettings.Read(settings);
    }

    private static bool AutomaticEnabled(WorkspacePublicationSettings settings, SocialPlatform platform) => platform switch
    {
        SocialPlatform.Facebook => settings.FacebookAutomaticEnabled != false,
        SocialPlatform.LinkedIn => settings.LinkedInAutomaticEnabled,
        _ => false
    };

    public async Task AuthorizeAutomatic(SocialPlatform platform, string userId, Guid id, Guid connectionId, CancellationToken ct)
    {
        if (!AutomaticEnabled(await ReadPublicationSettings(ct), platform)) throw new SocialConnectionException(409, "La publication automatique est désactivée dans cet espace.");
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

    public async Task CancelAutomatic(SocialPlatform platform, Guid id, CancellationToken ct)
    {
        await Post(platform, id, ct);
        var plan = await db.LinkedInAutomations.SingleOrDefaultAsync(x => x.SocialPostId == id, ct);
        if (plan is null) return;
        plan.State = LinkedInAutomationState.Blocked; plan.BlockReason = "automatic_cancelled";
        plan.Revision = Guid.NewGuid(); await db.SaveChangesAsync(ct);
    }

    public async Task ProcessDueAsync(CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("SocialConnections:Publication:WorkerEnabled")) return;
        var settings = await ReadPublicationSettings(ct);
        var facebook = AutomaticEnabled(settings, SocialPlatform.Facebook) &&
            configuration.GetValue<bool>("SocialConnections:Providers:Facebook:PublicationEnabled");
        var linkedIn = AutomaticEnabled(settings, SocialPlatform.LinkedIn);
        if (!facebook && !linkedIn) return;
        var plans = await (from plan in db.LinkedInAutomations.AsNoTracking()
            join post in db.SocialPosts.AsNoTracking() on plan.SocialPostId equals post.Id
            where plan.State == LinkedInAutomationState.Authorized &&
                (facebook && post.Platform == SocialPlatform.Facebook || linkedIn && post.Platform == SocialPlatform.LinkedIn) &&
                !db.PublicationOperations.Any(o => o.SocialPostId == plan.SocialPostId)
            select new { Plan = plan, post.Platform }).ToListAsync(ct);
        foreach (var item in plans.Where(x => x.Plan.ScheduledAtUtc <= clock.GetUtcNow())
            .OrderBy(x => x.Plan.ScheduledAtUtc).ThenBy(x => x.Plan.SocialPostId).Take(20))
        {
            ct.ThrowIfCancellationRequested();
            var (plan, platform) = (item.Plan, item.Platform);
            if (!AutomaticEnabled(await ReadPublicationSettings(ct), platform)) continue;
            try
            {
                var post = await Post(platform, plan.SocialPostId, ct);
                if (post.Status != SocialPostStatus.Scheduled || post.ScheduledAt != plan.ScheduledAtUtc || post.UpdatedAt != plan.PostUpdatedAtUtc) throw Invalid();
                var connection = await Connection(platform, plan.SocialConnectionId, ct);
                var credential = await Credential(platform, plan.AuthorizedByUserId, connection.Id, connection.Revision, ct);
                // A failed preflight has no publication side effect and creates no durable dispatch claim.
                if (platform == SocialPlatform.Facebook)
                {
                    var provider = services.GetServices<IWorkspaceOAuthProvider>().OfType<FacebookWorkspaceOAuthProvider>().Single();
                    await provider.PublicationCredentialAsync(connection.ExternalAccountId, credential, ct);
                }
                if (!AutomaticEnabled(await ReadPublicationSettings(ct), platform)) continue;
                var proof = new Application.Publishing.WorkspacePublicationProof(plan.AuthorizedByUserId, connection.Id,
                    connection.Revision, post.Content, post.SelectedVisualId, post.UpdatedAt);
                var destination = platform == SocialPlatform.Facebook ? connection.ExternalAccountId : "urn:li:person:" + connection.ExternalAccountId;
                await Execute(platform, new(connection.WorkspaceId, post.Id, Guid.NewGuid(), proof,
                    destination, clock.GetUtcNow().AddMinutes(5)), plan.Revision, ct);
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
