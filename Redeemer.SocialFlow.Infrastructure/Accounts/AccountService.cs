using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Redeemer.SocialFlow.Application.Accounts;

namespace Redeemer.SocialFlow.Infrastructure.Accounts;

public sealed class LoginTimingGuard
{
    private readonly PasswordHasher<ApplicationUser> hasher = new();
    private readonly ApplicationUser user = new();
    private readonly string hash;
    public LoginTimingGuard() => hash = hasher.HashPassword(user, Guid.NewGuid().ToString("N"));
    public void Verify(string password) => hasher.VerifyHashedPassword(user, hash, password);
}

public sealed class AccountService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    IHttpContextAccessor http, IAccountEmailSender mail, IConfiguration configuration,
    LoginTimingGuard timing, ILogger<AccountService> logger, IWorkspaceDirectory? workspaces = null) : IAccountService
{
    public async Task RegisterAsync(string email, string password, CancellationToken token)
    {
        email = email.Trim();
        if (await users.FindByEmailAsync(email) is not null) return;
        var user = new ApplicationUser { UserName = email, Email = email };
        IdentityResult result;
        try { result = await users.CreateAsync(user, password); }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        { return; } // Concurrent unique constraint violation: same public response.
        if (result.Succeeded) await SendAsync(user, true, token);
    }
    public async Task<bool> LoginAsync(string email, string password)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null) { timing.Verify(password); return false; }
        var result = await signIn.PasswordSignInAsync(user, password, false, lockoutOnFailure: true);
        return result.Succeeded;
    }
    public async Task LogoutAsync()
    {
        var user = await users.GetUserAsync(http.HttpContext!.User);
        // Invalidate replay of this cookie as well as other sessions for this account.
        if (user is not null && !(await users.UpdateSecurityStampAsync(user)).Succeeded)
            throw new InvalidOperationException("Session revocation could not be completed.");
        await http.HttpContext!.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
    public async Task<AccountProfile?> CurrentAsync()
    {
        var user = await users.GetUserAsync(http.HttpContext!.User);
        return user is { EmailConfirmed: true } ? new(user.Id, user.Email!) : null;
    }
    public async Task SendRecoveryAsync(string email, bool confirmation, CancellationToken token)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null || (confirmation ? user.EmailConfirmed : !user.EmailConfirmed)) return;
        await SendAsync(user, confirmation, token);
    }
    private async Task SendAsync(ApplicationUser user, bool confirmation, CancellationToken token)
    {
        var code = confirmation ? await users.GenerateEmailConfirmationTokenAsync(user)
            : await users.GeneratePasswordResetTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var origin = configuration["Authentication:PublicOrigin"];
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Authentication public HTTPS origin must be configured.");
        var purpose = confirmation ? "confirm-email" : "reset-password";
        // Fragment prevents credentials from entering server access logs/referrers.
        var link = uri.GetLeftPart(UriPartial.Authority) + "/#" + purpose + "?email=" +
            Uri.EscapeDataString(user.Email!) + "&code=" + Uri.EscapeDataString(encoded);
        try { await mail.SendAsync(user.Email!, purpose, link, token); }
        catch (Exception) { logger.LogWarning("ACCOUNT_EMAIL_DELIVERY_FAILED"); }
    }
    private static string? Decode(string code)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
        catch (FormatException) { return null; }
    }
    public async Task<bool> ConfirmAsync(string email, string code)
    {
        var decoded = Decode(code);
        var user = await users.FindByEmailAsync(email.Trim());
        if (decoded is null || user is not { EmailConfirmed: false } || !(await users.ConfirmEmailAsync(user, decoded)).Succeeded) return false;
        // Identity lives in a separate database. Provisioning is idempotent and can be retried
        // through POST /workspaces/personal if the business database is not yet available.
        if (workspaces is not null)
        {
            try { await workspaces.EnsurePersonalAsync(user.Id); }
            catch (Exception) { logger.LogWarning("PERSONAL_WORKSPACE_PROVISIONING_PENDING"); }
        }
        return true;
    }
    public async Task<bool> ResetAsync(string email, string code, string password)
    {
        var decoded = Decode(code);
        var user = await users.FindByEmailAsync(email.Trim());
        return decoded is not null && user is { EmailConfirmed: true } &&
            (await users.ResetPasswordAsync(user, decoded, password)).Succeeded;
    }
}
