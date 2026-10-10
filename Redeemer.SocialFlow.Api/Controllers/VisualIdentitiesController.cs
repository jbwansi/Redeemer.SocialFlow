using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Api.Development.Visuals;
using Redeemer.SocialFlow.Application.Visuals;

namespace Redeemer.SocialFlow.Api.Controllers;

public sealed record UpdateIdentityApiRequest(IdentityInput Identity, long Revision);
public sealed record IdentityRevisionRequest(long Revision);

[ApiController]
[Route("api/visual-identities")]
[ServiceFilter(typeof(LocalVisualAccessFilter))]
[TypeFilter(typeof(VisualApiExceptionFilter))]
[RequestSizeLimit(64 * 1024)]
public sealed class VisualIdentitiesController(VisualIdentityLibrary library) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LibraryIdentityDto>>> List(CancellationToken token) => Ok(await library.ListAsync(token));
    [HttpPost]
    public async Task<ActionResult<LibraryIdentityDto>> Create(IdentityInput input, CancellationToken token) =>
        StatusCode(201, await library.CreateAsync(input, token));
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LibraryIdentityDto>> Update(Guid id, UpdateIdentityApiRequest request, CancellationToken token) =>
        Ok(await library.UpdateAsync(id, request.Revision, request.Identity, token));
    [HttpPost("{id:guid}/default")]
    public async Task<ActionResult<LibraryIdentityDto>> Default(Guid id, IdentityRevisionRequest request, CancellationToken token) =>
        Ok(await library.DefaultAsync(id, request.Revision, token));
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromBody] IdentityRevisionRequest request, CancellationToken token)
    { await library.DeleteAsync(id, request.Revision, token); return NoContent(); }
    [HttpPost("{id:guid}/logo")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 21 * 1024 * 1024)]
    public async Task<ActionResult<LibraryIdentityDto>> UploadLogo(Guid id, [FromForm] long revision, IFormFile? file, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(file);
        await using var stream = file.OpenReadStream();
        return Ok(await library.UploadLogoAsync(id, revision, stream, token));
    }
    [HttpGet("{id:guid}/logos/{logoId:guid}")]
    public async Task<IActionResult> Logo(Guid id, Guid logoId, CancellationToken token)
    {
        var logo = await library.ReadLogoAsync(id, logoId, token);
        return File(logo.Bytes, logo.ContentType);
    }
}
