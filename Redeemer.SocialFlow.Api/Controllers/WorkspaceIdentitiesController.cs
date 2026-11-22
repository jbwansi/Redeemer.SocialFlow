using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Api.Development.Visuals;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Infrastructure.Visuals;

namespace Redeemer.SocialFlow.Api.Controllers;

[ApiController, Route("api/workspace-identities"), Authorize(Policy = "WorkspaceMember")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(VisualApiExceptionFilter)), RequestSizeLimit(64 * 1024)]
public sealed class WorkspaceIdentitiesController(VisualIdentityLibrary library, WorkspaceIdentityService identities,
    IWorkspaceAccessContext workspace) : ControllerBase
{
    [HttpGet("editorial")]
    public async Task<IActionResult> Editorial(CancellationToken token) =>
        Ok(new { workspace.WorkspaceId, Profile = await identities.ReadAsync(token) });

    [HttpPut("editorial"), Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> SaveEditorial(EditorialProfileInput input, CancellationToken token) =>
        Ok(await identities.SaveAsync(input, token));

    [HttpGet("visual")]
    public async Task<IActionResult> List(CancellationToken token)
    {
        var result = new List<object>();
        foreach (var item in await library.ListAsync(token))
            result.Add(new { item.Id, item.IdentityId, item.Name, item.Style, item.Colors, item.Slogan,
                item.Instructions, item.IsActive, item.IsDefault, item.Revision, item.LogoId,
                workspace.WorkspaceId, ApprovalId = await identities.ApprovalAsync(item.Id, item.Revision, token) });
        return Ok(result);
    }
    [HttpPost("visual"), Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Create(IdentityInput input, CancellationToken token) =>
        StatusCode(201, await library.CreateAsync(input, token));

    [HttpPut("visual/{id:guid}"), Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Update(Guid id, UpdateIdentityApiRequest request, CancellationToken token) =>
        Ok(await library.UpdateAsync(id, request.Revision, request.Identity, token));

    [HttpPost("visual/{id:guid}/default"), Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Default(Guid id, IdentityRevisionRequest request, CancellationToken token) =>
        Ok(await library.DefaultAsync(id, request.Revision, token));

    [HttpDelete("visual/{id:guid}"), Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Delete(Guid id, IdentityRevisionRequest request, CancellationToken token)
    { await library.DeleteAsync(id, request.Revision, token); return NoContent(); }

    [HttpPost("visual/{id:guid}/approve"), Authorize(Policy = "WorkspaceManageSocial")]
    public async Task<IActionResult> Approve(Guid id, IdentityRevisionRequest request, CancellationToken token) =>
        Ok(new { ApprovalId = await identities.ApproveAsync(id, request.Revision, User.FindFirstValue(ClaimTypes.NameIdentifier)!, token) });

    [HttpPost("visual/{id:guid}/logo"), Authorize(Policy = "WorkspaceManageSocial")]
    [RequestSizeLimit(21 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 21 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(Guid id, [FromForm] long revision, IFormFile file, CancellationToken token)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await library.UploadLogoAsync(id, revision, stream, token));
    }
    [HttpGet("visual/{id:guid}/logos/{logoId:guid}")]
    public async Task<IActionResult> Logo(Guid id, Guid logoId, CancellationToken token)
    {
        var logo = await library.ReadLogoAsync(id, logoId, token);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(logo.Bytes, logo.ContentType);
    }
}
