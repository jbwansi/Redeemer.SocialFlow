using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;

using Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
using Redeemer.SocialFlow.Infrastructure.Social;
using Redeemer.SocialFlow.Api.Controllers;
namespace Redeemer.SocialFlow.Api.Publishing;

internal sealed record WorkspacePublicationConfirmation(Guid WorkspaceId, Guid PostId, Guid OperationId,
    WorkspacePublicationProof Proof, string Destination, DateTimeOffset ExpiresAt);

// Shared workspace boundary for the existing durable use case and provider adapters.
public sealed partial class WorkspacePublicationService(SocialFlowDbContext db,
    ISocialSecretProtector secrets, IDataProtectionProvider protection, IPublicationOperationStore operations,
    IHttpClientFactory clients, IServiceProvider services, TimeProvider clock, IConfiguration configuration)
{
    private IDataProtector Protector(SocialPlatform platform) => protection.CreateProtector(platform + ".Workspace.ManualConfirmation.v1");
    private static SocialConnectionException Invalid() => new(409, "Publication ou connexion modifiée, expirée ou non autorisée. Rechargez la fiche.");
    private async Task<SocialPost> Post(SocialPlatform platform, Guid id, CancellationToken ct) =>
        await db.SocialPosts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Platform == platform, ct)
        ?? throw new SocialConnectionException(404, "Publication introuvable dans cet espace.");
    private async Task<SocialConnection> Connection(SocialPlatform platform, Guid id, CancellationToken ct)
    {
        var c = await db.SocialConnections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || c.Platform != platform || c.AccountType != (platform == SocialPlatform.Facebook ? "Page" : "Profile") ||
            c.ConnectionStatus != SocialConnectionStatus.Connected || !c.CanPublish ||
            (platform == SocialPlatform.LinkedIn && c.ExpiresAtUtc is null) || c.ExpiresAtUtc <= clock.GetUtcNow()) throw Invalid();
        return c;
    }
    public async Task<LinkedInPublicationView> Get(SocialPlatform platform, Guid id, CancellationToken ct)
    {
        var post = await Post(platform, id, ct);
        var operation = await db.PublicationOperations.AsNoTracking().SingleOrDefaultAsync(x => x.SocialPostId == id, ct);
        var enabled = platform != SocialPlatform.Facebook || configuration.GetValue<bool>("SocialConnections:Providers:Facebook:PublicationEnabled");
        var accountType = platform == SocialPlatform.Facebook ? "Page" : "Profile";
        var connections = await db.SocialConnections.AsNoTracking().Where(x => x.Platform == platform && x.AccountType == accountType).ToListAsync(ct);
        var allowed = connections.Any(x => x.ConnectionStatus == SocialConnectionStatus.Connected && x.CanPublish &&
            (platform == SocialPlatform.Facebook || x.ExpiresAtUtc is not null) && !(x.ExpiresAtUtc <= clock.GetUtcNow()));
        var editorialReason = post.Status == SocialPostStatus.Approved ? null :
            post.Status != SocialPostStatus.Scheduled ? "approval_required" :
            post.ScheduledAt is null ? "schedule_missing" : post.ScheduledAt > clock.GetUtcNow() ? "scheduled_not_due" : null;
        var manualReason = operation is not null ? "operation_exists" : !enabled ? "publication_disabled" :
            !allowed ? "publishing_permission_unverified" : editorialReason;
        {
            var plan = await db.LinkedInAutomations.AsNoTracking().SingleOrDefaultAsync(x => x.SocialPostId == id, ct);
            var workspaceEnabled = AutomaticEnabled(await ReadPublicationSettings(ct), platform);
            var worker = workspaceEnabled && configuration.GetValue<bool>("SocialConnections:Publication:WorkerEnabled");
            var authorized = plan?.State == LinkedInAutomationState.Authorized;
            var reason = !enabled ? "publication_disabled" : !allowed ? "publishing_permission_unverified" :
                post.Status != SocialPostStatus.Scheduled ? null : plan?.State == LinkedInAutomationState.Blocked ? plan.BlockReason : !workspaceEnabled ? "workspace_automatic_disabled" : !authorized ? "automatic_not_authorized" : !worker ? "worker_disabled" : null;
            return new(operation is null ? null : PublicationOperationDto.FromEntity(operation), manualReason is null,
                operation is null ? reason : null, authorized, worker, manualReason);
        }
    }
    public async Task<LinkedInPreview> Preview(SocialPlatform platform, string userId, Guid id, WorkspaceLinkedInPreviewRequest request, CancellationToken ct)
    {
        if (!(await Get(platform, id, ct)).CanPublish) throw Invalid();
        var post = await Post(platform, id, ct); var c = await Connection(platform, request.ConnectionId, ct);
        // Confirm decrypted capabilities before offering an actionable preview.
        await Credential(platform, userId, c.Id, c.Revision, ct);
        var destination = platform == SocialPlatform.Facebook ? c.ExternalAccountId : "urn:li:person:" + c.ExternalAccountId;
        var proof = new WorkspacePublicationProof(userId, c.Id, c.Revision, post.Content, post.SelectedVisualId, post.UpdatedAt);
        var confirmation = new WorkspacePublicationConfirmation(c.WorkspaceId, id, Guid.NewGuid(), proof, destination, clock.GetUtcNow().AddMinutes(5));
        return new(post.Content, destination, Protector(platform).Protect(JsonSerializer.Serialize(confirmation)));
    }
    public async Task<PublicationOperationDto> Publish(SocialPlatform platform, string userId, Guid id, LinkedInPublishRequest request, CancellationToken ct)
    {
        WorkspacePublicationConfirmation confirmation;
        try { confirmation = JsonSerializer.Deserialize<WorkspacePublicationConfirmation>(Protector(platform).Unprotect(request.Confirmation ?? ""))!; }
        catch { throw new SocialConnectionException(400, "Confirmation invalide. Préparez à nouveau la publication."); }
        if (confirmation is null || confirmation.WorkspaceId != db.ActiveWorkspaceId || confirmation.PostId != id ||
            confirmation.Proof.UserId != userId || confirmation.ExpiresAt <= clock.GetUtcNow())
            throw new SocialConnectionException(400, "Confirmation invalide ou expirée.");
        await Post(platform, id, ct);
        var current = await Get(platform, id, ct);
        if (current.Operation is not null) return current.Operation;
        if (!current.CanPublish) throw Invalid();
        await Credential(platform, userId, confirmation.Proof.ConnectionId, confirmation.Proof.ConnectionRevision, ct);
        return await Execute(platform, confirmation, null, ct);
    }
    private async Task<PublicationOperationDto> Execute(SocialPlatform platform, WorkspacePublicationConfirmation confirmation, Guid? planRevision, CancellationToken ct)
    {
        using var http = clients.CreateClient(platform == SocialPlatform.Facebook ? "WorkspaceFacebookPublication" : "WorkspaceLinkedInPublication");
        var proof = confirmation.Proof;
        IPostPublisher publisher = platform == SocialPlatform.Facebook
            ? new FacebookPostPublisher(http, new SelectedFacebookCredential(async (pageId, token) => {
                var c = await Connection(platform, proof.ConnectionId, token);
                if (c.ExternalAccountId != pageId) throw Invalid();
                var credential = await Credential(platform, proof.UserId, c.Id, proof.ConnectionRevision, token);
                var provider = services.GetServices<IWorkspaceOAuthProvider>().OfType<FacebookWorkspaceOAuthProvider>().Single();
                return await provider.PublicationCredentialAsync(pageId, credential, token);
            }), clock, services.GetService<IVisualStorage>())
            : new LinkedInPostPublisher(http, new SelectedCredential(async token => {
                var credential = await Credential(platform, proof.UserId, proof.ConnectionId, proof.ConnectionRevision, token);
                var c = await Connection(platform, proof.ConnectionId, token);
                return new() { AccessToken = credential.AccessToken, Subject = c.ExternalAccountId, ExpiresAt = credential.ExpiresAtUtc!.Value };
            }), clock, services.GetService<IVisualStorage>());
        return await new PublishScheduledPost(operations, publisher).ExecuteAsync(
            new(confirmation.PostId, confirmation.OperationId, platform.ToString(), confirmation.Destination, proof, planRevision,
                PublishApproved: planRevision is null), ct);
    }
    private async Task<SocialCredential> Credential(SocialPlatform platform, string userId, Guid id, Guid revision, CancellationToken ct)
    {
        var member = await db.WorkspaceMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == db.ActiveWorkspaceId && x.UserId == userId && x.Status == MembershipStatus.Active, ct);
        if (member is null || !WorkspacePermissions.Allows(member.Role, WorkspacePermission.Approve) ||
            !await db.Workspaces.AnyAsync(x => x.Id == db.ActiveWorkspaceId && x.Status == WorkspaceStatus.Active, ct))
            throw new SocialConnectionException(403, "Permission de publication insuffisante.");
        if (platform == SocialPlatform.Facebook && !configuration.GetValue<bool>("SocialConnections:Providers:Facebook:PublicationEnabled")) throw new SocialConnectionException(409, "Publication Facebook désactivée côté serveur.");
        var c = await Connection(platform, id, ct); if (c.Revision != revision) throw Invalid();
        SocialCredential credential;
        try { credential = JsonSerializer.Deserialize<SocialCredential>(secrets.Unprotect(c.ProtectedCredential, $"SocialFlow/SaaS/v1/{c.WorkspaceId}/{c.Id}/credential"))!; }
        catch { throw new SocialConnectionException(503, "Secret de connexion indisponible côté serveur."); }
        if (credential is null || !(platform == SocialPlatform.Facebook ? credential.Scopes.Contains("pages_manage_posts") && credential.Scopes.Contains("pages_read_engagement") : credential.Scopes.Contains("w_member_social")) || (platform == SocialPlatform.LinkedIn && credential.ExpiresAtUtc is null) ||
            credential.ExpiresAtUtc <= clock.GetUtcNow() || string.IsNullOrWhiteSpace(credential.AccessToken)) throw Invalid();
        return credential;
    }
    private sealed class SelectedFacebookCredential(Func<string, CancellationToken, Task<FacebookPublicationCredential?>> get) : IFacebookPublicationCredentialProvider
    { public Task<FacebookPublicationCredential?> GetAsync(string pageId, CancellationToken ct) => get(pageId, ct); }
    private sealed class SelectedCredential(Func<CancellationToken, Task<LinkedInMemberCredential?>> get) : ILinkedInCredentialProvider
    { public Task<LinkedInMemberCredential?> GetAsync(CancellationToken cancellationToken) => get(cancellationToken); }
}
