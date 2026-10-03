using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("KnowledgeDocuments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).IsRequired();
        builder.Property(x => x.PrimaryTheme).IsRequired();
        builder.Property(x => x.Language).IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.SourceType).HasConversion<int>().IsRequired();
        builder.Property(x => x.AuthorityLevel).HasConversion<int>().IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("TEXT").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("TEXT").IsRequired();
        builder.Ignore(x => x.CanBeUsedForGeneration);
        builder.Property(x => x.Themes).HasConversion(
            value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
            value => Array.AsReadOnly(JsonSerializer.Deserialize<string[]>(value, (JsonSerializerOptions?)null)!))
            .HasColumnType("TEXT").IsRequired().Metadata.SetValueComparer(
                new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => a!.SequenceEqual(b!),
                    value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                    value => Array.AsReadOnly(value.ToArray())));
        builder.Property(x => x.Usages).HasConversion(
            value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
            value => Array.AsReadOnly(JsonSerializer.Deserialize<KnowledgeUsage[]>(value, (JsonSerializerOptions?)null)!))
            .HasColumnType("TEXT").IsRequired().Metadata.SetValueComparer(
                new ValueComparer<IReadOnlyList<KnowledgeUsage>>(
                    (a, b) => a!.SequenceEqual(b!),
                    value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                    value => Array.AsReadOnly(value.ToArray())));
    }
}
