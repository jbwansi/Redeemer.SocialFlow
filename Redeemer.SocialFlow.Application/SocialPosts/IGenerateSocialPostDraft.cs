using Redeemer.SocialFlow.Application.AI;

namespace Redeemer.SocialFlow.Application.SocialPosts;

public interface IGenerateSocialPostDraft
{
    Task<GenerateSocialPostDraftResult> ExecuteAsync(GenerateContentRequest request, CancellationToken cancellationToken = default);
}
