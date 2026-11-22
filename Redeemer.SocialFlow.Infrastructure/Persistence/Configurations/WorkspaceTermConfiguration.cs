using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;

public sealed class WorkspaceTermConfiguration : IEntityTypeConfiguration<WorkspaceTerm>
{
    public void Configure(EntityTypeBuilder<WorkspaceTerm> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.WorkspaceId, x.SearchKey, x.Language, x.UsageContext }).IsUnique();
        b.Property(x => x.Canonical).HasMaxLength(200); b.Property(x => x.SearchKey).HasMaxLength(200);
    }
}
public sealed class WorkspaceTermRevisionConfiguration : IEntityTypeConfiguration<WorkspaceTermRevision>
{
    public void Configure(EntityTypeBuilder<WorkspaceTermRevision> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.WorkspaceId, x.TermId, x.Revision }).IsUnique();
        b.HasOne<WorkspaceTerm>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.TermId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
