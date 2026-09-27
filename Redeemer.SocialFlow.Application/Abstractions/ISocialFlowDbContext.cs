using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Application.Abstractions;

public interface ISocialFlowDbContext
{
    DbSet<SocialPost> SocialPosts { get; }
    DbSet<AiGeneration> AiGenerations { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
