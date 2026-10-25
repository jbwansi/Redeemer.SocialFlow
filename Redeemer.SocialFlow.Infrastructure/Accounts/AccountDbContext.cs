using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Redeemer.SocialFlow.Infrastructure.Accounts;

public sealed class ApplicationUser : IdentityUser { }

// Separate migration history/database: historical SocialFlow migrations remain untouched.
public sealed class AccountDbContext(DbContextOptions<AccountDbContext> options)
    : IdentityUserContext<ApplicationUser>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
    }
}
