using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence;

public sealed class SocialFlowDbContext(DbContextOptions<SocialFlowDbContext> options)
    : DbContext(options), ISocialFlowDbContext, IKnowledgeDbContext
{
    public DbSet<SocialPost> SocialPosts => Set<SocialPost>();
    public DbSet<AiGeneration> AiGenerations => Set<AiGeneration>();
    public DbSet<PublicationOperation> PublicationOperations => Set<PublicationOperation>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SocialFlowDbContext).Assembly);
    }
}
