using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;

public sealed class SocialPostConfiguration : IEntityTypeConfiguration<SocialPost>
{
    public void Configure(EntityTypeBuilder<SocialPost> builder)
    {
        builder.ToTable("SocialPosts");
        builder.HasQueryFilter(post => !post.IsDeleted);
        builder.Property(post => post.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(post => post.DeletedAt).HasColumnType("TEXT").IsRequired(false);
        builder.HasKey(post => post.Id);
        builder.Property(post => post.Id).ValueGeneratedNever();
        builder.Property(post => post.Title).IsRequired();
        builder.Property(post => post.Content).IsRequired();
        builder.Property(post => post.Platform).HasConversion<int>().IsRequired();
        // Prevent stale workflow updates from overwriting a concurrently confirmed publication.
        builder.Property(post => post.Status).HasConversion<int>().IsRequired().IsConcurrencyToken();
        builder.Property(post => post.CallToAction).IsRequired(false);
        builder.Property(post => post.VisualBrief).IsRequired(false);
        builder.Property(post => post.VisualUrl).IsRequired(false);
        // SQLite TEXT preserves DateTimeOffset precision and the original offset.
        builder.Property(post => post.ScheduledAt).HasColumnType("TEXT").IsRequired(false);
        builder.Property(post => post.PublishedAt).HasColumnType("TEXT").IsRequired(false);
        builder.Property(post => post.CreatedAt).HasColumnType("TEXT").IsRequired();
        builder.Property(post => post.UpdatedAt).HasColumnType("TEXT").IsRequired();
    }
}
