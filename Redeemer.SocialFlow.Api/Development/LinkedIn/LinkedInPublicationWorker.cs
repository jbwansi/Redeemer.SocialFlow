namespace Redeemer.SocialFlow.Api.Development.LinkedIn;

public sealed class LinkedInPublicationWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<LinkedInPublicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        try
        {
            // No immediate dispatch at host startup. Only explicitly enabled hosts register this worker.
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<LinkedInPublicationService>().ProcessDueAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception) { logger.LogWarning("LI_WORKER_SCAN_FAILED"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
