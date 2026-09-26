namespace Redeemer.SocialFlow.Application.AI;

public interface IContentGenerator
{
	Task<GeneratedContent> GenerateAsync(
		GenerateContentRequest request,
		CancellationToken cancellationToken = default);
}