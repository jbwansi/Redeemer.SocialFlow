using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Api.Development.Visuals;
using Redeemer.SocialFlow.Infrastructure.Terminology;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Application.Terminology;
using Microsoft.EntityFrameworkCore;

namespace Redeemer.SocialFlow.Api.Controllers;

[ApiController, Route("api/workspace-terminology"), Authorize(Policy = "WorkspaceMember")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(VisualApiExceptionFilter)), RequestSizeLimit(64 * 1024)]
public sealed class WorkspaceTerminologyController(WorkspaceTerminologyService terms, SocialFlowDbContext db) : ControllerBase
{
    [HttpGet("/api/posts/{postId:guid}/terminology-check")]
    public async Task<IActionResult> Check(Guid postId, CancellationToken token)
    {
        var post = await db.SocialPosts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == postId, token);
        if (post is null) return NotFound();
        var catalog = await terms.ReadAsync(token);
        var checks = new[] { post.Title, post.Content, post.CallToAction, post.VisualBrief }
            .Select(x => TerminologyValidator.Check(x, catalog.Terms)).ToArray();
        return Ok(new { catalog.WorkspaceId, catalog.Fingerprint,
            Warnings = checks.SelectMany(x => x.Warnings).Distinct().ToArray(),
            Terms = checks.SelectMany(x => x.Terms).DistinctBy(x => x.Id).ToArray() });
    }
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token) => Ok(await terms.ListAsync(token));
    [HttpPost, Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Create(TermInput input, CancellationToken token) =>
        StatusCode(201, await terms.SaveAsync(null, input, Actor, token));
    [HttpPut("{id:guid}"), Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Update(Guid id, TermInput input, CancellationToken token) =>
        Ok(await terms.SaveAsync(id, input, Actor, token));
    [HttpPost("{id:guid}/validate"), Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Validate(Guid id, IdentityRevisionRequest input, CancellationToken token) =>
        Ok(await terms.ChangeStatusAsync(id, input.Revision, true, Actor, token));
    [HttpPost("{id:guid}/retire"), Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Retire(Guid id, IdentityRevisionRequest input, CancellationToken token) =>
        Ok(await terms.ChangeStatusAsync(id, input.Revision, false, Actor, token));
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
