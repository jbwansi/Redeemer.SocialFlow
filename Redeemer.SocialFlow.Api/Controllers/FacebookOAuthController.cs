using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Api.Development.Facebook;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
namespace Redeemer.SocialFlow.Api.Controllers;
[ApiController]
[Route("api/dev/facebook")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FacebookOAuthController(FacebookFlows flows, FacebookGraphClient graph,
    IFacebookConnectionStore connections, IOptions<FacebookOptions> options, ILogger<FacebookOAuthController> logger) : ControllerBase
{
    private const string Cookie = "__Host-SocialFlow.Facebook.State";
    private static CookieOptions CookieOptions => new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = LinkedInStateStore.Lifetime };
    [AllowAnonymous, HttpGet("authorize")]
    public IActionResult Authorize([FromQuery] string? ticket)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!FacebookFlows.LocalHttps(HttpContext)) return StatusCode(403);
        if (!options.Value.Configured || !flows.ConsumeTicket(ticket)) return Unauthorized();
        var business = options.Value.LoginConfigId is not null;
        logger.LogInformation("Facebook OAuth authorization: Stage=authorize; AuthorizationFlow={AuthorizationFlow}; ConfigIdPresent={ConfigIdPresent}", business ? "business_configuration" : "scope_based", business);
        var flow = flows.States.Create(); Response.Cookies.Append(Cookie, flow.Browser, CookieOptions);
        var parameters = new Dictionary<string,string?> {
            ["client_id"] = options.Value.AppId, ["redirect_uri"] = FacebookOptions.Callback,
            ["response_type"] = "code", ["state"] = flow.State };
        if (business) parameters["config_id"] = options.Value.LoginConfigId;
        else parameters["scope"] = string.Join(',', FacebookOptions.RequiredScopes);
        return Redirect(QueryHelpers.AddQueryString("https://www.facebook.com/" + FacebookOptions.Version + "/dialog/oauth", parameters));
    }
    [AllowAnonymous, HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? state, [FromQuery] string? code, [FromQuery] string? error, CancellationToken token)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!FacebookFlows.LocalHttps(HttpContext)) return StatusCode(403);
        if (!flows.States.TryConsume(state, Request.Cookies[Cookie])) return Unauthorized();
        Response.Cookies.Delete(Cookie, CookieOptions);
        if (error is not null || string.IsNullOrWhiteSpace(code)) return Problem(statusCode:400, title:"Autorisation Facebook refusée ou incomplète.");
        var stage = "meta_oauth";
        try
        {
            var connection = await graph.ExchangeAsync(code, token);
            logger.LogInformation("Facebook OAuth: Stage=meta_oauth; OAuthCompleted=true; PagesCount={PagesCount}", connection.Pages.Length);
            stage = "local_storage";
            await connections.SaveAsync(connection, token);
            logger.LogInformation("Facebook OAuth: Stage=local_storage; ConnectionPersisted=true; PagesCount={PagesCount}", connection.Pages.Length);
        }
        catch (Exception failure)
        {
            var category = stage == "local_storage" ? "storage" : FacebookGraphClient.FailureCategory(failure, token);
            logger.LogWarning("Facebook callback failed: Stage={Stage}; Category={Category}; ExceptionType={ExceptionType}", stage, category, failure.GetType().Name);
            return Problem(statusCode:502, title:"Connexion Facebook impossible. Consultez le diagnostic OAuth dans la sortie de l’API puis reconnectez-vous.");
        }
        // Fixed local landing page; no token, code, external error or state in the destination.
        return Redirect("http://localhost:5173/#local");
    }
}
