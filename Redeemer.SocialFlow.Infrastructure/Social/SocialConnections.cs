using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
namespace Redeemer.SocialFlow.Infrastructure.Social;

public sealed class SocialConnections(DbContextOptions<SocialFlowDbContext> options, ICollaborationActor actor,
    IEnumerable<IWorkspaceOAuthProvider> providers, ISocialSecretProtector secrets, TimeProvider clock) : ISocialConnections
{
    private SocialFlowDbContext Db(Guid id) => new(options, new ExplicitWorkspaceContext(id));
    private static SocialConnectionException Denied() => new(403, "Permissions insuffisantes ou espace inaccessible.");
    private static SocialConnectionException Invalid() => new(400, "Parcours OAuth invalide, expiré ou déjà utilisé. Recommencez la connexion.");
    private static SocialConnectionException Missing() => new(404, "Connexion ou destination introuvable dans cet espace.");
    private static string Purpose(Guid workspace, Guid id, string kind) => $"SocialFlow/SaaS/v1/{workspace}/{id}/{kind}";
    private string Seal<T>(T data, Guid workspace, Guid id, string kind) => secrets.Protect(JsonSerializer.Serialize(data), Purpose(workspace, id, kind));
    private T Open<T>(string data, Guid workspace, Guid id, string kind)
    {
        try { return JsonSerializer.Deserialize<T>(secrets.Unprotect(data, Purpose(workspace, id, kind)))!; }
        catch { throw new SocialConnectionException(503, "Secret indisponible. Vérifiez le trousseau de chiffrement côté serveur."); }
    }
    private async Task Access(SocialFlowDbContext db, Guid id, WorkspacePermission permission, CancellationToken ct)
    {
        var active = await db.Workspaces.AnyAsync(x => x.Id == id && x.Status == WorkspaceStatus.Active, ct);
        var member = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == actor.UserId && x.Status == MembershipStatus.Active, ct);
        if (!active || member is null || !WorkspacePermissions.Allows(member.Role, permission)) throw Denied();
    }
    private IWorkspaceOAuthProvider Provider(SocialPlatform platform)
    {
        var p = providers.SingleOrDefault(x => x.Platform == platform);
        if (!secrets.Available || p is not { Available: true }) throw new SocialConnectionException(503, "Intégration indisponible : configuration ou validation du fournisseur requise.");
        return p;
    }
    private SocialConnectionStatus Status(SocialConnection c) => c.ConnectionStatus == SocialConnectionStatus.Connected && c.ExpiresAtUtc <= clock.GetUtcNow()
        ? SocialConnectionStatus.Expired : c.ConnectionStatus;
    private SocialConnectionDto Dto(SocialConnection c) => new(c.Id, c.Platform, c.ExternalAccountId, c.DisplayName, c.AccountType, Status(c),
        c.ConnectedAtUtc, c.ExpiresAtUtc, c.LastValidatedAtUtc, c.PublishingPermissionGranted, c.Revision);
    public async Task<IReadOnlyList<SocialConnectionDto>> ListAsync(Guid workspaceId, CancellationToken ct)
    {
        await using var db = Db(workspaceId); await Access(db, workspaceId, WorkspacePermission.Read, ct);
        return (await db.SocialConnections.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct)).Select(Dto).ToArray();
    }
    public async Task<IReadOnlyList<SocialProviderDto>> ProvidersAsync(Guid workspaceId, CancellationToken ct)
    {
        await using var db = Db(workspaceId); await Access(db, workspaceId, WorkspacePermission.Read, ct);
        return Enum.GetValues<SocialPlatform>().Select(platform => {
            var available = secrets.Available && providers.Any(x => x.Platform == platform && x.Available);
            if (platform == SocialPlatform.LinkedIn && providers.OfType<LinkedInWorkspaceOAuthProvider>().SingleOrDefault() is { } linkedIn)
                return new SocialProviderDto(platform, available, !secrets.Available ? "Clé de chiffrement des connexions indisponible côté serveur." : linkedIn.ConfigurationMessage);
            return new SocialProviderDto(platform, available, available ? "Connexion disponible selon les autorisations accordées. Publication automatique désactivée." : "Configuration et autorisations du fournisseur requises.");
        }).ToArray();
    }
    public async Task<string> StartAsync(Guid workspaceId, SocialPlatform platform, CancellationToken ct)
    {
        await using var db = Db(workspaceId);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Access(db, workspaceId, WorkspacePermission.ManageSocial, ct);
        var provider = Provider(platform);
        // Opportunistically erase expired discovery tokens; no background worker is started.
        foreach (var old in await db.SocialOAuthSessions.Where(x => x.UserId == actor.UserId && x.ProtectedData != "").ToListAsync(ct))
            if (old.ExpiresAtUtc <= clock.GetUtcNow()) { old.ProtectedData = ""; old.Status = SocialOAuthStatus.Failed; old.Revision = Guid.NewGuid(); }
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = provider.SupportsPkce ? WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)) : null;
        var attempt = new SocialOAuthSession { WorkspaceId = workspaceId, UserId = actor.UserId, Platform = platform,
            StateHash = Hash(state), Status = SocialOAuthStatus.Started, ExpiresAtUtc = clock.GetUtcNow().AddMinutes(10) };
        attempt.ProtectedData = Seal(verifier, workspaceId, attempt.Id, "verifier");
        db.SocialOAuthSessions.Add(attempt); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return provider.AuthorizationUrl(state, verifier is null ? null : WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }
    private static string Hash(string state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
    public async Task<SocialOAuthResult> CallbackAsync(SocialPlatform platform, string state, string? code, bool denied, CancellationToken ct)
    {
        if (state.Length != 43 || code?.Length > 8192) throw Invalid();
        var hash = Hash(state);
        await using var lookup = new SocialFlowDbContext(options);
        // Bootstrap by unguessable state hash AND authenticated initiator, never by a browser workspace parameter.
        var address = await lookup.SocialOAuthSessions.IgnoreQueryFilters(["Workspace"])
            .Where(x => x.StateHash == hash && x.UserId == actor.UserId && x.Platform == platform)
            .Select(x => new { x.Id, x.WorkspaceId }).SingleOrDefaultAsync(ct) ?? throw Invalid();
        await using var db = Db(address.WorkspaceId);
        SocialOAuthSession attempt; string? verifier;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await Access(db, address.WorkspaceId, WorkspacePermission.ManageSocial, ct);
            attempt = await db.SocialOAuthSessions.SingleAsync(x => x.Id == address.Id, ct);
            if (attempt.Status != SocialOAuthStatus.Started || attempt.ExpiresAtUtc <= clock.GetUtcNow()) throw Invalid();
            verifier = Open<string?>(attempt.ProtectedData, address.WorkspaceId, address.Id, "verifier");
            attempt.Status = SocialOAuthStatus.Exchanging; attempt.ProtectedData = ""; attempt.Revision = Guid.NewGuid();
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        try
        {
            if (denied || string.IsNullOrWhiteSpace(code)) throw Invalid();
            var discovered = await Provider(platform).DiscoverAsync(code, verifier, ct);
            if (discovered.Count > 500 || discovered.Any(x => string.IsNullOrWhiteSpace(x.ExternalAccountId) || x.ExternalAccountId.Length > 256 ||
                string.IsNullOrWhiteSpace(x.Credential.AccessToken) || x.DisplayName.Length > 256 || x.AccountType.Length > 64 || x.Credential.ExpiresAtUtc <= clock.GetUtcNow())) throw Invalid();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await Access(db, address.WorkspaceId, WorkspacePermission.ManageSocial, ct);
            if (attempt.ExpiresAtUtc <= clock.GetUtcNow()) throw Invalid();
            attempt.ProtectedData = Seal(discovered.DistinctBy(x => x.ExternalAccountId).ToArray(), address.WorkspaceId, address.Id, "candidates");
            attempt.Status = SocialOAuthStatus.Selection; attempt.Revision = Guid.NewGuid();
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return new(address.Id, address.WorkspaceId);
        }
        catch
        {
            attempt.ProtectedData = ""; attempt.Status = SocialOAuthStatus.Failed; attempt.Revision = Guid.NewGuid();
            await db.SaveChangesAsync(CancellationToken.None);
            throw new SocialConnectionException(400, "Connexion refusée, expirée ou fournisseur indisponible. Aucun compte n’a été ajouté.");
        }
    }
    private async Task<SocialOAuthSession> Session(SocialFlowDbContext db, Guid id, CancellationToken ct) =>
        await db.SocialOAuthSessions.SingleOrDefaultAsync(x => x.Id == id && x.UserId == actor.UserId && x.Status == SocialOAuthStatus.Selection, ct) is { } session && session.ExpiresAtUtc > clock.GetUtcNow()
            ? session : throw Invalid();
    public async Task<IReadOnlyList<SocialCandidateDto>> CandidatesAsync(Guid workspaceId, Guid sessionId, CancellationToken ct)
    {
        await using var db = Db(workspaceId); await Access(db, workspaceId, WorkspacePermission.ManageSocial, ct);
        var session = await Session(db, sessionId, ct);
        return Open<SocialCandidate[]>(session.ProtectedData, workspaceId, sessionId, "candidates")
            .Select(x => new SocialCandidateDto(x.ExternalAccountId, x.DisplayName, x.AccountType, x.PublishingPermissionGranted)).ToArray();
    }
    public async Task SelectAsync(Guid workspaceId, Guid sessionId, IReadOnlyList<string> externalIds, CancellationToken ct)
    {
        if (externalIds.Count is < 1 or > 100 || externalIds.Distinct().Count() != externalIds.Count) throw Invalid();
        await using var db = Db(workspaceId); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Access(db, workspaceId, WorkspacePermission.ManageSocial, ct); var session = await Session(db, sessionId, ct);
        var choices = Open<SocialCandidate[]>(session.ProtectedData, workspaceId, sessionId, "candidates");
        foreach (var id in externalIds)
        {
            var choice = choices.SingleOrDefault(x => x.ExternalAccountId == id) ?? throw Invalid();
            if (choice.Credential.ExpiresAtUtc <= clock.GetUtcNow()) throw Invalid();
            var connection = await db.SocialConnections.SingleOrDefaultAsync(x => x.Platform == session.Platform && x.ExternalAccountId == id && x.ConnectionStatus != SocialConnectionStatus.Revoked, ct);
            if (connection is null) { connection = new() { WorkspaceId = workspaceId, Platform = session.Platform, ExternalAccountId = id }; db.SocialConnections.Add(connection); }
            connection.DisplayName = choice.DisplayName; connection.AccountType = choice.AccountType;
            connection.ConnectedByUserId = actor.UserId; connection.ConnectedAtUtc = clock.GetUtcNow(); connection.LastValidatedAtUtc = clock.GetUtcNow();
            connection.ExpiresAtUtc = choice.Credential.ExpiresAtUtc; connection.PublishingPermissionGranted = choice.PublishingPermissionGranted;
            connection.ConnectionStatus = SocialConnectionStatus.Connected; connection.Revision = Guid.NewGuid();
            connection.ProtectedCredential = Seal(choice.Credential, workspaceId, connection.Id, "credential");
        }
        session.ProtectedData = ""; session.Status = SocialOAuthStatus.Completed; session.Revision = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task<SocialConnection> Connection(SocialFlowDbContext db, Guid id, Guid revision, CancellationToken ct)
    {
        var c = await db.SocialConnections.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (c.Revision != revision) throw new SocialConnectionException(409, "La connexion a changé. Rechargez la liste.");
        return c;
    }
    public async Task DisconnectAsync(Guid workspaceId, Guid id, Guid revision, CancellationToken ct)
    {
        await using var db = Db(workspaceId); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Access(db, workspaceId, WorkspacePermission.ManageSocial, ct); var c = await Connection(db, id, revision, ct);
        c.ConnectionStatus = SocialConnectionStatus.Revoked; c.ProtectedCredential = ""; c.PublishingPermissionGranted = false; c.Revision = Guid.NewGuid();
        // Local revocation avoids revoking a provider grant shared with another workspace.
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task ValidateAsync(Guid workspaceId, Guid id, Guid revision, CancellationToken ct)
    {
        await using var db = Db(workspaceId); await Access(db, workspaceId, WorkspacePermission.ManageSocial, ct);
        var c = await Connection(db, id, revision, ct); if (c.ConnectionStatus == SocialConnectionStatus.Revoked) throw Missing();
        if (string.IsNullOrEmpty(c.ProtectedCredential)) throw new SocialConnectionException(409, "Reconnectez ce compte pour renouveler son autorisation.");
        var credential = Open<SocialCredential>(c.ProtectedCredential, workspaceId, id, "credential");
        SocialCredential? validated = null; var invalid = false;
        try { validated = await Provider(c.Platform).ValidateAsync(c.ExternalAccountId, c.AccountType, credential, ct); }
        catch (SocialConnectionException e) when (e.Status == 401) { invalid = true; }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Access(db, workspaceId, WorkspacePermission.ManageSocial, ct);
        if (invalid) { c.ConnectionStatus = SocialConnectionStatus.ReauthorizationRequired; c.ProtectedCredential = ""; c.PublishingPermissionGranted = false; }
        else {
            c.ProtectedCredential = Seal(validated!, workspaceId, id, "credential"); c.ExpiresAtUtc = validated!.ExpiresAtUtc;
            var permission = c.Platform == SocialPlatform.Facebook ? "pages_manage_posts" : c.Platform == SocialPlatform.Instagram ? "instagram_content_publish" : c.AccountType == "Organization" ? "w_organization_social" : "w_member_social";
            c.PublishingPermissionGranted &= validated.Scopes.Contains(permission);
            c.ConnectionStatus = c.ExpiresAtUtc <= clock.GetUtcNow() ? SocialConnectionStatus.Expired : SocialConnectionStatus.Connected;
        }
        c.LastValidatedAtUtc = clock.GetUtcNow(); c.Revision = Guid.NewGuid(); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<Guid>> DestinationsAsync(Guid workspaceId, Guid postId, CancellationToken ct)
    {
        await using var db = Db(workspaceId); await Access(db, workspaceId, WorkspacePermission.Read, ct);
        if (!await db.SocialPosts.AnyAsync(x => x.Id == postId, ct)) throw Missing();
        return await db.PostSocialDestinations.Where(x => x.SocialPostId == postId).Select(x => x.SocialConnectionId).ToListAsync(ct);
    }
    public async Task SetDestinationsAsync(Guid workspaceId, Guid postId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count > 100 || ids.Distinct().Count() != ids.Count) throw Invalid();
        await using var db = Db(workspaceId); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Access(db, workspaceId, WorkspacePermission.Edit, ct);
        var post = await db.SocialPosts.SingleOrDefaultAsync(x => x.Id == postId, ct) ?? throw Missing();
        if (post.Status is not (Redeemer.SocialFlow.Domain.Enums.SocialPostStatus.Draft or Redeemer.SocialFlow.Domain.Enums.SocialPostStatus.Rejected))
            throw new SocialConnectionException(409, "Les destinations ne peuvent être modifiées que sur un brouillon ou un contenu rejeté.");
        var connections = await db.SocialConnections.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (connections.Count != ids.Count || connections.Any(x => x.Platform != post.Platform || Status(x) != SocialConnectionStatus.Connected || !x.PublishingPermissionGranted)) throw Missing();
        db.PostSocialDestinations.RemoveRange(await db.PostSocialDestinations.Where(x => x.SocialPostId == postId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        db.PostSocialDestinations.AddRange(ids.Select(id => new PostSocialDestination { WorkspaceId = workspaceId, SocialPostId = postId, SocialConnectionId = id }));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
