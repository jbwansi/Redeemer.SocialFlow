using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Security.Claims;
using Redeemer.SocialFlow.Api.Development.Visuals;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Infrastructure.Visuals;

namespace Redeemer.SocialFlow.Api.Controllers;

public sealed record GenerateVisualApiRequest(string IdentityId, string Name, string Style, string[] Colors,
    string? Slogan = null, string? VisualBrief = null, string? Instructions = null, Guid? LibraryId = null, long? Revision = null,
    string? ConceptToken = null, string? IdentityMode = null);

[ApiController]
[Route("api/posts/{postId:guid}/visuals")]
[ServiceFilter(typeof(LocalVisualAccessFilter))]
[TypeFilter(typeof(VisualApiExceptionFilter))]
public sealed class VisualsController(PostVisualService visuals) : PostVisualActions(visuals) { }

[ApiController]
[Route("api/posts/{postId:guid}/workspace-visuals")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = "WorkspaceMember")]
[TypeFilter(typeof(VisualApiExceptionFilter))]
public sealed class WorkspaceVisualsController(PostVisualService visuals) : PostVisualActions(visuals)
{ }

public abstract class PostVisualActions(PostVisualService visuals) : ControllerBase
{
    private ITimeLimitedDataProtector ConceptProtector => HttpContext.RequestServices
        .GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("VisualConcept", VisualConcept.Version, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "local")
        .ToTimeLimitedDataProtector();

    private async Task<VisualIdentity> ResolveIdentity(GenerateVisualApiRequest request, CancellationToken token)
    {
        var identity = new VisualIdentity(request.IdentityId, request.Name, request.Style, request.Colors, request.Slogan, request.Instructions);
        if (this is WorkspaceVisualsController)
        {
            if (request.IdentityMode == "Approved" && request.LibraryId is Guid approvedId && request.Revision is long approvedRevision)
                return await HttpContext.RequestServices.GetRequiredService<WorkspaceIdentityService>()
                    .ResolveApprovedAsync(approvedId, approvedRevision, identity, token);
            if (request.IdentityMode == "Temporary" && request.LibraryId is null && request.Revision is null)
                return identity;
            throw new ArgumentException("Choose an approved identity or explicitly authorize temporary settings.");
        }
        if (request.LibraryId is Guid libraryId)
        {
            if (request.Revision is not long revision) throw new ArgumentException("Identity revision is required.");
            identity = await HttpContext.RequestServices.GetRequiredService<VisualIdentityLibrary>()
                .ResolveAsync(libraryId, revision, identity, token);
        }
        return identity;
    }

    [HttpPost("concept")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Prepare(Guid postId, GenerateVisualApiRequest request, CancellationToken token)
    {
        var identity = await ResolveIdentity(request, token);
        var concept = await visuals.PrepareAsync(postId, identity, request.VisualBrief, token);
        return Ok(new { concept.Subject, concept.Message, concept.Scene, concept.Brief, concept.Style, concept.Exclusions,
            Warnings = concept.Terminology?.Warnings ?? Array.Empty<string>(), concept.Terminology,
            IdentityName = identity.Name, IdentityStatus = identity.ApprovalId.HasValue ? "Approved" : "Temporary",
            identity.LibraryId, identity.Revision, identity.ApprovalId,
            ConceptToken = ConceptProtector.Protect(concept.Fingerprint, TimeSpan.FromMinutes(30)) });
    }

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
        if (string.IsNullOrWhiteSpace(request.ConceptToken)) throw new VisualConceptException("VisualConceptRequired");
        string fingerprint;
        try { fingerprint = ConceptProtector.Unprotect(request.ConceptToken); }
        catch (CryptographicException) { throw new VisualConceptException("VisualConceptExpiredOrInvalid"); }
        var identity = await ResolveIdentity(request, token);
        var current = await visuals.PrepareAsync(postId, identity, request.VisualBrief, token);
        if (current.Fingerprint != fingerprint)
        {
            throw new VisualConceptException("VisualConceptStale");
        }
        var generator = HttpContext.RequestServices.GetRequiredService<IVisualGenerator>();
        var visual = await visuals.GenerateAsync(postId, identity, request.VisualBrief, generator, token, fingerprint);
        return CreatedAtAction(nameof(Get), new { postId, visualId = visual.Id }, visual);
    }
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VisualDto>>> List(Guid postId, CancellationToken token) =>
        Ok(await visuals.ListAsync(postId, token));
    [HttpGet("{visualId:guid}")]
    public async Task<IActionResult> Get(Guid postId, Guid visualId, CancellationToken token)
    {
        var content = await visuals.ReadAsync(postId, visualId, token);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
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
