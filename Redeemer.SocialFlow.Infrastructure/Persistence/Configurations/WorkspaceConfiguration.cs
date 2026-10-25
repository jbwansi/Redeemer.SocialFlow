using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).IsRequired().HasMaxLength(120);
        b.Property(x => x.PersonalOwnerUserId).HasMaxLength(450);
        b.HasIndex(x => x.PersonalOwnerUserId).IsUnique();
        b.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
        b.HasData(new { Id = Workspace.HistoricalId, Name = "Redeemer Holding", Type = WorkspaceType.Organization,
            Status = WorkspaceStatus.Suspended, CreatedAtUtc = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero) });
    }
}
public sealed class WorkspaceMembershipConfiguration : IEntityTypeConfiguration<WorkspaceMembership>
{
    public void Configure(EntityTypeBuilder<WorkspaceMembership> b)
    {
        b.HasKey(x => new { x.WorkspaceId, x.UserId });
        b.Property(x => x.UserId).HasMaxLength(450);
        b.HasIndex(x => new { x.UserId, x.Status });
        b.Property(x => x.JoinedAtUtc).HasColumnType("TEXT");
        b.Property(x => x.Role).IsConcurrencyToken(); b.Property(x => x.Status).IsConcurrencyToken();
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
    }
}
