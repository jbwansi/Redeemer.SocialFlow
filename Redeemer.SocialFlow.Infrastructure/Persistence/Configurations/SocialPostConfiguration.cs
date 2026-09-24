using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;

public sealed class SocialPostConfiguration : IEntityTypeConfiguration<SocialPost>
{
    public void Configure(EntityTypeBuilder<SocialPost> builder)
    {
        builder.ToTable("SocialPosts");
        builder.HasKey(post => post.Id);
        builder.Property(post => post.Id).ValueGeneratedNever();
        builder.Property(post => post.Title).IsRequired();
        builder.Property(post => post.Content).IsRequired();
        builder.Property(post => post.Platform).HasConversion<int>().IsRequired();
        builder.Property(post => post.Status).HasConversion<int>().IsRequired();
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
