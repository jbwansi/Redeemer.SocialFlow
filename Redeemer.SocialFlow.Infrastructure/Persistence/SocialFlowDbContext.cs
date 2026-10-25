using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Application.Accounts;

namespace Redeemer.SocialFlow.Infrastructure.Persistence;

public sealed class SocialFlowDbContext(DbContextOptions<SocialFlowDbContext> options, IWorkspaceAccessContext? workspace = null)
    : DbContext(options), ISocialFlowDbContext, IKnowledgeDbContext
{
    public Guid? ActiveWorkspaceId => workspace?.WorkspaceId;
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceInvitation> WorkspaceInvitations => Set<WorkspaceInvitation>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<StoredVisualIdentity> VisualIdentities => Set<StoredVisualIdentity>();
    public DbSet<IdentityLogo> IdentityLogos => Set<IdentityLogo>();
    public DbSet<PostVisual> PostVisuals => Set<PostVisual>();
    public DbSet<SocialPost> SocialPosts => Set<SocialPost>();
    public DbSet<AiGeneration> AiGenerations => Set<AiGeneration>();
    public DbSet<PublicationOperation> PublicationOperations => Set<PublicationOperation>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SocialFlowDbContext).Assembly);
        Tenant<SocialPost>(modelBuilder);
        Tenant<AiGeneration>(modelBuilder);
        Tenant<KnowledgeDocument>(modelBuilder);
        Tenant<KnowledgeChunk>(modelBuilder);
        Tenant<PublicationOperation>(modelBuilder);
        Tenant<PostVisual>(modelBuilder);
        Tenant<StoredVisualIdentity>(modelBuilder);
        Tenant<IdentityLogo>(modelBuilder);
        Tenant<WorkspaceInvitation>(modelBuilder);
    }

    private void Tenant<T>(ModelBuilder model) where T : class, IWorkspaceResource
    {
        model.Entity<T>().Property(x => x.WorkspaceId).IsConcurrencyToken();
        model.Entity<T>().HasIndex(x => x.WorkspaceId);
        model.Entity<T>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<T>().HasQueryFilter("Workspace", x => ActiveWorkspaceId != null && x.WorkspaceId == ActiveWorkspaceId);
    }
    private void GuardWrites()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<IWorkspaceResource>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var id = ActiveWorkspaceId ?? throw new InvalidOperationException("An explicit workspace context is required for writes.");
            var property = entry.Property(nameof(IWorkspaceResource.WorkspaceId));
            if (entry.State == EntityState.Added && (Guid)property.CurrentValue! == Guid.Empty) property.CurrentValue = id;
            if ((Guid)property.CurrentValue! != id || (entry.State != EntityState.Added && (Guid)property.OriginalValue! != id))
                throw new InvalidOperationException("Cross-workspace writes are prohibited.");
        }
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { GuardWrites(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken token = default)
    { GuardWrites(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, token); }
}
