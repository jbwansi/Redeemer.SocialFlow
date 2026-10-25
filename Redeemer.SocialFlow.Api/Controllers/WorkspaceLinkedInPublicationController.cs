using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;

namespace Redeemer.SocialFlow.Api.Controllers;

public sealed record WorkspaceLinkedInPreviewRequest(Guid ConnectionId);
internal sealed record WorkspaceLinkedInConfirmation(Guid WorkspaceId, Guid PostId, Guid OperationId,
    WorkspacePublicationProof Proof, string Destination, DateTimeOffset ExpiresAt);

// This is a tenant-aware entry point to the existing durable use case and LinkedIn publisher.
[ApiController, Route("api/linkedin/posts/{id:guid}"), Authorize(Policy = "AccountSession")]
[Authorize(Policy = "WorkspaceApprove")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceLinkedInPublicationController(SocialFlowDbContext db, ICollaborationActor actor,
    ISocialSecretProtector secrets, IDataProtectionProvider protection, IPublicationOperationStore operations,
    IHttpClientFactory clients, IServiceProvider services, TimeProvider clock) : ControllerBase
{
    private IDataProtector Protector => protection.CreateProtector("LinkedIn.Workspace.ManualConfirmation.v1");
    private static SocialConnectionException Invalid() => new(409, "Publication ou connexion modifiée, expirée ou non autorisée. Rechargez la fiche.");
    private async Task<SocialPost> Post(Guid id, CancellationToken ct) =>
        await db.SocialPosts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Platform == SocialPlatform.LinkedIn, ct)
        ?? throw new SocialConnectionException(404, "Publication LinkedIn introuvable dans cet espace.");
    private async Task<SocialConnection> Connection(Guid id, CancellationToken ct)
    {
        var c = await db.SocialConnections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || c.Platform != SocialPlatform.LinkedIn || c.AccountType != "Profile" ||
            c.ConnectionStatus != SocialConnectionStatus.Connected || !c.PublishingPermissionGranted ||
            c.ExpiresAtUtc is null || c.ExpiresAtUtc <= clock.GetUtcNow()) throw Invalid();
        return c;
    }
    [HttpGet("publication")]
    public async Task<LinkedInPublicationView> Get(Guid id, CancellationToken ct)
    {
        var post = await Post(id, ct);
        var operation = await db.PublicationOperations.AsNoTracking().SingleOrDefaultAsync(x => x.SocialPostId == id, ct);
        return new(operation is null ? null : PublicationOperationDto.FromEntity(operation), operation is null &&
            post.Status == SocialPostStatus.Scheduled && post.ScheduledAt <= clock.GetUtcNow());
    }
    [HttpPost("preview")]
    public async Task<LinkedInPreview> Preview(Guid id, WorkspaceLinkedInPreviewRequest request, CancellationToken ct)
    {
        if (!(await Get(id, ct)).CanPublish) throw Invalid();
        var post = await Post(id, ct); var c = await Connection(request.ConnectionId, ct);
        // Confirm decrypted capabilities before offering an actionable preview.
        await Credential(c.Id, c.Revision, ct);
        var destination = "urn:li:person:" + c.ExternalAccountId;
        var proof = new WorkspacePublicationProof(actor.UserId, c.Id, c.Revision, post.Content, post.SelectedVisualId, post.UpdatedAt);
        var confirmation = new WorkspaceLinkedInConfirmation(c.WorkspaceId, id, Guid.NewGuid(), proof, destination, clock.GetUtcNow().AddMinutes(5));
        return new(post.Content, destination, Protector.Protect(JsonSerializer.Serialize(confirmation)));
    }
    [HttpPost("publish")]
    public async Task<PublicationOperationDto> Publish(Guid id, LinkedInPublishRequest request, CancellationToken ct)
    {
        WorkspaceLinkedInConfirmation confirmation;
        try { confirmation = JsonSerializer.Deserialize<WorkspaceLinkedInConfirmation>(Protector.Unprotect(request.Confirmation ?? ""))!; }
        catch { throw new SocialConnectionException(400, "Confirmation invalide. Préparez à nouveau la publication."); }
        if (confirmation is null || confirmation.WorkspaceId != db.ActiveWorkspaceId || confirmation.PostId != id ||
            confirmation.Proof.UserId != actor.UserId || confirmation.ExpiresAt <= clock.GetUtcNow())
            throw new SocialConnectionException(400, "Confirmation invalide ou expirée.");
        await Post(id, ct);
        var current = await Get(id, ct);
        if (current.Operation is not null) return current.Operation;
        await Credential(confirmation.Proof.ConnectionId, confirmation.Proof.ConnectionRevision, ct);
        using var http = clients.CreateClient("WorkspaceLinkedInPublication");
        var credentials = new SelectedCredential(token => Credential(confirmation.Proof.ConnectionId, confirmation.Proof.ConnectionRevision, token));
        var publisher = new LinkedInPostPublisher(http, credentials, clock, services.GetService<IVisualStorage>());
        return await new PublishScheduledPost(operations, publisher).ExecuteAsync(
            new(id, confirmation.OperationId, "LinkedIn", confirmation.Destination, confirmation.Proof), ct);
    }
    private async Task<LinkedInMemberCredential?> Credential(Guid id, Guid revision, CancellationToken ct)
    {
        var member = await db.WorkspaceMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == db.ActiveWorkspaceId && x.UserId == actor.UserId && x.Status == MembershipStatus.Active, ct);
        if (member is null || !WorkspacePermissions.Allows(member.Role, WorkspacePermission.Approve) ||
            !await db.Workspaces.AnyAsync(x => x.Id == db.ActiveWorkspaceId && x.Status == WorkspaceStatus.Active, ct))
            throw new SocialConnectionException(403, "Permission de publication insuffisante.");
        var c = await Connection(id, ct); if (c.Revision != revision) throw Invalid();
        SocialCredential credential;
        try { credential = JsonSerializer.Deserialize<SocialCredential>(secrets.Unprotect(c.ProtectedCredential, $"SocialFlow/SaaS/v1/{c.WorkspaceId}/{c.Id}/credential"))!; }
        catch { throw new SocialConnectionException(503, "Secret LinkedIn indisponible côté serveur."); }
        if (credential is null || !credential.Scopes.Contains("w_member_social") || credential.ExpiresAtUtc is null ||
            credential.ExpiresAtUtc <= clock.GetUtcNow() || string.IsNullOrWhiteSpace(credential.AccessToken)) throw Invalid();
        return new() { AccessToken = credential.AccessToken, Subject = c.ExternalAccountId, ExpiresAt = credential.ExpiresAtUtc.Value };
    }
    private sealed class SelectedCredential(Func<CancellationToken, Task<LinkedInMemberCredential?>> get) : ILinkedInCredentialProvider
    { public Task<LinkedInMemberCredential?> GetAsync(CancellationToken cancellationToken) => get(cancellationToken); }
}

