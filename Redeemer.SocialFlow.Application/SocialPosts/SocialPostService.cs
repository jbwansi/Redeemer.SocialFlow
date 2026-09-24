using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Application.SocialPosts;

public sealed class SocialPostService(ISocialFlowDbContext context) : ISocialPostService
{
    public async Task<SocialPostDto> CreateAsync(CreatePostRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var post = SocialPost.Create(request.Title, request.Content, request.Platform);
        context.SocialPosts.Add(post);
        await context.SaveChangesAsync(cancellationToken);
        return SocialPostDto.FromEntity(post);
    }

    public Task<SocialPostDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return context.SocialPosts.AsNoTracking().Where(post => post.Id == id)
            .Select(SocialPostDto.Projection).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SocialPostDto>> ListAsync(
        ListPostsRequest? request = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request ??= new ListPostsRequest();
        if (request.CreatedFrom > request.CreatedTo)
            throw new ArgumentException("CreatedFrom must not be after CreatedTo.", nameof(request));

        var query = context.SocialPosts.AsNoTracking();
        if (request.Platform is { } platform)
            query = query.Where(post => post.Platform == platform);
        if (request.Status is { } status)
            query = query.Where(post => post.Status == status);

        // SQLite cannot compare/order DateTimeOffset instants stored as TEXT.
        // Stream DTOs after SQL filters and compare instants without losing offset or tick precision.
        var posts = new List<SocialPostDto>();
        await foreach (var post in query.Select(SocialPostDto.Projection).AsAsyncEnumerable()
                           .WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.CreatedFrom is { } from && post.CreatedAt < from) continue;
            if (request.CreatedTo is { } to && post.CreatedAt > to) continue;
            posts.Add(post);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return posts.OrderByDescending(post => post.CreatedAt).ThenBy(post => post.Id).ToArray();
    }

    public Task<SocialPostDto> UpdateAsync(Guid id, UpdatePostRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(id, post => post.Update(request.Title, request.Content, request.CallToAction,
            request.VisualBrief, request.VisualUrl), cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var post = await LoadAsync(id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        post.EnsureCanDelete();
        context.SocialPosts.Remove(post);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<SocialPostDto> SubmitForReviewAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(id, post => post.SubmitForReview(), cancellationToken);

    public Task<SocialPostDto> ApproveAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(id, post => post.Approve(), cancellationToken);

    public Task<SocialPostDto> RejectAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(id, post => post.Reject(), cancellationToken);

    public Task<SocialPostDto> ScheduleAsync(Guid id, SchedulePostRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(id, post => post.Schedule(request.ScheduledAt), cancellationToken);

    public Task<SocialPostDto> CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(id, post => post.Cancel(), cancellationToken);

    private async Task<SocialPostDto> MutateAsync(Guid id, Action<SocialPost> mutate, CancellationToken cancellationToken)
    {
        var post = await LoadAsync(id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        mutate(post);
        await context.SaveChangesAsync(cancellationToken);
        return SocialPostDto.FromEntity(post);
    }

    private async Task<SocialPost> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await context.SocialPosts.SingleOrDefaultAsync(post => post.Id == id, cancellationToken)
            ?? throw new PostNotFoundException(id);
    }
}
