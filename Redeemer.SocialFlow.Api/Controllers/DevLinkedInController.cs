using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Api.Development.LinkedIn;

namespace Redeemer.SocialFlow.Api.Controllers;

public sealed record LinkedInConnectionStatus(bool Connected, string State, DateTimeOffset? ExpiresAt);

[ApiController]
[Route("api/dev/linkedin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DevLinkedInController(LinkedInStateStore states, LinkedInTokenStore tokens,
    LinkedInOAuthClient client, IOptions<LinkedInOptions> options, TimeProvider clock, ILogger<DevLinkedInController> logger, IHostEnvironment environment) : ControllerBase
{
    private const string Cookie = "__Host-SocialFlow.LinkedIn.State";
    private static CookieOptions CookieSettings => new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = LinkedInStateStore.Lifetime };

    [HttpGet("connect")]
    public IActionResult Connect()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!Request.IsHttps) return Problem(statusCode: 400, title: "HTTPS is required.");
        if (!options.Value.IsConfigured)
        {
            LinkedInDiagnostics.Write(logger, environment, "Configuration", null, null, "LI_CONFIG_MISSING");
            return Problem(statusCode: 503, title: "LinkedIn is not configured.");
        }
        try
        {
            var flow = states.Create();
            Response.Cookies.Append(Cookie, flow.Browser, CookieSettings);
            return Redirect(QueryHelpers.AddQueryString("https://www.linkedin.com/oauth/v2/authorization", new Dictionary<string, string?>
            {
                ["response_type"] = "code", ["client_id"] = options.Value.ClientId,
                ["redirect_uri"] = LinkedInOptions.Callback, ["scope"] = LinkedInOptions.Scopes, ["state"] = flow.State
            }));
        }
        catch (Exception) { return Problem(statusCode: 503, title: "LinkedIn connection could not be started."); }
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? state, [FromQuery] string? code,
        [FromQuery] string? error, CancellationToken cancellationToken)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!Request.IsHttps || !states.TryConsume(state, Request.Cookies[Cookie]))
            return Problem(statusCode: 401, title: "Invalid or expired LinkedIn state. Start a new connection.");
        Response.Cookies.Delete(Cookie, CookieSettings);
        if (error is not null) return Problem(statusCode: 400, title: "LinkedIn authorization was refused or could not be completed.");
        if (string.IsNullOrWhiteSpace(code)) return Problem(statusCode: 400, title: "LinkedIn authorization code is missing.");
        if (!options.Value.IsConfigured)
        {
            LinkedInDiagnostics.Write(logger, environment, "Configuration", null, null, "LI_CONFIG_MISSING");
            return Problem(statusCode: 503, title: "LinkedIn is not configured.");
        }
        var storing = false;
        try
        {
            var connection = await client.ExchangeAsync(code, cancellationToken);
            storing = true;
            await tokens.SaveAsync(connection, cancellationToken);
            // Clear the code/state from the displayed URL. No provider data enters the redirect.
            return Redirect("/api/dev/linkedin/status");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception)
        {
            if (storing) LinkedInDiagnostics.Write(logger, environment, "ProtectedStorage", exception, null, "LI_STORAGE_SAVE");
            return Problem(statusCode: 502, title: "LinkedIn connection failed. Start a new connection.");
        }
    }

    [HttpGet("status")]
    [ProducesResponseType<LinkedInConnectionStatus>(200)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        try
        {
            var connection = await tokens.ReadAsync(cancellationToken);
            if (connection is null || connection.ClientId != options.Value.ClientId)
                return Ok(new LinkedInConnectionStatus(false, "disconnected", null));
            var active = connection.ExpiresAt > clock.GetUtcNow();
            return Ok(new LinkedInConnectionStatus(active, active ? "connected" : "expired", connection.ExpiresAt));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception) { return Problem(statusCode: 503, title: "LinkedIn connection status is unavailable."); }
    }
}
