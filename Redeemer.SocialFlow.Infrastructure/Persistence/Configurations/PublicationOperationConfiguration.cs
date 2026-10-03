using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;

public sealed class PublicationOperationConfiguration : IEntityTypeConfiguration<PublicationOperation>
{
    public void Configure(EntityTypeBuilder<PublicationOperation> builder)
    {
        builder.ToTable("PublicationOperations");
        builder.HasKey(x => x.OperationId);
        builder.Property(x => x.OperationId).ValueGeneratedNever();
        // One logical operation per post, including certain failures: no automatic retry path.
        builder.HasIndex(x => x.SocialPostId).IsUnique();
        builder.HasOne<SocialPost>().WithMany().HasForeignKey(x => x.SocialPostId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Provider).IsRequired();
        builder.Property(x => x.Destination).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.Platform).HasConversion<int>().IsRequired();
        builder.Property(x => x.State).HasConversion<int>().IsRequired();
        builder.Property(x => x.ClaimedAt).HasColumnType("TEXT").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("TEXT").IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnType("TEXT");
        builder.Property(x => x.ExternalId).IsRequired(false);
    }
}
