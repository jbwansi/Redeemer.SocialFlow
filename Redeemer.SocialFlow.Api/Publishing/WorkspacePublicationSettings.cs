using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Redeemer.SocialFlow.Api.Publishing;

public sealed record WorkspacePublicationSettings(
    Guid? FacebookConnectionId,
    Guid? LinkedInConnectionId,
    [Required, StringLength(100)] string TimeZone,
    [Required] string[] SuggestedTimes,
    [Required] bool? FacebookAutomaticEnabled,
    bool LinkedInAutomaticEnabled = false)
{
    public static WorkspacePublicationSettings Read(string? json) => json is null
        ? new(null, null, "Etc/UTC", [], true)
        : JsonSerializer.Deserialize<WorkspacePublicationSettings>(json)!;
}
