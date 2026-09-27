namespace Redeemer.SocialFlow.Application.AI;

public interface IContentGenerator
{
	Task<ContentGenerationResult> GenerateAsync(
		GenerateContentRequest request,
		CancellationToken cancellationToken = default);
}
