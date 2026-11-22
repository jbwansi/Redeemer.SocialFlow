namespace Redeemer.SocialFlow.Application.AI;

public sealed record EditorialIdentity(Guid WorkspaceId, long Revision, string OrganizationName,
    string? Mission, string? Values, string? Positioning, string? Audiences, string? Tone,
    string? Themes, string? Rules, string? Avoid);

public interface IWorkspaceEditorialProfileReader
{
    Task<EditorialIdentity?> ReadAsync(CancellationToken token);
}
