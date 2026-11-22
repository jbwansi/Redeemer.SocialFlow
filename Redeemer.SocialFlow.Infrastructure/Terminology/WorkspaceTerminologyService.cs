using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Terminology;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Infrastructure.Terminology;

public sealed record TermInput(long Revision, string Canonical, string[] Aliases, TermCategory Category,
    string? Language = null, string? UsageContext = null, string? Note = null, string? Source = null);
public sealed record TermDto(Guid Id, Guid WorkspaceId, long Revision, string Canonical, string[] Aliases,
    TermCategory Category, string Language, string UsageContext, string? Note, string? Source,
    TermStatus Status, DateTimeOffset? VerifiedAt, DateTimeOffset UpdatedAt);
public sealed class TermNotFoundException : Exception { }

public sealed class WorkspaceTerminologyService(SocialFlowDbContext db) : ITerminologyReader
{
    private static TermDto Dto(WorkspaceTerm t) => new(t.Id, t.WorkspaceId, t.Revision, t.Canonical,
        JsonSerializer.Deserialize<string[]>(t.AliasesJson)!, t.Category, t.Language, t.UsageContext,
        t.Note, t.Source, t.Status, t.VerifiedAt, t.UpdatedAt);
    public async Task<IReadOnlyList<TermDto>> ListAsync(CancellationToken token) =>
        (await db.WorkspaceTerms.AsNoTracking().OrderBy(x => x.Canonical).ToListAsync(token)).Select(Dto).ToArray();

    public async Task<TermDto> SaveAsync(Guid? id, TermInput input, string actor, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var term = id is null ? WorkspaceTerm.Create(actor) : await Find(id.Value, token);
        if (term.Revision != input.Revision) throw new DomainException("Term revision changed; reload it.");
        term.Update(input.Canonical, input.Aliases, input.Category, input.Language, input.UsageContext, input.Note, input.Source, actor);
        if (await db.WorkspaceTerms.AnyAsync(x => x.Id != term.Id && x.SearchKey == term.SearchKey &&
            x.Language == term.Language && x.UsageContext == term.UsageContext, token))
            throw new DomainException("This canonical term already exists in this language/context.");
        if (id is null) db.Add(term);
        await Record(term, token); await tx.CommitAsync(token); return Dto(term);
    }
    public async Task<TermDto> ChangeStatusAsync(Guid id, long revision, bool validate, string actor, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var term = await Find(id, token);
        if (term.Revision != revision) throw new DomainException("Term revision changed; reload it.");
        if (validate)
        {
            if (await db.WorkspaceTerms.CountAsync(x => x.Status == TermStatus.Validated, token) >= 100)
                throw new DomainException("Maximum 100 validated terms per workspace; retire unused terms first.");
            term.Validate(actor);
        }
        else term.Retire(actor);
        await Record(term, token); await tx.CommitAsync(token); return Dto(term);
    }
    private async Task Record(WorkspaceTerm term, CancellationToken token)
    {
        await db.SaveChangesAsync(token);
        db.Add(WorkspaceTermRevision.Capture(term));
        await db.SaveChangesAsync(token);
    }
    private async Task<WorkspaceTerm> Find(Guid id, CancellationToken token) =>
        await db.WorkspaceTerms.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new TermNotFoundException();

    public async Task<TerminologyCatalog> ReadAsync(CancellationToken token)
    {
        var workspaceId = db.ActiveWorkspaceId ?? throw new InvalidOperationException("An active workspace is required.");
        var validated = await db.WorkspaceTerms.AsNoTracking().Where(x => x.Status == TermStatus.Validated).ToListAsync(token);
        if (validated.Count > 100) throw new DomainException("Terminology limit exceeded.");
        var rules = validated.OrderBy(x => x.Id).Select(t => new TermRule(t.Id, t.Revision, t.Canonical,
            JsonSerializer.Deserialize<string[]>(t.AliasesJson)!, t.Language, t.UsageContext, t.Note, t.Source!)).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { workspaceId, rules }))));
        return new(workspaceId, fingerprint, rules);
    }
}
