using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Application.Abstractions;

/// <summary>
/// Unit of work for knowledge documents and their chunks, following ISocialFlowDbContext.
/// Changes to a document and its chunks are committed together by SaveChangesAsync.
/// </summary>
public interface IKnowledgeDbContext
{
    DbSet<KnowledgeDocument> KnowledgeDocuments { get; }
    DbSet<KnowledgeChunk> KnowledgeChunks { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
