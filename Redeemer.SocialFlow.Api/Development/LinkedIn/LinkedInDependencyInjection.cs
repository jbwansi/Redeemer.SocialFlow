using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;

namespace Redeemer.SocialFlow.Api.Development.LinkedIn;

public static class LinkedInDependencyInjection
{
    public static IServiceCollection AddLocalLinkedIn(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return services;
        services.Configure<LinkedInOptions>(configuration.GetSection("LinkedIn"));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<LinkedInStateStore>();
        services.AddSingleton<LinkedInTokenStore>();
        services.AddDataProtection();
        services.AddScoped<ILinkedInCredentialProvider, ProtectedLinkedInCredentialProvider>();
        var enabled = configuration.GetValue<bool>("LinkedIn:PublicationEnabled");
        var workerEnabled = enabled && configuration.GetValue<bool>("LinkedIn:WorkerEnabled");
        services.AddSingleton(new LinkedInPublicationSettings(enabled, workerEnabled));
        services.AddScoped<LinkedInPublicationService>();
        if (workerEnabled) services.AddHostedService<LinkedInPublicationWorker>();
        services.AddHttpClient<LinkedInOAuthClient>(client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers(); // no bodies, authorization headers or token endpoint diagnostics
        if (enabled)
        {
            services.AddHttpClient<LinkedInPostPublisher>(client => client.Timeout = TimeSpan.FromSeconds(30))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
                .RemoveAllLoggers(); // deliberately no retry/resilience handler
            services.AddScoped<IPostPublisher>(provider => provider.GetRequiredService<LinkedInPostPublisher>());
            services.AddScoped<IPublishScheduledPost, PublishScheduledPost>();
        }
        return services;
    }
}
