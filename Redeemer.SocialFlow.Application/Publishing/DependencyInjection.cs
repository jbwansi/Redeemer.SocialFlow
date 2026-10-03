using Microsoft.Extensions.DependencyInjection;

namespace Redeemer.SocialFlow.Application.Publishing;

public static class DependencyInjection
{
    /// <summary>Opt-in wiring: the host must deliberately select a publisher. No default external adapter.</summary>
    public static IServiceCollection AddPostPublication<TPublisher>(this IServiceCollection services)
        where TPublisher : class, IPostPublisher
    {
        services.AddScoped<IPostPublisher, TPublisher>();
        services.AddScoped<IPublishScheduledPost, PublishScheduledPost>();
        return services;
    }
}
