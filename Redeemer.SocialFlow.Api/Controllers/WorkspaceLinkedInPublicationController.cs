using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Api.Publishing;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Domain.Enums;
namespace Redeemer.SocialFlow.Api.Controllers;
public sealed record WorkspaceLinkedInPreviewRequest(Guid ConnectionId);

[ApiController, Route("api/linkedin/posts/{id:guid}"), Authorize(Policy = "AccountSession")]
[Authorize(Policy = "WorkspaceApprove")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceLinkedInPublicationController(WorkspacePublicationService publication, ICollaborationActor actor) : ControllerBase
{
    [HttpGet("publication")]
    public Task<LinkedInPublicationView> Get(Guid id, CancellationToken ct) => publication.Get(SocialPlatform.LinkedIn, id, ct);
    [HttpPost("preview")]
    public Task<LinkedInPreview> Preview(Guid id, WorkspaceLinkedInPreviewRequest request, CancellationToken ct) => publication.Preview(SocialPlatform.LinkedIn, actor.UserId, id, request, ct);
    [HttpPost("publish")]
    public Task<PublicationOperationDto> Publish(Guid id, LinkedInPublishRequest request, CancellationToken ct) => publication.Publish(SocialPlatform.LinkedIn, actor.UserId, id, request, ct);
    [HttpPost("automatic")]
    public async Task<IActionResult> Automatic(Guid id, WorkspaceLinkedInPreviewRequest request, CancellationToken ct)
    { await publication.AuthorizeAutomatic(SocialPlatform.LinkedIn, actor.UserId, id, request.ConnectionId, ct); return NoContent(); }
    [HttpDelete("automatic")]
    public async Task<IActionResult> CancelAutomatic(Guid id, CancellationToken ct)
    { await publication.CancelAutomatic(SocialPlatform.LinkedIn, id, ct); return NoContent(); }
}
