using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.Accounts;
namespace Redeemer.SocialFlow.Infrastructure.Social;
public static class SocialDependencyInjection
{
    public static void AddWorkspaceSocialConnections(this IServiceCollection services)
    {
        services.AddSingleton<ISocialSecretProtector,SocialSecretProtector>();
        services.AddScoped<ISocialConnections,SocialConnections>();
        services.AddScoped<IWorkspaceSocialConnectionResolver,WorkspaceSocialConnectionResolver>();
        // Dedicated clients deliberately avoid request-logging handlers and provider redirects.
        services.AddScoped<IWorkspaceOAuthProvider>(p=>ActivatorUtilities.CreateInstance<LinkedInWorkspaceOAuthProvider>(p,Client()));
        services.AddScoped<IWorkspaceOAuthProvider>(p=>ActivatorUtilities.CreateInstance<FacebookWorkspaceOAuthProvider>(p,Client()));
        services.AddScoped<IWorkspaceOAuthProvider>(p=>ActivatorUtilities.CreateInstance<InstagramWorkspaceOAuthProvider>(p,Client()));
    }
    private static HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(25), MaxResponseContentBufferSize=2*1024*1024 };
}
