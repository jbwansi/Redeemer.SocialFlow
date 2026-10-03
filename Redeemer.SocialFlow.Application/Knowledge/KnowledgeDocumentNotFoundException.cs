namespace Redeemer.SocialFlow.Application.Knowledge;

public sealed class KnowledgeDocumentNotFoundException(Guid documentId)
    : Exception($"Knowledge document '{documentId}' was not found.")
{
    public Guid DocumentId { get; } = documentId;
}
