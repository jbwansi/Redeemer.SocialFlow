using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Redeemer.SocialFlow.Application.Accounts;

namespace Redeemer.SocialFlow.Api.Controllers;

public sealed record EmailRequest([Required, EmailAddress, StringLength(254)] string Email);
public sealed record LoginRequest([Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128)] string Password);
public sealed record RegisterRequest([Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 12), RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\p{L}\p{Nd}]).+$")] string Password);
public sealed record TokenRequest([Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(4096)] string Code);
public sealed record ResetRequest([Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(4096)] string Code,
    [Required, StringLength(128, MinimumLength = 12), RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\p{L}\p{Nd}]).+$")] string Password);

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(16 * 1024)]
public sealed class AuthController(IAccountService accounts, IAntiforgery antiforgery) : ControllerBase
{
    [AllowAnonymous, HttpGet("csrf")]
    public IActionResult Csrf() => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [AllowAnonymous, HttpPost("register"), EnableRateLimiting("Accounts")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken token)
    { await accounts.RegisterAsync(request.Email, request.Password, token); return AcceptedMessage(); }

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("Accounts")]
    public async Task<IActionResult> Login(LoginRequest request) =>
        await accounts.LoginAsync(request.Email, request.Password) ? NoContent() :
            Problem(statusCode: 401, title: "Connexion impossible. Vérifiez vos informations et la confirmation de votre adresse.");

    [Authorize(Policy = "AccountSession"), HttpPost("logout")]
    public async Task<IActionResult> Logout() { await accounts.LogoutAsync(); return NoContent(); }

    [Authorize(Policy = "AccountSession"), HttpGet("me")]
    public async Task<IActionResult> Me() => await accounts.CurrentAsync() is { } account ? Ok(account) : Unauthorized();

    [AllowAnonymous, HttpPost("forgot-password"), EnableRateLimiting("Accounts")]
    public async Task<IActionResult> Forgot(EmailRequest request, CancellationToken token)
    { await accounts.SendRecoveryAsync(request.Email, false, token); return AcceptedMessage(); }

    [AllowAnonymous, HttpPost("resend-confirmation"), EnableRateLimiting("Accounts")]
    public async Task<IActionResult> Resend(EmailRequest request, CancellationToken token)
    { await accounts.SendRecoveryAsync(request.Email, true, token); return AcceptedMessage(); }

    [AllowAnonymous, HttpPost("confirm-email"), EnableRateLimiting("Accounts")]
    public async Task<IActionResult> Confirm(TokenRequest request) =>
        await accounts.ConfirmAsync(request.Email, request.Code) ? NoContent() : InvalidLink();

    [AllowAnonymous, HttpPost("reset-password"), EnableRateLimiting("Accounts")]
    public async Task<IActionResult> Reset(ResetRequest request) =>
        await accounts.ResetAsync(request.Email, request.Code, request.Password) ? NoContent() : InvalidLink();

    private ObjectResult AcceptedMessage() => StatusCode(202, new { message = "Si cette demande est applicable, un email vous sera envoyé. Consultez également vos courriers indésirables." });
    private ObjectResult InvalidLink() => Problem(statusCode: 400, title: "Lien invalide ou expiré. Demandez un nouveau lien.");
}
