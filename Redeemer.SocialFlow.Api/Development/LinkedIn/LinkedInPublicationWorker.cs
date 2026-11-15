using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Api.Publishing;
namespace Redeemer.SocialFlow.Api.Development.LinkedIn;

public sealed class LinkedInPublicationWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<LinkedInPublicationWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = Math.Clamp(configuration.GetValue("SocialConnections:Publication:PollIntervalSeconds", 30), 5, 300);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval), clock);
        try
        {
            // No immediate dispatch at host startup. Only explicitly enabled hosts register this worker.
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<Redeemer.SocialFlow.Application.Accounts.WorkspaceAccessContext>()
                        .Bind(Redeemer.SocialFlow.Domain.Entities.Workspace.HistoricalId);
                    try { if (scope.ServiceProvider.GetService<LinkedInPublicationService>() is { } linkedIn) await linkedIn.ProcessDueAsync(stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception) { logger.LogWarning("LI_WORKER_SCAN_FAILED"); }
                    var facebook = scope.ServiceProvider.GetService<Redeemer.SocialFlow.Api.Development.Facebook.FacebookPublicationService>();
                    try { if (facebook is not null) await facebook.ProcessDueAsync(stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception) { logger.LogWarning("FB_WORKER_SCAN_FAILED"); }
                    if (scope.ServiceProvider.GetRequiredService<IConfiguration>().GetValue<bool>("SocialConnections:Publication:WorkerEnabled"))
                    {
                        var db = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
                        var workspaces = await db.Workspaces.AsNoTracking().Where(x => x.Status == WorkspaceStatus.Active).Select(x => x.Id).ToListAsync(stoppingToken);
                        foreach (var workspaceId in workspaces)
                        {
                            await using var tenant = scopes.CreateAsyncScope();
                            tenant.ServiceProvider.GetRequiredService<WorkspaceAccessContext>().Bind(workspaceId);
                            try { await tenant.ServiceProvider.GetRequiredService<WorkspacePublicationService>().ProcessDueAsync(stoppingToken); }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                            catch (Exception) { logger.LogWarning("PUBLICATION_WORKSPACE_SCAN_FAILED"); }
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception) { logger.LogWarning("LI_WORKER_SCAN_FAILED"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
