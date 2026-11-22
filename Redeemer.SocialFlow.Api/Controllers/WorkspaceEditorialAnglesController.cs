using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Api.Development.Visuals;
using Redeemer.SocialFlow.Infrastructure.Editorial;
namespace Redeemer.SocialFlow.Api.Controllers;

[ApiController, Route("api/workspace-editorial-angles"), Authorize(Policy = "WorkspaceMember")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(VisualApiExceptionFilter)), RequestSizeLimit(32 * 1024)]
public sealed class WorkspaceEditorialAnglesController(WorkspaceEditorialAngleService angles) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token, [FromQuery] bool activeOnly = false) => Ok(await angles.ListAsync(activeOnly, token));
    [HttpPost, Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Create(AngleInput input, CancellationToken token) => StatusCode(201, await angles.SaveAsync(null, input, Actor, token));
    [HttpPut("{id:guid}"), Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Update(Guid id, AngleInput input, CancellationToken token) => Ok(await angles.SaveAsync(id, input, Actor, token));
    [HttpPost("initialize"), Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Initialize(CancellationToken token) { await angles.InitializeAsync(Actor, token); return NoContent(); }
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
