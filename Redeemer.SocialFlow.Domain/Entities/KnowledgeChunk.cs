using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public sealed class KnowledgeChunk
{
    private KnowledgeChunk() { }
    public Guid Id { get; private set; }
    public Guid KnowledgeDocumentId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public int ChunkIndex { get; private set; }
    public int? PageNumber { get; private set; }
    public string? Section { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static KnowledgeChunk Create(Guid knowledgeDocumentId, string content, int chunkIndex,
        int? pageNumber = null, string? section = null, TimeProvider? timeProvider = null)
    {
        if (knowledgeDocumentId == Guid.Empty) throw new DomainException("A knowledge document is required.");
        if (string.IsNullOrWhiteSpace(content)) throw new DomainException("Chunk content is required.");
        if (chunkIndex < 0) throw new DomainException("Chunk index must be non-negative.");
        if (pageNumber is <= 0) throw new DomainException("Page number must be positive.");
        return new KnowledgeChunk
        {
            Id = Guid.NewGuid(), KnowledgeDocumentId = knowledgeDocumentId,
            Content = content, ChunkIndex = chunkIndex, PageNumber = pageNumber,
            Section = string.IsNullOrWhiteSpace(section) ? null : section.Trim(),
            CreatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow()
        };
    }
}
