using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Application.SocialPosts;

public sealed class GenerateSocialPostDraft(IContentGenerator generator, ISocialFlowDbContext context)
    : IGenerateSocialPostDraft
{
    public async Task<GenerateSocialPostDraftResult> ExecuteAsync(
        GenerateContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var generated = await generator.GenerateAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var post = SocialPost.Create(generated.Title, generated.Content, request.Platform);
        post.Update(generated.Title, generated.Content, generated.CallToAction, generated.VisualBrief);
        context.SocialPosts.Add(post);
        await context.SaveChangesAsync(cancellationToken);
        return new GenerateSocialPostDraftResult(SocialPostDto.FromEntity(post), generated.Warnings);
    }
}
