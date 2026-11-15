using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Api.Publishing;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Domain.Enums;
namespace Redeemer.SocialFlow.Api.Controllers;

[ApiController, Route("api/facebook/posts/{id:guid}"), Authorize(Policy = "AccountSession")]
[Authorize(Policy = "WorkspaceApprove")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceFacebookPublicationController(WorkspacePublicationService publication, ICollaborationActor actor) : ControllerBase
{
    [HttpGet("publication")]
    public Task<LinkedInPublicationView> Get(Guid id, CancellationToken ct) => publication.Get(SocialPlatform.Facebook, id, ct);
    [HttpPost("preview")]
    public Task<LinkedInPreview> Preview(Guid id, WorkspaceLinkedInPreviewRequest request, CancellationToken ct) => publication.Preview(SocialPlatform.Facebook, actor.UserId, id, request, ct);
    [HttpPost("publish")]
    public Task<PublicationOperationDto> Publish(Guid id, LinkedInPublishRequest request, CancellationToken ct) => publication.Publish(SocialPlatform.Facebook, actor.UserId, id, request, ct);
    [HttpPost("automatic")]
    public async Task<IActionResult> Automatic(Guid id, WorkspaceLinkedInPreviewRequest request, CancellationToken ct)
    { await publication.AuthorizeAutomatic(SocialPlatform.Facebook, actor.UserId, id, request.ConnectionId, ct); return NoContent(); }
    [HttpDelete("automatic")]
    public async Task<IActionResult> CancelAutomatic(Guid id, CancellationToken ct)
    { await publication.CancelAutomatic(id, ct); return NoContent(); }
}
