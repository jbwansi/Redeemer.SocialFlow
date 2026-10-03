using Redeemer.SocialFlow.Application.AI;

namespace Redeemer.SocialFlow.Application.Knowledge;

public enum GroundedContentOutcome { Generated = 1, NoRelevantPassages = 2, ContextLimitExceeded = 3 }

/// <summary>
/// References are the exact passages supplied as context, not proof that the model used each
/// passage or verified all assertions. Generation is null when no generation was attempted.
/// </summary>
public sealed record GroundedContentResult(GroundedContentOutcome Outcome,
    ContentGenerationResult? Generation, IReadOnlyList<KnowledgePassage> ReferencePassages);
