using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Api.Controllers;

public sealed record LinkedInPublishRequest(string? Confirmation);

[ApiController]
[Route("api/dev/linkedin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DevLinkedInPublicationController(LinkedInPublicationService publication) : ControllerBase
{
    [HttpGet("runtime")]
    public Task<IActionResult> Runtime(CancellationToken token) => Safe(() => publication.RuntimeAsync(token), token);

    [HttpGet("posts/{id:guid}/publication")]
    public Task<IActionResult> Get(Guid id, CancellationToken token) => Safe(() => publication.GetAsync(id, token), token);

    [HttpGet("posts/{id:guid}/preview")]
    public Task<IActionResult> Preview(Guid id, CancellationToken token) => Safe(() => publication.PreviewAsync(id, token), token);

    [HttpPost("posts/{id:guid}/publish")]
    public Task<IActionResult> Publish(Guid id, [FromBody] LinkedInPublishRequest request, CancellationToken token)
        => Safe(() => publication.PublishAsync(id, request.Confirmation, token), token);

    private async Task<IActionResult> Safe<T>(Func<Task<T>> action, CancellationToken token)
    {
        try { return Ok(await action()); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return StatusCode(499); }
        catch (LinkedInPublicationException error)
        {
            return Problem(statusCode: error.Status, title: error.Status switch
            {
                404 => "Post not found.", 400 => "Invalid or expired publication confirmation.",
                409 => "Publication unavailable or confirmation changed. Refresh the post.",
                _ => "LinkedIn publication is disabled."
            });
        }
        catch (DomainException) { return Problem(statusCode: 409, title: "The post cannot be published in its current state."); }
        catch (Exception) { return Problem(statusCode: 503, title: "Publication status unavailable. Check the durable operation before continuing."); }
    }
}
