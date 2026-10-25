using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Redeemer.SocialFlow.Infrastructure.Accounts;

namespace Redeemer.SocialFlow.Api.Security;

// Creating migration metadata never starts the application or its external integrations.
public sealed class AccountDesignTimeFactory : IDesignTimeDbContextFactory<AccountDbContext>
{
    public AccountDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<AccountDbContext>()
            .UseSqlite("Data Source=socialflow-accounts.db").Options);
}
