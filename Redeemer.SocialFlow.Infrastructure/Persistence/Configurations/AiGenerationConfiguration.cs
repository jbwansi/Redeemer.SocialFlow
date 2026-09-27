using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;

public sealed class AiGenerationConfiguration : IEntityTypeConfiguration<AiGeneration>
{
    public void Configure(EntityTypeBuilder<AiGeneration> builder)
    {
        builder.ToTable("AiGenerations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasOne<SocialPost>().WithMany().HasForeignKey(x => x.SocialPostId)
            .IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Subject).IsRequired();
        builder.Property(x => x.Objective).IsRequired();
        builder.Property(x => x.Audience).IsRequired();
        builder.Property(x => x.Platform).HasConversion<int>().IsRequired();
        builder.Property(x => x.Provider).IsRequired();
        builder.Property(x => x.Model).IsRequired();
        builder.Property(x => x.GeneratedAt).HasColumnType("TEXT").IsRequired();
        builder.Property(x => x.Warnings).HasConversion(
            value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
            value => Array.AsReadOnly(JsonSerializer.Deserialize<string[]>(value, (JsonSerializerOptions?)null)!))
            .HasColumnType("TEXT").IsRequired()
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                (left, right) => left!.SequenceEqual(right!),
                value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                value => Array.AsReadOnly(value.ToArray())));
    }
}
