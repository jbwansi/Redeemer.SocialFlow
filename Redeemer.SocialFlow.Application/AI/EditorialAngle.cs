namespace Redeemer.SocialFlow.Application.AI;

// Captured once before generation; never taken from browser-supplied instructions.
public sealed record EditorialAngle(Guid Id, Guid WorkspaceId, long Revision, string Name,
    string Description, string Objective, string Instructions, string CallToAction);
public interface IWorkspaceEditorialAngleReader
{
    Task<EditorialAngle> ResolveAsync(Guid id, CancellationToken token);
}
public sealed class EditorialAngleUnavailableException : Exception { }
