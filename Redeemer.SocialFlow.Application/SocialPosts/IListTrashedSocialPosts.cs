namespace Redeemer.SocialFlow.Application.SocialPosts;

public interface IListTrashedSocialPosts
{
    Task<IReadOnlyList<TrashedSocialPostDto>> ExecuteAsync(CancellationToken cancellationToken = default);
}
