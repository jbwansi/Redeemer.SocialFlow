using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Infrastructure.Visuals;

public sealed record EditorialProfileInput(long Revision, string OrganizationName, string? Mission = null,
    string? Values = null, string? Positioning = null, string? Audiences = null, string? Tone = null,
    string? Themes = null, string? Rules = null, string? Avoid = null);

public sealed class WorkspaceIdentityService(SocialFlowDbContext db) : IWorkspaceEditorialProfileReader
{
    public async Task<EditorialIdentity?> ReadAsync(CancellationToken token)
    {
        var p = await db.WorkspaceEditorialProfiles.AsNoTracking().SingleOrDefaultAsync(token);
        return p is null ? null : new(p.WorkspaceId, p.Revision, p.OrganizationName, p.Mission, p.Values,
            p.Positioning, p.Audiences, p.Tone, p.Themes, p.Rules, p.Avoid);
    }
    public async Task<EditorialIdentity> SaveAsync(EditorialProfileInput input, CancellationToken token)
    {
        var p = await db.WorkspaceEditorialProfiles.SingleOrDefaultAsync(token);
        if ((p?.Revision ?? 0) != input.Revision) throw new DomainException("Editorial profile changed; reload it.");
        if (p is null) { p = WorkspaceEditorialProfile.Create(); db.Add(p); }
        p.Update(input.OrganizationName, input.Mission, input.Values, input.Positioning, input.Audiences,
            input.Tone, input.Themes, input.Rules, input.Avoid);
        await db.SaveChangesAsync(token);
        return (await ReadAsync(token))!;
    }
    public async Task<Guid?> ApprovalAsync(Guid id, long revision, CancellationToken token) =>
        await db.VisualIdentityApprovals.AsNoTracking().Where(x => x.IdentityId == id && x.Revision == revision)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(token);

    private static VisualIdentity Canonical(StoredVisualIdentity source, Guid? approval = null) =>
        new(source.IdentityId, source.Name, source.Style, JsonSerializer.Deserialize<string[]>(source.ColorsJson)!,
            source.Slogan, source.Instructions, source.Id, source.Revision, source.LogoId, approval);

    public async Task<Guid> ApproveAsync(Guid id, long revision, string actor, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new DomainException("An authenticated approver is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var source = await Source(id, revision, token);
        var existing = await ApprovalAsync(id, revision, token);
        if (existing.HasValue) return existing.Value;
        var approval = VisualIdentityApproval.Create(source, JsonSerializer.Serialize(Canonical(source).Snapshot()), actor);
        db.Add(approval);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return approval.Id;
    }
    private async Task<StoredVisualIdentity> Source(Guid id, long revision, CancellationToken token)
    {
        var source = await db.VisualIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, token)
            ?? throw new IdentityNotFoundException();
        if (!source.IsActive || source.Revision != revision) throw new DomainException("Identity inactive or revision obsolete.");
        return source;
    }
    public async Task<VisualIdentity> ResolveApprovedAsync(Guid id, long revision, CancellationToken token)
    {
        var source = await Source(id, revision, token);
        var approval = await ApprovalAsync(id, revision, token) ?? throw new DomainException("This identity revision is not approved.");
        return Canonical(source, approval);
    }
    public async Task<VisualIdentity> ResolveApprovedAsync(Guid id, long revision, VisualIdentity supplied, CancellationToken token)
    {
        var source = await Source(id, revision, token);
        var approval = await ApprovalAsync(id, revision, token) ?? throw new DomainException("This identity revision is not approved.");
        var canonical = Canonical(source, approval);
        // Browser fields are checked, but only server fields are sent to the provider.
        if (supplied.Id != canonical.Id || supplied.Name != canonical.Name || supplied.Style != canonical.Style ||
            supplied.Slogan != canonical.Slogan || supplied.Instructions != canonical.Instructions ||
            !supplied.Colors.SequenceEqual(canonical.Colors))
            throw new DomainException("Approved identity fields cannot be substituted.");
        return canonical;
    }
}
