using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public sealed class WorkspaceEditorialProfile : IWorkspaceResource
{
    private WorkspaceEditorialProfile() { }
    public Guid WorkspaceId { get; private set; }
    public long Revision { get; private set; }
    public string OrganizationName { get; private set; } = "";
    public string? Mission { get; private set; }
    public string? Values { get; private set; }
    public string? Positioning { get; private set; }
    public string? Audiences { get; private set; }
    public string? Tone { get; private set; }
    public string? Themes { get; private set; }
    public string? Rules { get; private set; }
    public string? Avoid { get; private set; }
    public static WorkspaceEditorialProfile Create() => new();
    public void Update(string name, string? mission, string? values, string? positioning,
        string? audiences, string? tone, string? themes, string? rules, string? avoid)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new DomainException("Invalid organization name.");
        if (new[] { mission, values, positioning, audiences, tone, themes, rules, avoid }.Any(x => x?.Length > 4000))
            throw new DomainException("Editorial field exceeds 4000 characters.");
        OrganizationName = name.Trim(); Mission = mission?.Trim(); Values = values?.Trim(); Positioning = positioning?.Trim();
        Audiences = audiences?.Trim(); Tone = tone?.Trim(); Themes = themes?.Trim(); Rules = rules?.Trim(); Avoid = avoid?.Trim();
        Revision++;
    }
}

// Append-only evidence for one exact revision. Later edits never change this snapshot.
public sealed class VisualIdentityApproval : IWorkspaceResource
{
    private VisualIdentityApproval() { }
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid IdentityId { get; private set; }
    public long Revision { get; private set; }
    public string SnapshotJson { get; private set; } = "";
    public string ApprovedBy { get; private set; } = "";
    public DateTimeOffset ApprovedAt { get; private set; }
    public static VisualIdentityApproval Create(StoredVisualIdentity identity, string snapshot, string actor) =>
        new() { Id = Guid.NewGuid(), WorkspaceId = identity.WorkspaceId, IdentityId = identity.Id,
            Revision = identity.Revision, SnapshotJson = snapshot, ApprovedBy = actor, ApprovedAt = DateTimeOffset.UtcNow };
}
