using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Abstractions;

namespace Redeemer.SocialFlow.Application.SocialPosts;

public sealed class ListTrashedSocialPosts(ISocialFlowDbContext context) : IListTrashedSocialPosts
{
    public async Task<IReadOnlyList<TrashedSocialPostDto>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var posts = await context.SocialPosts.IgnoreQueryFilters().AsNoTracking()
            .Where(post => post.IsDeleted == true)
            .Select(post => new TrashedSocialPostDto(post.Id, post.Title, post.Platform, post.Status,
                post.CreatedAt, post.UpdatedAt, post.DeletedAt!.Value))
            .ToListAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // SQLite cannot order DateTimeOffset instants stored as TEXT correctly across offsets.
        return posts.OrderByDescending(post => post.DeletedAt).ThenBy(post => post.Id).ToArray();
    }
}
