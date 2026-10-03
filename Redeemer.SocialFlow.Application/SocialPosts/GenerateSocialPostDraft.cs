using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Knowledge;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Application.SocialPosts;

public sealed class GenerateSocialPostDraft(IContentGenerator generator, ISocialFlowDbContext context,
    IGenerateGroundedContent groundedGenerator)
    : IGenerateSocialPostDraft
{
    public async Task<GenerateSocialPostDraftResult> ExecuteAsync(
        GenerateContentRequest request, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(request, SocialPostGenerationMode.Editorial, cancellationToken)).Draft!;

    public async Task<GenerateSocialPostDraftExecutionResult> ExecuteAsync(GenerateContentRequest request,
        SocialPostGenerationMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Invalid generation mode.", nameof(mode));
        ContentGenerationResult generation;
        IReadOnlyList<KnowledgePassage> references = Array.Empty<KnowledgePassage>();
        if (mode == SocialPostGenerationMode.Grounded)
        {
            var grounded = await groundedGenerator.ExecuteAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (grounded.Outcome == GroundedContentOutcome.NoRelevantPassages)
                return new(SocialPostDraftOutcome.NoRelevantPassages, null, null, references);
            if (grounded.Outcome == GroundedContentOutcome.ContextLimitExceeded)
                return new(SocialPostDraftOutcome.ContextLimitExceeded, null, null, references);
            if (grounded.Outcome != GroundedContentOutcome.Generated || grounded.Generation is null || grounded.ReferencePassages.Count == 0)
                throw new InvalidOperationException("Invalid grounded generation result.");
            generation = grounded.Generation;
            references = grounded.ReferencePassages;
        }
        else
        {
            generation = await generator.GenerateAsync(request, cancellationToken);
        }
        var generated = generation.Content;
        cancellationToken.ThrowIfCancellationRequested();

        var post = SocialPost.Create(generated.Title, generated.Content, request.Platform);
        post.Update(generated.Title, generated.Content, generated.CallToAction, generated.VisualBrief);
        var audit = AiGeneration.Create(post.Id, request.Subject, request.Objective, request.Audience,
            request.Platform, generation.Metadata.Provider, generation.Metadata.Model, generated.Warnings);
        context.SocialPosts.Add(post);
        context.AiGenerations.Add(audit);
        await context.SaveChangesAsync(cancellationToken);
        return new(SocialPostDraftOutcome.Created,
            new GenerateSocialPostDraftResult(SocialPostDto.FromEntity(post), generated.Warnings), generation.Metadata, references);
    }
}
