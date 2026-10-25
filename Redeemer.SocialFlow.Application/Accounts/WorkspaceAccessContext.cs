namespace Redeemer.SocialFlow.Application.Accounts;

/// <summary>One immutable workspace binding per scope. Never populated directly from untrusted input.</summary>
public sealed class WorkspaceAccessContext : IWorkspaceAccessContext
{
    public Guid? WorkspaceId { get; private set; }
    public void Bind(Guid workspaceId)
    {
        if (workspaceId == Guid.Empty || (WorkspaceId.HasValue && WorkspaceId != workspaceId))
            throw new InvalidOperationException("A scope cannot switch workspaces.");
        WorkspaceId = workspaceId;
    }
}

/// <summary>For explicitly scoped maintenance and isolated tests; conveys no HTTP authorization.</summary>
public sealed class ExplicitWorkspaceContext(Guid workspaceId) : IWorkspaceAccessContext
{
    public Guid? WorkspaceId { get; } = workspaceId != Guid.Empty ? workspaceId : throw new ArgumentException("Workspace required.");
}
