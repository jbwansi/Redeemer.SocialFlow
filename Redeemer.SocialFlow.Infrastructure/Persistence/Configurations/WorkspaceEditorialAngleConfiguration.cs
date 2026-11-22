using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Redeemer.SocialFlow.Domain.Entities;
namespace Redeemer.SocialFlow.Infrastructure.Persistence.Configurations;
public sealed class WorkspaceEditorialAngleConfiguration : IEntityTypeConfiguration<WorkspaceEditorialAngle>
{
    public void Configure(EntityTypeBuilder<WorkspaceEditorialAngle> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.WorkspaceId, x.Active, x.DisplayOrder });
    }
}
