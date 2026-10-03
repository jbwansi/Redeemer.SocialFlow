using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.SocialPosts;

namespace Redeemer.SocialFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ISocialPostService, SocialPostService>();
        services.AddScoped<IListTrashedSocialPosts, ListTrashedSocialPosts>();
        services.AddScoped<IGenerateSocialPostDraft, GenerateSocialPostDraft>();
        services.AddScoped<Knowledge.IGenerateGroundedContent, Knowledge.GenerateGroundedContent>();
        services.AddScoped<Knowledge.IKnowledgeDocumentService, Knowledge.KnowledgeDocumentService>();
        return services;
    }
}
