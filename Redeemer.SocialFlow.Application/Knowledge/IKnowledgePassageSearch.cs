using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Application.Knowledge;

/// <summary>Provider-independent, read-only retrieval of passages for content generation.</summary>
public interface IKnowledgePassageSearch
{
    /// <summary>
    /// Returns at most request.Limit passages, ordered by relevance, or an empty collection.
    /// Implementations MUST restrict candidates to documents eligible according to
    /// KnowledgeDocument.CanBeUsedForGeneration: Approved, active, and permitting Generation.
    /// Apply eligibility and optional filters before limiting results; never fall back to
    /// ineligible documents when no eligible passages match. Do not change document state.
    /// </summary>
    /// <remarks>
    /// Eligibility is evaluated against current document metadata at search time.
    /// Implementations must preserve the semantics of
    /// <see cref="KnowledgeDocument.CanBeUsedForGeneration"/> even when using an external index.
    /// Invalid requests must be rejected with ArgumentException.
    /// </remarks>
    Task<IReadOnlyList<KnowledgePassage>> SearchForGenerationAsync(
        SearchKnowledgePassagesRequest request, CancellationToken cancellationToken = default);
}
