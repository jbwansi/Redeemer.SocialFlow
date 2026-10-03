using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.Knowledge;

public sealed record CreateKnowledgeChunkRequest(string Content, int ChunkIndex, int? PageNumber = null, string? Section = null);

public sealed record CreateKnowledgeDocumentRequest(string Title, string PrimaryTheme,
    IReadOnlyList<string> Themes, IReadOnlyList<KnowledgeUsage> Usages, SourceType SourceType,
    AuthorityLevel AuthorityLevel, string Language, IReadOnlyList<CreateKnowledgeChunkRequest> Chunks,
    int Version = 1, bool IsActive = true);

public sealed record ListKnowledgeDocumentsRequest(int PageNumber = 1, int PageSize = 20,
    KnowledgeDocumentStatus? Status = null, string? Theme = null, string? Language = null);
