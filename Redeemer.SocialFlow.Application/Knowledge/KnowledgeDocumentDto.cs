using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.Knowledge;

public sealed record KnowledgeDocumentDto(Guid Id, string Title, string PrimaryTheme,
    IReadOnlyList<string> Themes, KnowledgeDocumentStatus Status, IReadOnlyList<KnowledgeUsage> Usages,
    SourceType SourceType, AuthorityLevel AuthorityLevel, string Language, int Version, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static KnowledgeDocumentDto FromEntity(KnowledgeDocument document) => new(document.Id,
        document.Title, document.PrimaryTheme, Array.AsReadOnly(document.Themes.ToArray()), document.Status,
        Array.AsReadOnly(document.Usages.ToArray()), document.SourceType, document.AuthorityLevel,
        document.Language, document.Version, document.IsActive, document.CreatedAt, document.UpdatedAt);
}

public sealed record KnowledgeChunkDto(Guid Id, Guid KnowledgeDocumentId, string Content, int ChunkIndex,
    int? PageNumber, string? Section, DateTimeOffset CreatedAt);

public sealed record KnowledgeDocumentDetailsDto(KnowledgeDocumentDto Document, IReadOnlyList<KnowledgeChunkDto> Chunks);

public sealed record KnowledgeDocumentPage(IReadOnlyList<KnowledgeDocumentDto> Items, int TotalCount, int PageNumber, int PageSize);
