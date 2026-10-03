namespace Redeemer.SocialFlow.Application.Knowledge;

public interface IKnowledgeDocumentService
{
    Task<KnowledgeDocumentDetailsDto> CreateAsync(CreateKnowledgeDocumentRequest request, CancellationToken cancellationToken = default);
    /// <summary>Returns null when the document is absent, consistent with SocialPost reads.</summary>
    Task<KnowledgeDocumentDetailsDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Filters before pagination; newest creation first, then ID ascending.</summary>
    Task<KnowledgeDocumentPage> ListAsync(ListKnowledgeDocumentsRequest? request = null, CancellationToken cancellationToken = default);
    /// <summary>Uses Domain ChangeStatus(Approved); throws KnowledgeDocumentNotFoundException when absent.</summary>
    Task<KnowledgeDocumentDto> ApproveAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Uses Domain SetActive without changing status or usages; throws KnowledgeDocumentNotFoundException when absent.</summary>
    Task<KnowledgeDocumentDto> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
}
