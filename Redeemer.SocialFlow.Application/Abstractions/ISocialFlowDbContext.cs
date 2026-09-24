using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Application.Abstractions;

public interface ISocialFlowDbContext
{
    DbSet<SocialPost> SocialPosts { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
