namespace Redeemer.SocialFlow.Application.AI;

public interface IVisualGenerator
{
    Task<GeneratedVisual> GenerateAsync(GenerateVisualRequest request, CancellationToken cancellationToken = default);
}
