using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.SocialPosts;

namespace Redeemer.SocialFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ISocialPostService, SocialPostService>();
        return services;
    }
}
