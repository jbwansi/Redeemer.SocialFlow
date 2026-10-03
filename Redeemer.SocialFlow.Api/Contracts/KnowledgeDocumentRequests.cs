using System.ComponentModel.DataAnnotations;
using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Api.Contracts;

public sealed record CreateKnowledgeDocumentHttpRequest(
    [Required] string Title,
    [Required] string PrimaryTheme,
    [Required] IReadOnlyList<string> Themes,
    [Required] IReadOnlyList<KnowledgeUsage> Usages,
    [Required, EnumDataType(typeof(SourceType))] SourceType? SourceType,
    [Required, EnumDataType(typeof(AuthorityLevel))] AuthorityLevel? AuthorityLevel,
    [Required] string Language,
    [Required] IReadOnlyList<CreateKnowledgeChunkHttpRequest> Chunks,
    [Range(1, int.MaxValue)] int Version = 1,
    bool IsActive = true);

public sealed record CreateKnowledgeChunkHttpRequest(
    [Required] string Content,
    [Required, Range(0, int.MaxValue)] int? ChunkIndex,
    [Range(1, int.MaxValue)] int? PageNumber = null,
    string? Section = null);

public sealed class ListKnowledgeDocumentsHttpRequest
{
    [Range(1, int.MaxValue)] public int PageNumber { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [EnumDataType(typeof(KnowledgeDocumentStatus))] public KnowledgeDocumentStatus? Status { get; init; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? Theme { get; init; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? Language { get; init; }
}

public sealed record SetKnowledgeDocumentActiveHttpRequest([Required] bool? IsActive);
