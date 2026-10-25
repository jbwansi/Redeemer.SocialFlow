using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;
namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;
public sealed class WorkspaceInvitationConfiguration : IEntityTypeConfiguration<WorkspaceInvitation>
{
    public void Configure(EntityTypeBuilder<WorkspaceInvitation> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.NormalizedEmail).HasMaxLength(254);
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.Property(x => x.InvitedByUserId).HasMaxLength(450);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.WorkspaceId, x.NormalizedEmail }).IsUnique().HasFilter("Status = 1");
    }
}
