using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Knowledge;

namespace Redeemer.SocialFlow.Application.SocialPosts;

public enum SocialPostDraftOutcome { Created = 1, NoRelevantPassages = 2, ContextLimitExceeded = 3 }

/// <summary>
/// Draft is absent when generation cannot proceed. References are the supplied context,
/// not persisted citations or proof that generated assertions have been verified.
/// </summary>
public sealed record GenerateSocialPostDraftExecutionResult(SocialPostDraftOutcome Outcome,
    GenerateSocialPostDraftResult? Draft, ContentGenerationMetadata? Metadata,
    IReadOnlyList<KnowledgePassage> ReferencePassages)
{
    public Guid? PostId => Draft?.Post.Id;
}
