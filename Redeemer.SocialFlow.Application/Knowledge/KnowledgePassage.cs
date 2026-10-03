using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.Knowledge;

/// <summary>A retrieved chunk with document provenance for generation and source attribution.</summary>
public sealed record KnowledgePassage(
    Guid KnowledgeChunkId,
    Guid KnowledgeDocumentId,
    string DocumentTitle,
    int DocumentVersion,
    SourceType SourceType,
    AuthorityLevel AuthorityLevel,
    string Language,
    string Content,
    int ChunkIndex,
    int? PageNumber,
    string? Section);
