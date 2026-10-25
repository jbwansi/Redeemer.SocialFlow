using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;
namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;
public sealed class SocialConnectionConfiguration : IEntityTypeConfiguration<SocialConnection>
{
    public void Configure(EntityTypeBuilder<SocialConnection> b)
    {
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        b.Property(x => x.ExternalAccountId).HasMaxLength(256);
        b.Property(x => x.DisplayName).HasMaxLength(256); b.Property(x => x.AccountType).HasMaxLength(64);
        b.Property(x => x.ConnectedByUserId).HasMaxLength(450); b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.WorkspaceId, x.Platform, x.ExternalAccountId }).IsUnique().HasFilter("ConnectionStatus <> 3");
    }
}
public sealed class SocialOAuthSessionConfiguration : IEntityTypeConfiguration<SocialOAuthSession>
{
    public void Configure(EntityTypeBuilder<SocialOAuthSession> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.StateHash).HasMaxLength(64); b.HasIndex(x => x.StateHash).IsUnique();
        b.Property(x => x.UserId).HasMaxLength(450); b.Property(x => x.Revision).IsConcurrencyToken();
    }
}
public sealed class PostSocialDestinationConfiguration : IEntityTypeConfiguration<PostSocialDestination>
{
    public void Configure(EntityTypeBuilder<PostSocialDestination> b)
    {
        b.HasKey(x => new { x.WorkspaceId, x.SocialPostId, x.SocialConnectionId });
        b.HasOne<SocialPost>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.SocialPostId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SocialConnection>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.SocialConnectionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
