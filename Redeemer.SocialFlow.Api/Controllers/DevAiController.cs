using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Api.Contracts;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Knowledge;
using Redeemer.SocialFlow.Api.Errors;

namespace Redeemer.SocialFlow.Api.Controllers;

[ApiController]
[Route("api/dev/ai")]
public sealed class DevAiController(IServiceProvider services, ILogger<DevAiController> logger) : ControllerBase
{
    [HttpPost("generate")]
    [EndpointSummary("Generate content without saving a post")]
    [ProducesResponseType<GeneratedContent>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> Generate([FromBody] DevelopmentGenerateRequest? request, CancellationToken cancellationToken)
    {
        if (!IsValid(request))
            return BadRequest();

        try
        {
            var generator = services.GetRequiredService<IContentGenerator>();
            return Ok((await generator.GenerateAsync(ToInternalRequest(request!), cancellationToken)).Content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            DevelopmentAiDiagnostics.Log(logger, exception);
            return Problem("Content generation failed.");
        }
    }

    [HttpPost("generate-grounded")]
    [EndpointSummary("Generate content from knowledge passages without saving a post")]
    [ProducesResponseType<GroundedContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> GenerateGrounded([FromBody] DevelopmentGenerateRequest? request, CancellationToken cancellationToken)
    {
        if (!IsValid(request))
            return BadRequest();

        try
        {
            var generator = services.GetRequiredService<IGenerateGroundedContent>();
            return Ok(await generator.ExecuteAsync(ToInternalRequest(request!), cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            DevelopmentAiDiagnostics.Log(logger, exception);
            return Problem("Content generation failed.");
        }
    }

    private static GenerateContentRequest ToInternalRequest(DevelopmentGenerateRequest request) =>
        new(request.Subject, request.Objective, request.Audience, request.Platform);

    private static bool IsValid(DevelopmentGenerateRequest? request) => request is not null &&
        !string.IsNullOrWhiteSpace(request.Subject) &&
        !string.IsNullOrWhiteSpace(request.Objective) &&
        !string.IsNullOrWhiteSpace(request.Audience) &&
        Enum.IsDefined(request.Platform);
}
