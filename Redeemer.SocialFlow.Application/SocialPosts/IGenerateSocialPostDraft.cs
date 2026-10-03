using Redeemer.SocialFlow.Application.AI;

namespace Redeemer.SocialFlow.Application.SocialPosts;

public interface IGenerateSocialPostDraft
{
    Task<GenerateSocialPostDraftResult> ExecuteAsync(GenerateContentRequest request, CancellationToken cancellationToken = default);
    Task<GenerateSocialPostDraftExecutionResult> ExecuteAsync(GenerateContentRequest request,
        SocialPostGenerationMode mode, CancellationToken cancellationToken = default);
}
