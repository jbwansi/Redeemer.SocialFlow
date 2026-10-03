using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Api.Contracts;
using Redeemer.SocialFlow.Application.Knowledge;

namespace Redeemer.SocialFlow.Api.Controllers;

[ApiController]
[Route("api/dev/knowledge/documents")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class DevKnowledgeDocumentsController(IKnowledgeDocumentService documents) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<KnowledgeDocumentDetailsDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<KnowledgeDocumentDetailsDto>> Create(
        [FromBody] CreateKnowledgeDocumentHttpRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.Chunks.Any(chunk => chunk is null))
            return Problem(statusCode: 400, title: "Invalid request", detail: "A document and non-null chunks are required.");
        var result = await documents.CreateAsync(new(request.Title, request.PrimaryTheme, request.Themes,
            request.Usages, request.SourceType!.Value, request.AuthorityLevel!.Value, request.Language,
            request.Chunks.Select(chunk => new CreateKnowledgeChunkRequest(chunk.Content, chunk.ChunkIndex!.Value,
                chunk.PageNumber, chunk.Section)).ToArray(), request.Version, request.IsActive), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Document.Id }, result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<KnowledgeDocumentDetailsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<KnowledgeDocumentDetailsDto>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await documents.GetByIdAsync(id, cancellationToken) ?? throw new KnowledgeDocumentNotFoundException(id));

    [HttpGet]
    [ProducesResponseType<KnowledgeDocumentPage>(StatusCodes.Status200OK)]
    public async Task<ActionResult<KnowledgeDocumentPage>> List(
        [FromQuery] ListKnowledgeDocumentsHttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await documents.ListAsync(new(request.PageNumber, request.PageSize,
                request.Status, request.Theme, request.Language), cancellationToken));
        }
        catch (ArgumentException exception) when (exception.ParamName == "request")
        {
            return Problem(statusCode: 400, title: "Invalid request", detail: "The pagination or document filters are invalid.");
        }
    }

    [HttpPost("{id}/approve")]
    [ProducesResponseType<KnowledgeDocumentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<KnowledgeDocumentDto>> Approve(Guid id, CancellationToken cancellationToken) =>
        Ok(await documents.ApproveAsync(id, cancellationToken));

    [HttpPut("{id}/active")]
    [ProducesResponseType<KnowledgeDocumentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<KnowledgeDocumentDto>> SetActive(Guid id,
        [FromBody] SetKnowledgeDocumentActiveHttpRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
            return Problem(statusCode: 400, title: "Invalid request", detail: "An activation request is required.");
        return Ok(await documents.SetActiveAsync(id, request.IsActive!.Value, cancellationToken));
    }
}
