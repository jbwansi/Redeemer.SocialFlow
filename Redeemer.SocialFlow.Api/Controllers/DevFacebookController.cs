using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Api.Development.Facebook;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
namespace Redeemer.SocialFlow.Api.Controllers;
public sealed record FacebookPageChoice(Guid ConnectionId, string PageId);
public sealed record FacebookDisconnect(Guid ConnectionId);
[ApiController]
[Route("api/dev/facebook")]
[ServiceFilter(typeof(FacebookAdminFilter))]
[RequestSizeLimit(64 * 1024)]
public sealed class DevFacebookController(FacebookPublicationService publication, IFacebookConnectionStore connections,
    FacebookFlows flows, IOptions<FacebookOptions> options, TimeProvider clock, FacebookGraphClient graph, ILogger<DevFacebookController> logger) : ControllerBase
{
    [HttpGet("runtime")]
    public Task<IActionResult> Runtime(CancellationToken token) => Safe(async () => {
        var runtime = await publication.RuntimeAsync(token); var connection = await connections.ReadAsync(token);
        var state = connection is null ? "disconnected" : connection.HasElapsedDeadline(clock.GetUtcNow()) ? "expired"
            : runtime.Connected ? "connected" : connection.Pages.Length == 0 ? "no-pages"
            : connection.SelectedPageId is null ? "select-page" : "verification-required";
        logger.LogInformation("Facebook runtime: Stage=runtime; ConnectionPersisted={ConnectionPersisted}; PagesCount={PagesCount}; State={State}", connection is not null, connection?.Pages.Length ?? 0, state);
        return (object)new { runtime.Connected, runtime.ExpiresAt, runtime.Destination, runtime.PublicationEnabled, runtime.WorkerEnabled,
            Configured = options.Value.Configured, ConnectionId = connection?.Id,
            ExpirationKnown = connection?.ExpiresAt is not null, DataAccessExpiresAt = connection?.DataAccessExpiresAt,
            RevalidationRequired = connection is not null && connection.ExpiresAt is null,
            State = state,
            Pages = connection?.Pages.Select(p => new { p.Id, p.Name }).ToArray() };
    });
    [HttpPost("connect")]
    public Task<IActionResult> Connect() => Safe(() => {
        if (!options.Value.Configured) throw new LinkedInPublicationException(503);
        return Task.FromResult<object>(new { AuthorizeUrl = "https://localhost:65474/api/dev/facebook/authorize?ticket=" + Uri.EscapeDataString(flows.Ticket()) });
    });
    [HttpPost("page")]
    public Task<IActionResult> Page(FacebookPageChoice request, CancellationToken token) => Safe(async () => {
        var connection = await connections.ReadAsync(token);
        if (connection is null || connection.HasElapsedDeadline(clock.GetUtcNow())) throw new FacebookAuthorizationException();
        if (connection.ExpiresAt is null) await graph.VerifyUserTokenAsync(connection.UserToken, token);
        await connections.SelectAsync(request.ConnectionId, request.PageId, token); return (object)new { Selected = true };
    });
    [HttpPost("disconnect")]
    public Task<IActionResult> Disconnect(FacebookDisconnect request, CancellationToken token) => Safe(async () => {
        await connections.DisconnectAsync(request.ConnectionId, token); return (object)new { Disconnected = true };
    });
    [HttpGet("posts/{id:guid}/publication")]
    public Task<IActionResult> Get(Guid id, CancellationToken token) => Safe(async () => (object)await publication.GetAsync(id, token));
    [HttpGet("posts/{id:guid}/preview")]
    public Task<IActionResult> Preview(Guid id, CancellationToken token) => Safe(async () => (object)await publication.PreviewAsync(id, token));
    [HttpPost("posts/{id:guid}/publish")]
    public Task<IActionResult> Publish(Guid id, LinkedInPublishRequest request, CancellationToken token) => Safe(async () => (object)await publication.PublishAsync(id, request.Confirmation, token));
    private async Task<IActionResult> Safe(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (LinkedInPublicationException error) { return Problem(statusCode:error.Status,title:"Publication indisponible ou confirmation périmée. Actualisez la publication."); }
        catch (FacebookAuthorizationException) { return Problem(statusCode:409,title:"Connexion ou Page indisponible. Actualisez puis reconnectez Facebook si nécessaire."); }
        catch (DomainException) { return Problem(statusCode:409,title:"État incompatible avec la publication."); }
        catch (Exception) { return Problem(statusCode:503,title:"Facebook indisponible. Consultez l’opération durable avant de continuer."); }
    }
}
