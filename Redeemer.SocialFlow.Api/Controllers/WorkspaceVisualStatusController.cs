using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Infrastructure.AI;

namespace Redeemer.SocialFlow.Api.Controllers;

// Independent of the optional action controller and storage/provider construction.
[ApiController, Route("api/posts/workspace-visuals/status")]
[Authorize(Policy = "WorkspaceMember")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceVisualStatusController(IConfiguration configuration,
    IOptions<OpenAIVisualGeneratorOptions> imageOptions) : ControllerBase
{
    [HttpGet]
    public IActionResult Status()
    {
        var enabled = configuration.GetValue<bool>("Visuals:WorkspaceEnabled");
        var generationEnabled = false;
        if (enabled)
        {
            try { _ = imageOptions.Value; generationEnabled = true; }
            catch (OptionsValidationException) { /* No values or validation messages leave the server. */ }
        }
        return Ok(new
        {
            enabled, uploadEnabled = enabled, generationEnabled,
            reason = !enabled ? "WorkspaceVisualsDisabled" : !generationEnabled ? "ImageGenerationNotConfigured" : null
        });
    }
}
