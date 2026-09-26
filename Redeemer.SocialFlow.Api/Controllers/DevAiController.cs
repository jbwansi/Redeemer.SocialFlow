using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Application.AI;
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
    public async Task<IActionResult> Generate([FromBody] GenerateContentRequest? request, CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Subject) ||
            string.IsNullOrWhiteSpace(request.Objective) ||
            string.IsNullOrWhiteSpace(request.Audience) ||
            !Enum.IsDefined(request.Platform))
            return BadRequest();

        try
        {
            var generator = services.GetRequiredService<IContentGenerator>();
            return Ok(await generator.GenerateAsync(request, cancellationToken));
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
}
