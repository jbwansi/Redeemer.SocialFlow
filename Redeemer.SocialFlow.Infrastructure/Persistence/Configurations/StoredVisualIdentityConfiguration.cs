using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;

public sealed class StoredVisualIdentityConfiguration : IEntityTypeConfiguration<StoredVisualIdentity>
{
    public void Configure(EntityTypeBuilder<StoredVisualIdentity> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => x.IdentityId).IsUnique();
        b.HasIndex(x => x.IsDefault).IsUnique().HasFilter("\"IsDefault\" = 1");
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasOne<IdentityLogo>().WithMany().HasForeignKey(x => x.LogoId).OnDelete(DeleteBehavior.Restrict);
        b.HasData(new { Id = new Guid("8f116275-3b02-442f-a930-719e33b10601"), IdentityId = "RH", Name = "Redeemer Holding",
            Style = "professionnel, humain, moderne et épuré", Slogan = "Transformer par les valeurs",
            ColorsJson = "[\"#DA2E29\",\"#D4AF37\",\"#000000\",\"#FFFFFF\"]",
            IsActive = true, IsDeleted = false, IsDefault = true, Revision = 1L,
            CreatedAt = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero) });
    }
}
public sealed class IdentityLogoConfiguration : IEntityTypeConfiguration<IdentityLogo>
{
    public void Configure(EntityTypeBuilder<IdentityLogo> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => x.StorageKey).IsUnique();
        b.HasOne<StoredVisualIdentity>().WithMany().HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.IsReady).IsConcurrencyToken();
    }
}
