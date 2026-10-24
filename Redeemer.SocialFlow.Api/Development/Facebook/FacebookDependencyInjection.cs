using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Api.Development.Visuals;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Infrastructure.Publishing;
using Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;
namespace Redeemer.SocialFlow.Api.Development.Facebook;
public static class FacebookDependencyInjection
{
    public static IServiceCollection AddLocalFacebook(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("Facebook:Enabled")) return services;
        services.Configure<FacebookOptions>(configuration.GetSection("Facebook"));
        services.TryAddSingleton(TimeProvider.System); services.AddDataProtection();
        services.AddSingleton<FacebookFlows>(); services.AddSingleton<IFacebookConnectionStore, ProtectedFacebookConnectionStore>();
        services.AddScoped<FacebookAdminFilter>(_ => new FacebookAdminFilter(new LocalVisualAccess(true, false,
            Environment.GetEnvironmentVariable("SOCIALFLOW_VISUAL_ADMIN_SECRET"), configuration["Facebook:AppSecret"], configuration["OpenAI:ApiKey"], configuration["OpenAI:Images:ApiKey"]), environment));
        services.AddScoped<FacebookPublicationService>();
        services.AddHttpClient<FacebookGraphClient>(c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }).RemoveAllLoggers();
        if (configuration.GetValue<bool>("Facebook:PublicationEnabled"))
        {
            services.AddHttpClient<FacebookPostPublisher>(c => c.Timeout = TimeSpan.FromSeconds(60))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }).RemoveAllLoggers();
            services.AddScoped<IPostPublisher>(p => new PlatformPostPublisher(p.GetService<LinkedInPostPublisher>(), p.GetRequiredService<FacebookPostPublisher>()));
            services.AddScoped<IPublishScheduledPost, PublishScheduledPost>();
            // AddHostedService uses TryAddEnumerable: the existing LinkedIn timer is never duplicated.
            if (configuration.GetValue<bool>("Facebook:WorkerEnabled")) services.AddHostedService<LinkedInPublicationWorker>();
        }
        return services;
    }
}
