using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Infrastructure.Persistence;

public sealed class SocialFlowDbContext(DbContextOptions<SocialFlowDbContext> options)
    : DbContext(options), ISocialFlowDbContext
{
    public DbSet<SocialPost> SocialPosts => Set<SocialPost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SocialFlowDbContext).Assembly);
    }
}
