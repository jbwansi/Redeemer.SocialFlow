using Redeemer.SocialFlow.Application.AI;

namespace Redeemer.SocialFlow.Application.Knowledge;

public interface IGenerateGroundedContent
{
    Task<GroundedContentResult> ExecuteAsync(GenerateContentRequest request, CancellationToken cancellationToken = default);
}
