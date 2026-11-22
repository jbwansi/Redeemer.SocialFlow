using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Infrastructure.Editorial;

public sealed record AngleInput(long Revision, string Name, string Description, string Objective,
    string Instructions, string CallToAction, bool Active, int DisplayOrder);
public sealed record AngleDto(Guid Id, Guid WorkspaceId, long Revision, string Name, string Description,
    string Objective, string Instructions, string CallToAction, bool Active, int DisplayOrder);
public sealed class WorkspaceEditorialAngleService(SocialFlowDbContext db, IConfiguration configuration) : IWorkspaceEditorialAngleReader
{
    private static AngleDto Dto(WorkspaceEditorialAngle x) => new(x.Id, x.WorkspaceId, x.Revision, x.Name,
        x.Description, x.Objective, x.Instructions, x.CallToAction, x.Active, x.DisplayOrder);
    public async Task<IReadOnlyList<AngleDto>> ListAsync(bool activeOnly, CancellationToken token) =>
        (await db.WorkspaceEditorialAngles.AsNoTracking().Where(x => !activeOnly || x.Active)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(token)).Select(Dto).ToArray();
    public async Task<AngleDto> SaveAsync(Guid? id, AngleInput input, string actor, CancellationToken token)
    {
        var angle = id is null ? WorkspaceEditorialAngle.Create() :
            await db.WorkspaceEditorialAngles.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new EditorialAngleUnavailableException();
        if (angle.Revision != input.Revision) throw new DomainException("Angle revision changed.");
        angle.Update(input.Name, input.Description, input.Objective, input.Instructions, input.CallToAction, input.Active, input.DisplayOrder, actor);
        if (id is null) db.Add(angle);
        await db.SaveChangesAsync(token); return Dto(angle);
    }
    // Explicit initialization of an empty workspace; never an implicit/global fallback.
    public async Task InitializeAsync(string actor, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        if (await db.WorkspaceEditorialAngles.AnyAsync(token)) throw new DomainException("Workspace already has angles.");
        var templates = configuration.GetSection("EditorialAngles:InitialCatalog").GetChildren().ToArray();
        if (templates.Length is 0 or > 50) throw new DomainException("Initial catalog unavailable.");
        for (var i = 0; i < templates.Length; i++)
        {
            var item = templates[i]; var angle = WorkspaceEditorialAngle.Create();
            angle.Update(item["Name"]!, item["Description"] ?? "", item["Objective"] ?? "", item["Instructions"] ?? "",
                item["CallToAction"] ?? "", true, i, actor);
            db.Add(angle);
        }
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
    }
    public async Task<EditorialAngle> ResolveAsync(Guid id, CancellationToken token)
    {
        var x = await db.WorkspaceEditorialAngles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Active, token)
            ?? throw new EditorialAngleUnavailableException();
        return new(x.Id, x.WorkspaceId, x.Revision, x.Name, x.Description, x.Objective, x.Instructions, x.CallToAction);
    }
}
