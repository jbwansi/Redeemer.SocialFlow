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
        var generation = await generator.GenerateAsync(request, cancellationToken);
        var generated = generation.Content;
        cancellationToken.ThrowIfCancellationRequested();

        var post = SocialPost.Create(generated.Title, generated.Content, request.Platform);
        post.Update(generated.Title, generated.Content, generated.CallToAction, generated.VisualBrief);
        var audit = AiGeneration.Create(post.Id, request.Subject, request.Objective, request.Audience,
            request.Platform, generation.Metadata.Provider, generation.Metadata.Model, generated.Warnings);
        context.SocialPosts.Add(post);
        context.AiGenerations.Add(audit);
        await context.SaveChangesAsync(cancellationToken);
        return new GenerateSocialPostDraftResult(SocialPostDto.FromEntity(post), generated.Warnings);
    }
}
