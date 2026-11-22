using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Application.Knowledge;
using Redeemer.SocialFlow.Infrastructure.Knowledge;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Infrastructure.Publishing;

namespace Redeemer.SocialFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<SocialFlowDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<ISocialFlowDbContext>(provider => provider.GetRequiredService<SocialFlowDbContext>());
        services.AddScoped<IKnowledgeDbContext>(provider => provider.GetRequiredService<SocialFlowDbContext>());
        services.AddScoped<IKnowledgePassageSearch, SqliteKnowledgePassageSearch>();
        services.AddScoped<IPublicationOperationStore, SqlitePublicationOperationStore>();
        services.AddScoped<Visuals.WorkspaceIdentityService>();
        services.AddScoped<Terminology.WorkspaceTerminologyService>();
        services.AddScoped<Application.Terminology.ITerminologyReader>(p => p.GetRequiredService<Terminology.WorkspaceTerminologyService>());
        services.AddScoped<Application.AI.IWorkspaceEditorialProfileReader>(p => p.GetRequiredService<Visuals.WorkspaceIdentityService>());
        return services;
    }
}
