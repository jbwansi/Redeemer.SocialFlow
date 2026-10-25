using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;
namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;
public sealed class LinkedInAutomationConfiguration : IEntityTypeConfiguration<LinkedInAutomation>
{
    public void Configure(EntityTypeBuilder<LinkedInAutomation> b)
    {
        b.HasKey(x => new { x.WorkspaceId, x.SocialPostId });
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.Property(x => x.AuthorizedByUserId).HasMaxLength(450);
        b.Property(x => x.BlockReason).HasMaxLength(256);
        b.HasOne<SocialPost>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.SocialPostId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SocialConnection>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.SocialConnectionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
