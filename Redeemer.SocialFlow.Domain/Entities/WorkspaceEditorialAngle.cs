using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public sealed class WorkspaceEditorialAngle : IWorkspaceResource
{
    private WorkspaceEditorialAngle() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; private set; }
    public long Revision { get; private set; }
    public string Name { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Objective { get; private set; } = "";
    public string Instructions { get; private set; } = "";
    public string CallToAction { get; private set; } = "";
    public bool Active { get; private set; }
    public int DisplayOrder { get; private set; }
    public string UpdatedBy { get; private set; } = "";
    public DateTimeOffset UpdatedAt { get; private set; }
    public static WorkspaceEditorialAngle Create() => new();
    public void Update(string name, string description, string objective, string instructions,
        string callToAction, bool active, int displayOrder, string actor)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || string.IsNullOrWhiteSpace(actor) ||
            new[] { description, objective, instructions, callToAction }.Any(x => x is null || x.Length > 4000) ||
            displayOrder is < 0 or > 10000) throw new DomainException("Invalid editorial angle.");
        Name = name.Trim(); Description = description.Trim(); Objective = objective.Trim(); Instructions = instructions.Trim();
        CallToAction = callToAction.Trim(); Active = active; DisplayOrder = displayOrder;
        UpdatedBy = actor; UpdatedAt = DateTimeOffset.UtcNow; Revision++;
    }
}
