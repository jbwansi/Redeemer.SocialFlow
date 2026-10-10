using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Api.Development.Visuals;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Visuals;

namespace Redeemer.SocialFlow.Api.Controllers;

public sealed record GenerateVisualApiRequest(string IdentityId, string Name, string Style, string[] Colors,
    string? Slogan = null, string? VisualBrief = null);

[ApiController]
[Route("api/posts/{postId:guid}/visuals")]
[ServiceFilter(typeof(LocalVisualAccessFilter))]
[TypeFilter(typeof(VisualApiExceptionFilter))]
public sealed class VisualsController(PostVisualService visuals) : ControllerBase
{
    [HttpPost("upload")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 21 * 1024 * 1024)]
    public async Task<ActionResult<VisualDto>> Upload(Guid postId, IFormFile? file, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(file);
        await using var input = file.OpenReadStream();
        // Filename, extension and caller MIME are deliberately not trusted.
        var visual = await visuals.UploadAsync(postId, input, token);
        return CreatedAtAction(nameof(Get), new { postId, visualId = visual.Id }, visual);
    }
    [HttpPost("generate")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<ActionResult<VisualDto>> Generate(Guid postId, GenerateVisualApiRequest? request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var identity = new VisualIdentity(request.IdentityId, request.Name, request.Style, request.Colors, request.Slogan);
        var generator = HttpContext.RequestServices.GetRequiredService<IVisualGenerator>();
        var visual = await visuals.GenerateAsync(postId, identity, request.VisualBrief, generator, token);
        return CreatedAtAction(nameof(Get), new { postId, visualId = visual.Id }, visual);
    }
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VisualDto>>> List(Guid postId, CancellationToken token) =>
        Ok(await visuals.ListAsync(postId, token));
    [HttpGet("{visualId:guid}")]
    public async Task<IActionResult> Get(Guid postId, Guid visualId, CancellationToken token)
    {
        var content = await visuals.ReadAsync(postId, visualId, token);
        return File(content.Bytes, content.ContentType); // Inline preview through this protected route only.
    }
    [HttpPost("{visualId:guid}/select")]
    public async Task<ActionResult<VisualDto>> Select(Guid postId, Guid visualId, CancellationToken token) =>
        Ok(await visuals.SelectAsync(postId, visualId, token));
    [HttpDelete("{visualId:guid}")]
    public async Task<IActionResult> Delete(Guid postId, Guid visualId, CancellationToken token)
    {
        await visuals.DeleteAsync(postId, visualId, token);
        return NoContent();
    }
}
