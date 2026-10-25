using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Api.Security;

public sealed class SocialFlowDesignTimeFactory : IDesignTimeDbContextFactory<SocialFlowDbContext>
{
    public SocialFlowDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite("Data Source=socialflow.db").Options);
}
