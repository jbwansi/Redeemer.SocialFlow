using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("KnowledgeChunks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasOne<KnowledgeDocument>().WithMany().HasForeignKey(x => x.KnowledgeDocumentId)
            .IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.ChunkIndex).IsRequired();
        builder.Property(x => x.PageNumber).IsRequired(false);
        builder.Property(x => x.Section).IsRequired(false);
        builder.Property(x => x.CreatedAt).HasColumnType("TEXT").IsRequired();
    }
}
