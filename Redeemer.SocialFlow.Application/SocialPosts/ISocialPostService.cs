namespace Redeemer.SocialFlow.Application.SocialPosts;

public interface ISocialPostService
{
    Task<SocialPostDto> CreateAsync(CreatePostRequest request, CancellationToken cancellationToken = default);
    Task<SocialPostDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SocialPostDto>> ListAsync(ListPostsRequest? request = null, CancellationToken cancellationToken = default);
    Task<SocialPostDto> UpdateAsync(Guid id, UpdatePostRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SocialPostDto> SubmitForReviewAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SocialPostDto> ApproveAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SocialPostDto> RejectAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SocialPostDto> ScheduleAsync(Guid id, SchedulePostRequest request, CancellationToken cancellationToken = default);
    Task<SocialPostDto> CancelAsync(Guid id, CancellationToken cancellationToken = default);
}
