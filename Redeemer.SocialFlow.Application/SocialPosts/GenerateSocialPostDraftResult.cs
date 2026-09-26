namespace Redeemer.SocialFlow.Application.SocialPosts;

public sealed record GenerateSocialPostDraftResult(
    SocialPostDto Post,
    IReadOnlyList<string> Warnings);
