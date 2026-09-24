using System.Linq.Expressions;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.SocialPosts;

public sealed record SocialPostDto(
    Guid Id, string Title, string Content, SocialPlatform Platform, SocialPostStatus Status,
    string? CallToAction, string? VisualBrief, string? VisualUrl,
    DateTimeOffset? ScheduledAt, DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    internal static readonly Expression<Func<SocialPost, SocialPostDto>> Projection = post => new(
        post.Id, post.Title, post.Content, post.Platform, post.Status,
        post.CallToAction, post.VisualBrief, post.VisualUrl,
        post.ScheduledAt, post.PublishedAt, post.CreatedAt, post.UpdatedAt);

    internal static readonly Func<SocialPost, SocialPostDto> FromEntity = Projection.Compile();
}
