using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.IntegrationTests;

// Existing fixtures operate within one explicit workspace in their temporary databases.
// This retains all tenant filters, write guards and relational constraints.
public static class TestWorkspace
{
    public static readonly IWorkspaceAccessContext Context = new ExplicitWorkspaceContext(Workspace.HistoricalId);
    public static SocialFlowDbContext Db(DbContextOptions<SocialFlowDbContext> options) => new(options, Context);
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<WorkspaceAccessContext>(_ => { var scope = new WorkspaceAccessContext(); scope.Bind(Workspace.HistoricalId); return scope; });
        services.AddScoped<IWorkspaceAccessContext>(p => p.GetRequiredService<WorkspaceAccessContext>());
    }
}
