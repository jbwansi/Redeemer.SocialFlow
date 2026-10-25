using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Infrastructure.Accounts;

public sealed class WorkspaceCollaboration(DbContextOptions<SocialFlowDbContext> options, ICollaborationActor actor,
    UserManager<ApplicationUser> users, IAccountEmailSender mail, IConfiguration config, TimeProvider clock) : IWorkspaceCollaboration
{
    private static CollaborationException Denied() => new(403, "Permission insuffisante ou espace inaccessible.");
    private static CollaborationException Conflict() => new(409, "Les données ont changé. Rechargez l'équipe.");
    private SocialFlowDbContext Db(Guid id) => new(options, new ExplicitWorkspaceContext(id));
    private async Task<WorkspaceMembership> Access(SocialFlowDbContext db, Guid id, WorkspacePermission permission, CancellationToken ct)
    {
        var space = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == id, ct);
        var member = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == actor.UserId, ct);
        if (space is not { Status: WorkspaceStatus.Active, Type: WorkspaceType.Organization } ||
            member is not { Status: MembershipStatus.Active } || !WorkspacePermissions.Allows(member.Role, permission)) throw Denied();
        return member;
    }
    private static void Version(Guid actual, Guid expected) { if (actual != expected) throw Conflict(); }
    private async Task<WorkspaceMembership> Member(SocialFlowDbContext db, Guid id, string userId, CancellationToken ct) =>
        await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == userId && x.Status == MembershipStatus.Active, ct)
        ?? throw new CollaborationException(404, "Membre introuvable.");
    public async Task<WorkspaceTeam> GetAsync(Guid workspaceId, CancellationToken token)
    {
        await using var db = Db(workspaceId);
        var me = await Access(db, workspaceId, WorkspacePermission.Read, token);
        var members = await db.WorkspaceMemberships.Where(x => x.WorkspaceId == workspaceId && x.Status == MembershipStatus.Active).ToListAsync(token);
        var ids = members.Select(x => x.UserId).ToArray();
        var emails = await users.Users.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Email ?? "Compte indisponible", token);
        var invitations = WorkspacePermissions.Allows(me.Role, WorkspacePermission.ManageTeam)
            ? (await db.WorkspaceInvitations.Where(x => x.Status == InvitationStatus.Pending).ToListAsync(token))
                .Where(x => x.ExpiresAtUtc > clock.GetUtcNow()).Select(x => new TeamInvitation(x.Id, x.Email, x.Role, x.ExpiresAtUtc, x.Revision)).ToArray()
            : [];
        return new(members.Select(x => new TeamMember(x.UserId, emails.GetValueOrDefault(x.UserId, "Compte indisponible"), x.Role, x.Revision)).ToArray(), invitations, me.Role);
    }
    public async Task InviteAsync(Guid workspaceId, string email, WorkspaceRole role, CancellationToken token)
    {
        email = email.Trim();
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || !Enum.IsDefined(role))
            throw new CollaborationException(400, "Adresse ou rôle invalide.");
        var normalized = users.NormalizeEmail(email)!;
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await using var db = Db(workspaceId);
        WorkspaceInvitation invitation;
        await using (var tx = await db.Database.BeginTransactionAsync(token))
        {
            var me = await Access(db, workspaceId, WorkspacePermission.ManageTeam, token);
            if (!WorkspacePermissions.CanManage(me.Role, role, role)) throw Denied();
            var recipient = await users.FindByEmailAsync(email);
            if (recipient is not null && await db.WorkspaceMemberships.AnyAsync(x => x.WorkspaceId == workspaceId && x.UserId == recipient.Id && x.Status == MembershipStatus.Active, token))
                throw new CollaborationException(409, "Ce destinataire est déjà membre.");
            var pending = await db.WorkspaceInvitations.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized && x.Status == InvitationStatus.Pending, token);
            if (pending is not null)
            {
                if (pending.ExpiresAtUtc > clock.GetUtcNow()) throw new CollaborationException(409, "Une invitation est déjà en attente.");
                pending.Status = InvitationStatus.Expired; pending.Revision = Guid.NewGuid();
                await db.SaveChangesAsync(token);
            }
            invitation = new() { WorkspaceId = workspaceId, Email = email, NormalizedEmail = normalized, Role = role,
                TokenHash = Hash(code), CreatedAtUtc = clock.GetUtcNow(), ExpiresAtUtc = clock.GetUtcNow().AddHours(1), InvitedByUserId = actor.UserId };
            db.WorkspaceInvitations.Add(invitation); await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        }
        // Fragment stays out of HTTP request URLs and access logs. Never return the token in API responses.
        var link = config["Authentication:PublicOrigin"]!.TrimEnd('/') + "/#accept-invitation?code=" + code;
        try { await mail.SendAsync(email, "workspace-invitation", link, token); }
        catch (Exception)
        {
            // Do not propagate SMTP exceptions: they may contain a personal link or address.
            await using var retry = Db(workspaceId);
            var failed = await retry.WorkspaceInvitations.SingleAsync(x => x.Id == invitation.Id, CancellationToken.None);
            if (failed.Status == InvitationStatus.Pending)
            {
                failed.Status = InvitationStatus.Revoked; failed.Revision = Guid.NewGuid();
                try { await retry.SaveChangesAsync(CancellationToken.None); } catch (DbUpdateConcurrencyException) { }
            }
            throw new CollaborationException(503, "Envoi impossible. Rechargez les invitations puis réessayez.");
        }
    }
    public async Task ChangeAsync(Guid workspaceId, string userId, WorkspaceRole role, Guid revision, CancellationToken token)
    {
        await using var db = Db(workspaceId);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var me = await Access(db, workspaceId, WorkspacePermission.ManageTeam, token);
        var target = await Member(db, workspaceId, userId, token);
        if (!WorkspacePermissions.CanManage(me.Role, target.Role, role)) throw Denied();
        Version(target.Revision, revision);
        var owners = await db.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == workspaceId && x.Role == WorkspaceRole.Owner && x.Status == MembershipStatus.Active, token);
        target.Change(role, MembershipStatus.Active, owners);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
    }
    public Task RemoveAsync(Guid workspaceId, string userId, Guid revision, CancellationToken token) => Remove(workspaceId, userId, revision, false, token);
    public Task LeaveAsync(Guid workspaceId, Guid revision, CancellationToken token) => Remove(workspaceId, actor.UserId, revision, true, token);
    private async Task Remove(Guid id, string userId, Guid revision, bool leaving, CancellationToken ct)
    {
        await using var db = Db(id);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var me = await Access(db, id, leaving ? WorkspacePermission.Read : WorkspacePermission.ManageTeam, ct);
        var target = await Member(db, id, userId, ct);
        if (!leaving && !WorkspacePermissions.CanManage(me.Role, target.Role, target.Role)) throw Denied();
        Version(target.Revision, revision);
        var owners = await db.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == id && x.Role == WorkspaceRole.Owner && x.Status == MembershipStatus.Active, ct);
        target.Change(target.Role, MembershipStatus.Inactive, owners);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task RevokeAsync(Guid workspaceId, Guid invitationId, Guid revision, CancellationToken token)
    {
        await using var db = Db(workspaceId);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var me = await Access(db, workspaceId, WorkspacePermission.ManageTeam, token);
        var invitation = await db.WorkspaceInvitations.SingleOrDefaultAsync(x => x.Id == invitationId, token)
            ?? throw new CollaborationException(404, "Invitation introuvable.");
        if (!WorkspacePermissions.CanManage(me.Role, invitation.Role, invitation.Role)) throw Denied();
        Version(invitation.Revision, revision);
        if (invitation.Status != InvitationStatus.Pending) throw Conflict();
        invitation.Status = InvitationStatus.Revoked; invitation.Revision = Guid.NewGuid();
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
    }
    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
    public async Task<Guid> AcceptAsync(string code, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 43) throw new CollaborationException(400, "Invitation invalide ou expirée.");
        var user = await users.FindByIdAsync(actor.UserId);
        if (user is not { EmailConfirmed: true } || user.NormalizedEmail != users.NormalizeEmail(actor.Email)) throw Denied();
        // Narrow bootstrap lookup by a 256-bit secret hash, before membership exists. Never enumerate invitations.
        await using var lookup = new SocialFlowDbContext(options);
        var hash = Hash(code);
        var id = await lookup.WorkspaceInvitations.IgnoreQueryFilters(["Workspace"]).Where(x => x.TokenHash == hash)
            .Select(x => (Guid?)x.WorkspaceId).SingleOrDefaultAsync(token);
        if (id is null) throw new CollaborationException(400, "Invitation invalide ou expirée.");
        await using var db = Db(id.Value);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var invitation = await db.WorkspaceInvitations.SingleAsync(x => x.TokenHash == hash, token);
        var space = await db.Workspaces.SingleAsync(x => x.Id == id, token);
        if (space is not { Type: WorkspaceType.Organization, Status: WorkspaceStatus.Active } ||
            invitation.Status != InvitationStatus.Pending || invitation.ExpiresAtUtc <= clock.GetUtcNow() || invitation.NormalizedEmail != user.NormalizedEmail)
            throw new CollaborationException(400, "Invitation invalide, expirée ou destinée à une autre adresse.");
        // Recheck the issuer's current authority; a removed/demoted issuer cannot leave a privilege grant behind.
        var issuer = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == invitation.InvitedByUserId, token);
        if (issuer is not { Status: MembershipStatus.Active } || !WorkspacePermissions.CanManage(issuer.Role, invitation.Role, invitation.Role)) throw Denied();
        var existing = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == user.Id, token);
        if (existing is { Status: MembershipStatus.Active }) throw new CollaborationException(409, "Vous êtes déjà membre.");
        if (existing is null) db.WorkspaceMemberships.Add(WorkspaceMembership.Join(id.Value, user.Id, invitation.Role));
        else existing.Change(invitation.Role, MembershipStatus.Active, 0);
        invitation.Status = InvitationStatus.Accepted; invitation.Revision = Guid.NewGuid();
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return id.Value;
    }
}
