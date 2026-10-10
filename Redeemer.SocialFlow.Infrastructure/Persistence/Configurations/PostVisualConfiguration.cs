using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;

public sealed class PostVisualConfiguration : IEntityTypeConfiguration<PostVisual>
{
    public void Configure(EntityTypeBuilder<PostVisual> b)
    {
        b.HasKey(x => x.Id);
        b.HasOne<StoredVisualIdentity>().WithMany().HasForeignKey(x => x.SourceIdentityId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.SocialPostId, x.Id });
        b.HasOne<SocialPost>().WithMany().HasForeignKey(x => x.SocialPostId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.StorageKey).IsUnique();
        b.Property(x => x.Origin).HasConversion<int>();
        b.Property(x => x.StorageKey).IsRequired();
        b.Property(x => x.ContentType).IsRequired();
        b.Property(x => x.Sha256).IsRequired();
        b.Property(x => x.IsDeleted).IsConcurrencyToken();
        b.Property(x => x.CreatedAt).HasColumnType("TEXT");
        b.Property(x => x.ValidatedAt).HasColumnType("TEXT");
        b.Property(x => x.DeletedAt).HasColumnType("TEXT");
        b.Property(x => x.PurgedAt).HasColumnType("TEXT");
    }
}
