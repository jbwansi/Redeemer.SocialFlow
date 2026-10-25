using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Enums;
namespace Redeemer.SocialFlow.Api.Controllers;

public sealed record StartSocialRequest(SocialPlatform Platform);
public sealed record SelectSocialRequest([Required, MinLength(1), MaxLength(100)] string[] ExternalAccountIds);
public sealed record PostDestinationsRequest([Required, MaxLength(100)] Guid[] ConnectionIds);
[ApiController, Route("api/workspaces/{workspaceId:guid}/social-connections"), Authorize(Policy="AccountSession")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class SocialConnectionsController(ISocialConnections connections) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(Guid workspaceId,CancellationToken ct) => Ok(await connections.ListAsync(workspaceId,ct));
    [HttpGet("providers")] public async Task<IActionResult> Providers(Guid workspaceId,CancellationToken ct) => Ok(await connections.ProvidersAsync(workspaceId,ct));
    [HttpPost("start"),EnableRateLimiting("Accounts")]
    public async Task<IActionResult> Start(Guid workspaceId,StartSocialRequest request,CancellationToken ct) => Ok(new { authorizationUrl=await connections.StartAsync(workspaceId,request.Platform,ct) });
    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> Candidates(Guid workspaceId,Guid sessionId,CancellationToken ct) => Ok(await connections.CandidatesAsync(workspaceId,sessionId,ct));
    [HttpPost("sessions/{sessionId:guid}/select")]
    public async Task<IActionResult> Select(Guid workspaceId,Guid sessionId,SelectSocialRequest request,CancellationToken ct)
    { await connections.SelectAsync(workspaceId,sessionId,request.ExternalAccountIds,ct); return NoContent(); }
    [HttpPost("{id:guid}/disconnect")]
    public async Task<IActionResult> Disconnect(Guid workspaceId,Guid id,RevisionRequest request,CancellationToken ct)
    { await connections.DisconnectAsync(workspaceId,id,request.Revision,ct); return NoContent(); }
    [HttpPost("{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid workspaceId,Guid id,RevisionRequest request,CancellationToken ct)
    { await connections.ValidateAsync(workspaceId,id,request.Revision,ct); return NoContent(); }
    [HttpGet("posts/{postId:guid}/destinations")]
    public async Task<IActionResult> Destinations(Guid workspaceId,Guid postId,CancellationToken ct) => Ok(await connections.DestinationsAsync(workspaceId,postId,ct));
    [HttpPut("posts/{postId:guid}/destinations")]
    public async Task<IActionResult> SetDestinations(Guid workspaceId,Guid postId,PostDestinationsRequest request,CancellationToken ct)
    { await connections.SetDestinationsAsync(workspaceId,postId,request.ConnectionIds,ct); return NoContent(); }
}

[ApiController,Route("api/social-oauth/{platform:int}/callback"),Authorize(Policy="AccountSession")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class SocialOAuthCallbackController(ISocialConnections connections) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Callback(SocialPlatform platform,CancellationToken ct)
    {
        Response.Headers["Referrer-Policy"]="no-referrer";
        var values=HttpContext.Items["social-oauth-query"] as IQueryCollection ?? Request.Query;
        if(values["state"].Count!=1 || values["code"].Count>1) return Redirect("/#social-connections?oauth=failed");
        try {
            var session=await connections.CallbackAsync(platform,values["state"].ToString(),values["code"].FirstOrDefault(),values.ContainsKey("error"),ct);
            // Only an opaque discovery ID enters the SPA, never codes, state, or tokens.
            return Redirect("/#social-connections?session="+session.SessionId+"&workspace="+session.WorkspaceId);
        }
        catch(SocialConnectionException) { return Redirect("/#social-connections?oauth=failed"); }
    }
}
