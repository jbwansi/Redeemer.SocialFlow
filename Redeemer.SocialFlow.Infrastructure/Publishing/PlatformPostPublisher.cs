using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Domain.Enums;
namespace Redeemer.SocialFlow.Infrastructure.Publishing;
public sealed class PlatformPostPublisher(IPostPublisher? linkedIn, IPostPublisher? facebook) : IPostPublisher
{
    public Task<PostPublicationResult> PublishAsync(PostPublicationRequest request, CancellationToken cancellationToken = default) =>
        (request.Platform switch { SocialPlatform.LinkedIn => linkedIn, SocialPlatform.Facebook => facebook, _ => null })
            ?.PublishAsync(request, cancellationToken) ?? Task.FromResult(PostPublicationResult.Failed());
}
