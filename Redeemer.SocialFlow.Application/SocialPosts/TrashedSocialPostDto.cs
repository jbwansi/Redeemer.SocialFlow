using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.SocialPosts;

public sealed record TrashedSocialPostDto(
    Guid Id, string Title, SocialPlatform Platform, SocialPostStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset DeletedAt);
