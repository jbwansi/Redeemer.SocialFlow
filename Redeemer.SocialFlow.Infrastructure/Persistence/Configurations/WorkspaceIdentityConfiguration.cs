using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;

public sealed class WorkspaceEditorialProfileConfiguration : IEntityTypeConfiguration<WorkspaceEditorialProfile>
{
    public void Configure(EntityTypeBuilder<WorkspaceEditorialProfile> b)
    {
        b.HasKey(x => x.WorkspaceId); b.Property(x => x.WorkspaceId).ValueGeneratedNever();
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.Property(x => x.OrganizationName).HasMaxLength(200);
        foreach (var name in new[] { "Mission", "Values", "Positioning", "Audiences", "Tone", "Themes", "Rules", "Avoid" })
            b.Property<string>(name).HasMaxLength(4000);
    }
}
public sealed class VisualIdentityApprovalConfiguration : IEntityTypeConfiguration<VisualIdentityApproval>
{
    public void Configure(EntityTypeBuilder<VisualIdentityApproval> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.WorkspaceId, x.IdentityId, x.Revision }).IsUnique();
        b.Property(x => x.ApprovedBy).HasMaxLength(450);
        b.HasOne<StoredVisualIdentity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.IdentityId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
