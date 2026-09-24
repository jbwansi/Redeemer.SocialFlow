using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<SocialFlowDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<ISocialFlowDbContext>(provider => provider.GetRequiredService<SocialFlowDbContext>());
        return services;
    }
}
