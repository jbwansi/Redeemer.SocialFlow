using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Redeemer.SocialFlow.Application.Accounts;

namespace Redeemer.SocialFlow.Api.Controllers;

public sealed record CreateWorkspaceRequest([Required, StringLength(120, MinimumLength = 1)] string Name);

[ApiController, Route("api/workspaces"), Authorize(Policy = "AccountSession")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspacesController(IWorkspaceDirectory directory) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token) => Ok(await directory.ListAsync(UserId, token));
    [HttpPost("personal"), EnableRateLimiting("Accounts")]
    public async Task<IActionResult> Personal(CancellationToken token) => Ok(await directory.EnsurePersonalAsync(UserId, token));
    [HttpPost, EnableRateLimiting("Accounts")]
    public async Task<IActionResult> Create(CreateWorkspaceRequest request, CancellationToken token)
    {
        var workspace = await directory.CreateOrganizationAsync(UserId, request.Name, token);
        return CreatedAtAction(nameof(Get), new { id = workspace.Id }, workspace);
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken token) =>
        await directory.GetAsync(UserId, id, token) is { } workspace ? Ok(workspace) : Problem(statusCode: 404, title: "Espace inaccessible.");
    // Selection is acknowledged by the server. Every following resource request rechecks membership.
    [HttpPost("{id:guid}/select")]
    public async Task<IActionResult> Select(Guid id, CancellationToken token) =>
        await directory.GetAsync(UserId, id, token) is { } workspace ? Ok(workspace) : Problem(statusCode: 404, title: "Espace inaccessible.");
}
