using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Infrastructure.Visuals;

public sealed class SqliteVisualIdentityRepository(DbContextOptions<SocialFlowDbContext> options) : IVisualIdentityRepository
{
    private SocialFlowDbContext Db() => new(options);
    private static async Task<StoredVisualIdentity> Find(SocialFlowDbContext db, Guid id, CancellationToken token) =>
        await db.VisualIdentities.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, token) ?? throw new IdentityNotFoundException();
    private static void Revision(StoredVisualIdentity identity, long revision)
    { if (identity.Revision != revision) throw new DomainException("Identity changed; reload before editing."); }
    private static async Task Unique(SocialFlowDbContext db, StoredVisualIdentity identity, CancellationToken token)
    { if (await db.VisualIdentities.AnyAsync(x => x.Id != identity.Id && x.IdentityId == identity.IdentityId, token)) throw new DomainException("Identity identifier is already reserved."); }
    public async Task<IReadOnlyList<StoredVisualIdentity>> ListAsync(CancellationToken token)
    {
        await using var db = Db();
        return await db.VisualIdentities.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync(token);
    }
    public async Task<StoredVisualIdentity> GetAsync(Guid id, CancellationToken token)
    { await using var db = Db(); return await Find(db, id, token); }
    public async Task<StoredVisualIdentity> CreateAsync(StoredVisualIdentity identity, CancellationToken token)
    {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await Unique(db, identity, token);
        db.VisualIdentities.Add(identity);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token); return identity;
    }
    public async Task<StoredVisualIdentity> UpdateAsync(Guid id, long revision, IdentityInput input, CancellationToken token)
    {
        await using var db = Db(); await using var tx = await db.Database.BeginTransactionAsync(token);
        var identity = await Find(db, id, token); Revision(identity, revision);
        identity.Update(input.IdentityId, input.Name, input.Style, input.Colors, input.Slogan, input.Instructions, input.IsActive);
        await Unique(db, identity, token);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token); return identity;
    }
    public async Task<StoredVisualIdentity> DefaultAsync(Guid id, long revision, CancellationToken token)
    {
        await using var db = Db(); await using var tx = await db.Database.BeginTransactionAsync(token);
        var identity = await Find(db, id, token); Revision(identity, revision);
        if (!identity.IsActive) throw new DomainException("Default identity must be active.");
        foreach (var previous in await db.VisualIdentities.Where(x => x.IsDefault && x.Id != id).ToListAsync(token)) previous.SetDefault(false);
        // Clear the unique default before setting its replacement, within the same transaction.
        await db.SaveChangesAsync(token);
        identity.SetDefault(true);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token); return identity;
    }
    public async Task DeleteAsync(Guid id, long revision, CancellationToken token)
    {
        await using var db = Db(); await using var tx = await db.Database.BeginTransactionAsync(token);
        var identity = await Find(db, id, token); Revision(identity, revision);
        if (await db.PostVisuals.AnyAsync(x => x.SourceIdentityId == id, token))
            throw new DomainException("Identity is referenced by a visual; deactivate it instead.");
        identity.Delete(); await db.SaveChangesAsync(token); await tx.CommitAsync(token);
    }
    public async Task ReserveLogoAsync(IdentityLogo logo, long revision, CancellationToken token)
    {
        await using var db = Db(); await using var tx = await db.Database.BeginTransactionAsync(token);
        Revision(await Find(db, logo.IdentityId, token), revision);
        db.IdentityLogos.Add(logo); await db.SaveChangesAsync(token); await tx.CommitAsync(token);
    }
    public async Task<StoredVisualIdentity> CompleteLogoAsync(Guid id, Guid logoId, long revision, CancellationToken token)
    {
        await using var db = Db(); await using var tx = await db.Database.BeginTransactionAsync(token);
        var identity = await Find(db, id, token); Revision(identity, revision);
        var logo = await db.IdentityLogos.SingleAsync(x => x.Id == logoId && x.IdentityId == id, token);
        logo.Complete(); identity.SetLogo(logo.Id);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token); return identity;
    }
    public async Task<IdentityLogo> GetLogoAsync(Guid id, Guid logoId, CancellationToken token)
    {
        await using var db = Db();
        return await db.IdentityLogos.AsNoTracking().SingleOrDefaultAsync(x => x.IdentityId == id && x.Id == logoId && x.IsReady, token)
            ?? throw new IdentityNotFoundException();
    }
}
