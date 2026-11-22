using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Knowledge;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Application.Terminology;
using System.Text.Json;

namespace Redeemer.SocialFlow.Application.SocialPosts;

public sealed class GenerateSocialPostDraft(IContentGenerator generator, ISocialFlowDbContext context,
    IGenerateGroundedContent groundedGenerator, DraftGenerationProgress? progress = null,
    IWorkspaceEditorialProfileReader? editorialProfiles = null, ITerminologyReader? terminology = null)
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
        // Always resolve tenant identity on the server, never accept a caller's identity.
        request = request with { EditorialIdentity = editorialProfiles is null ? null : await editorialProfiles.ReadAsync(cancellationToken) };
        var catalog = terminology is null ? null : await terminology.ReadAsync(cancellationToken);
        request = request with { TerminologyCatalog = catalog, Terms = catalog is null ? Array.Empty<TermRule>() :
            TerminologyValidator.Applicable(catalog.Terms, request.Subject, request.Objective, request.Audience, request.Context) };
        IReadOnlyList<KnowledgePassage> references = Array.Empty<KnowledgePassage>();
        if (mode == SocialPostGenerationMode.Grounded)
        {
            if (progress is not null) progress.Stage = DraftGenerationStage.KnowledgeSearch;
            var grounded = await groundedGenerator.ExecuteAsync(request, cancellationToken);
            if (progress is not null) progress.Stage = DraftGenerationStage.ResponseValidation;
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
            if (progress is not null) progress.Stage = DraftGenerationStage.ProviderCall;
            generation = await generator.GenerateAsync(request, cancellationToken);
        }
        if (progress is not null) progress.Stage = DraftGenerationStage.ResponseValidation;
        var generated = generation.Content;
        if (catalog is not null && catalog.Terms.Count > 0)
        {
            var checks = new[] { generated.Title, generated.Content, generated.CallToAction, generated.VisualBrief }
                .Select(t => TerminologyValidator.Check(t, catalog.Terms, correct: true)).ToArray();
            var inputChecks = new[] { request.Subject, request.Objective, request.Audience, request.Context }
                .Concat(references.Select(x => x.Content)).Select(t => TerminologyValidator.Check(t, catalog.Terms)).ToArray();
            var warnings = inputChecks.Concat(checks).SelectMany(x => x.Warnings).Distinct().ToArray();
            generated = generated with { Title = checks[0].Text, Content = checks[1].Text,
                CallToAction = generated.CallToAction is null ? null : checks[2].Text,
                VisualBrief = generated.VisualBrief is null ? null : checks[3].Text,
                Warnings = generated.Warnings.Concat(warnings).ToArray() };
            generation = generation with { Metadata = generation.Metadata with { Terminology = new(catalog.WorkspaceId,
                catalog.Fingerprint, checks.Concat(inputChecks).SelectMany(x => x.Terms).DistinctBy(x => x.Id).OrderBy(x => x.Id).ToArray(), warnings) } };
        }
        if (editorialProfiles is not null && request.EditorialIdentity is null)
            generated = generated with { Warnings = generated.Warnings.Concat(["Identité éditoriale absente : politique neutre, sans personnalisation du workspace."]).ToArray() };
        cancellationToken.ThrowIfCancellationRequested();

        var post = SocialPost.Create(generated.Title, generated.Content, request.Platform);
        post.Update(generated.Title, generated.Content, generated.CallToAction, generated.VisualBrief);
        var audit = AiGeneration.Create(post.Id, request.Subject, request.Objective, request.Audience,
            request.Platform, generation.Metadata.Provider, generation.Metadata.Model, generated.Warnings);
        if (generation.Metadata.Terminology is not null)
            audit.RecordTerminology(JsonSerializer.Serialize(generation.Metadata.Terminology));
        if (progress is not null) progress.Stage = DraftGenerationStage.Persistence;
        context.SocialPosts.Add(post);
        context.AiGenerations.Add(audit);
        await context.SaveChangesAsync(cancellationToken);
        return new(SocialPostDraftOutcome.Created,
            new GenerateSocialPostDraftResult(SocialPostDto.FromEntity(post), generated.Warnings), generation.Metadata, references);
    }
}
