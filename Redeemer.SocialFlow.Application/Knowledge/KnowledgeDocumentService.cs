using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.Knowledge;

public sealed class KnowledgeDocumentService(IKnowledgeDbContext context, TimeProvider? timeProvider = null) : IKnowledgeDocumentService
{
    public async Task<KnowledgeDocumentDetailsDto> CreateAsync(CreateKnowledgeDocumentRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Chunks);
        var document = KnowledgeDocument.Create(request.Title, request.PrimaryTheme, request.Themes,
            request.Usages, request.SourceType, request.AuthorityLevel, request.Language,
            request.Version, request.IsActive, timeProvider);
        var chunks = new List<KnowledgeChunk>();
        // Finish all Domain validation before attaching any entity to the unit of work.
        foreach (var chunk in request.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(chunk);
            chunks.Add(KnowledgeChunk.Create(document.Id, chunk.Content, chunk.ChunkIndex,
                chunk.PageNumber, chunk.Section, timeProvider));
        }
        cancellationToken.ThrowIfCancellationRequested();
        context.KnowledgeDocuments.Add(document);
        context.KnowledgeChunks.AddRange(chunks);
        // EF commits the document and its chunks atomically in one SaveChanges transaction.
        await context.SaveChangesAsync(cancellationToken);
        return Details(document, chunks);
    }

    public async Task<KnowledgeDocumentDetailsDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = await context.KnowledgeDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (document is null) return null;
        var chunks = await context.KnowledgeChunks.AsNoTracking().Where(x => x.KnowledgeDocumentId == id).ToListAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Details(document, chunks);
    }

    public async Task<KnowledgeDocumentPage> ListAsync(ListKnowledgeDocumentsRequest? request = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request ??= new();
        if (request.PageNumber < 1 || request.PageSize is < 1 or > 100 ||
            request.Status is { } invalidStatus && !Enum.IsDefined(invalidStatus) ||
            request.Theme is not null && string.IsNullOrWhiteSpace(request.Theme) ||
            request.Language is not null && string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("PageNumber must be positive, PageSize between 1 and 100, Status valid and supplied filters non-blank.", nameof(request));
        var query = context.KnowledgeDocuments.AsNoTracking();
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        var language = request.Language?.Trim();
        if (language is not null) query = query.Where(x => x.Language == language);
        var theme = request.Theme?.Trim();
        var documents = new List<KnowledgeDocumentDto>();
        // JSON-converted themes and SQLite DateTimeOffset ordering require client evaluation.
        // Apply every filter before pagination, and never load chunk contents for the list.
        await foreach (var document in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (theme is not null && !document.Themes.Contains(theme, StringComparer.Ordinal)) continue;
            documents.Add(KnowledgeDocumentDto.FromEntity(document));
        }
        var offset = (long)(request.PageNumber - 1) * request.PageSize;
        var items = offset >= documents.Count ? Array.Empty<KnowledgeDocumentDto>() :
            documents.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip((int)offset).Take(request.PageSize).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new(items, documents.Count, request.PageNumber, request.PageSize);
    }

    public Task<KnowledgeDocumentDto> ApproveAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(id, document => document.ChangeStatus(KnowledgeDocumentStatus.Approved), cancellationToken);

    public Task<KnowledgeDocumentDto> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default) =>
        MutateAsync(id, document => document.SetActive(isActive), cancellationToken);

    private async Task<KnowledgeDocumentDto> MutateAsync(Guid id, Action<KnowledgeDocument> mutate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = await context.KnowledgeDocuments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KnowledgeDocumentNotFoundException(id);
        cancellationToken.ThrowIfCancellationRequested();
        mutate(document);
        await context.SaveChangesAsync(cancellationToken);
        return KnowledgeDocumentDto.FromEntity(document);
    }

    private static KnowledgeDocumentDetailsDto Details(KnowledgeDocument document, IEnumerable<KnowledgeChunk> chunks) =>
        new(KnowledgeDocumentDto.FromEntity(document), chunks.OrderBy(x => x.ChunkIndex).ThenBy(x => x.Id)
            .Select(x => new KnowledgeChunkDto(x.Id, x.KnowledgeDocumentId, x.Content, x.ChunkIndex, x.PageNumber, x.Section, x.CreatedAt)).ToArray());
}
