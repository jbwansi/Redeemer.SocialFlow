using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
namespace Redeemer.SocialFlow.Api.Controllers;

public sealed record InviteMemberRequest([Required, EmailAddress, StringLength(254)] string Email, WorkspaceRole Role);
public sealed record ChangeRoleRequest(WorkspaceRole Role, Guid Revision);
public sealed record RevisionRequest(Guid Revision);
public sealed record AcceptInvitationRequest([Required, StringLength(43, MinimumLength = 43)] string Code);

[ApiController, Route("api/workspaces/{workspaceId:guid}/team"), Authorize(Policy = "AccountSession")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeamController(IWorkspaceCollaboration team) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid workspaceId, CancellationToken ct) => Ok(await team.GetAsync(workspaceId, ct));
    [HttpPost("invitations"), EnableRateLimiting("Accounts")]
    public async Task<IActionResult> Invite(Guid workspaceId, InviteMemberRequest request, CancellationToken ct)
    { await team.InviteAsync(workspaceId, request.Email, request.Role, ct); return NoContent(); }
    [HttpPut("members/{userId}")]
    public async Task<IActionResult> Change(Guid workspaceId, string userId, ChangeRoleRequest request, CancellationToken ct)
    { await team.ChangeAsync(workspaceId, userId, request.Role, request.Revision, ct); return NoContent(); }
    [HttpPost("members/{userId}/remove")]
    public async Task<IActionResult> Remove(Guid workspaceId, string userId, RevisionRequest request, CancellationToken ct)
    { await team.RemoveAsync(workspaceId, userId, request.Revision, ct); return NoContent(); }
    [HttpPost("leave")]
    public async Task<IActionResult> Leave(Guid workspaceId, RevisionRequest request, CancellationToken ct)
    { await team.LeaveAsync(workspaceId, request.Revision, ct); return NoContent(); }
    [HttpPost("invitations/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid workspaceId, Guid id, RevisionRequest request, CancellationToken ct)
    { await team.RevokeAsync(workspaceId, id, request.Revision, ct); return NoContent(); }
}
[ApiController, Route("api/invitations"), Authorize(Policy = "AccountSession")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InvitationsController(IWorkspaceCollaboration team) : ControllerBase
{
    [HttpPost("accept"), EnableRateLimiting("Accounts")]
    public async Task<IActionResult> Accept(AcceptInvitationRequest request, CancellationToken ct) =>
        Ok(new { workspaceId = await team.AcceptAsync(request.Code, ct) });
}
public sealed class HttpCollaborationActor(IHttpContextAccessor http) : ICollaborationActor
{
    public string UserId => http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new CollaborationException(401, "Session requise.");
    public string Email => http.HttpContext?.User.FindFirstValue(ClaimTypes.Email) ?? throw new CollaborationException(401, "Session requise.");
}
