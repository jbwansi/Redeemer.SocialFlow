namespace Redeemer.SocialFlow.Domain.Entities;

public enum LinkedInAutomationState { Authorized = 1, Blocked = 2 }

// No row means no automatic authorization, regardless of creation/scheduling dates.
public sealed class LinkedInAutomation : IWorkspaceResource
{
    public Guid WorkspaceId { get; set; }
    public Guid SocialPostId { get; set; }
    public Guid SocialConnectionId { get; set; }
    public string AuthorizedByUserId { get; set; } = "";
    public DateTimeOffset AuthorizedAtUtc { get; set; }
    public DateTimeOffset ScheduledAtUtc { get; set; }
    public DateTimeOffset PostUpdatedAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public LinkedInAutomationState State { get; set; }
    public string? BlockReason { get; set; }
}
