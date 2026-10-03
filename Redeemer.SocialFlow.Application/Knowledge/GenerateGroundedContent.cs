using System.Text.Json;
using Redeemer.SocialFlow.Application.AI;

namespace Redeemer.SocialFlow.Application.Knowledge;

public sealed class GenerateGroundedContent(IKnowledgePassageSearch search, IContentGenerator generator)
    : IGenerateGroundedContent
{
    public const int MaximumPassages = 5;
    // Full serialized reference data, including provenance and escaping; not a token estimate.
    public const int MaximumContextCharacters = 12_000;

    public async Task<GroundedContentResult> ExecuteAsync(GenerateContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var passages = await search.SearchForGenerationAsync(new(request.Subject, MaximumPassages), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (passages.Count == 0)
            return new(GroundedContentOutcome.NoRelevantPassages, null, Array.Empty<KnowledgePassage>());
        var selected = new List<KnowledgePassage>();
        var seen = new HashSet<Guid>();
        foreach (var passage in passages.Take(MaximumPassages))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(passage.KnowledgeChunkId)) continue;
            selected.Add(passage);
            if (JsonSerializer.Serialize(selected).Length > MaximumContextCharacters)
                selected.RemoveAt(selected.Count - 1);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (selected.Count == 0)
            return new(GroundedContentOutcome.ContextLimitExceeded, null, Array.Empty<KnowledgePassage>());
        var references = selected.AsReadOnly();
        // Only retrieved, bounded references are passed, never caller-supplied references.
        var result = await generator.GenerateAsync(request with { ReferencePassages = references }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(GroundedContentOutcome.Generated, result, references);
    }
}
