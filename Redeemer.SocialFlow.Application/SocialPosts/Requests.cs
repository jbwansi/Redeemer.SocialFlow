using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.SocialPosts;

public sealed record CreatePostRequest(string Title, string? Content, SocialPlatform Platform);

public sealed record UpdatePostRequest(
    string Title, string? Content, string? CallToAction = null,
    string? VisualBrief = null, string? VisualUrl = null);

public sealed record SchedulePostRequest(DateTimeOffset ScheduledAt);

/// <summary>Optional filters combined with AND. CreatedAt bounds are inclusive instants.</summary>
public sealed record ListPostsRequest(
    SocialPlatform? Platform = null, SocialPostStatus? Status = null,
    DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null);
