namespace Redeemer.SocialFlow.Application.SocialPosts;

public sealed class PostNotFoundException(Guid postId)
    : Exception($"Social post '{postId}' was not found.")
{
    public Guid PostId { get; } = postId;
}
