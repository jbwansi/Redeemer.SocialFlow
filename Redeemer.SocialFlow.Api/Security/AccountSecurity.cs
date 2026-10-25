using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Api.Development.Visuals;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Infrastructure.Accounts;

namespace Redeemer.SocialFlow.Api.Security;

public static class AccountSecurity
{
    public const string LocalScheme = "LocalAdministration";
    public static void AddAccountSecurity(this IServiceCollection services, IConfiguration config)
    {
        if (!Uri.TryCreate(config["Authentication:PublicOrigin"], UriKind.Absolute, out var origin) ||
            origin.Scheme != "https" || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" ||
            origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidOperationException("Authentication public HTTPS origin must be configured.");
        services.AddDbContext<AccountDbContext>(options => options.UseSqlite(
            config.GetConnectionString("Accounts") ?? "Data Source=socialflow-accounts.db"));
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.User.AllowedUserNameCharacters = string.Empty;
            options.SignIn.RequireConfirmedEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequiredUniqueChars = 4;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<AccountDbContext>().AddSignInManager().AddDefaultTokenProviders();
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = "__Host-SocialFlow.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = false;
                options.Events.OnValidatePrincipal = async context =>
                {
                    var manager = context.HttpContext.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();
                    var user = await manager.ValidateSecurityStampAsync(context.Principal);
                    if (user is not { EmailConfirmed: true })
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    }
                };
                options.Events.OnRedirectToLogin = context => WriteProblem(context.Response, 401, "Session requise ou expirée.");
                options.Events.OnRedirectToAccessDenied = context => WriteProblem(context.Response, 403, "Accès non autorisé.");
            })
            .AddScheme<AuthenticationSchemeOptions, LocalAdministrationHandler>(LocalScheme, _ => { });
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
        services.AddAuthorization(options =>
        {
            // No ordinary Identity account may access historical global business data.
            var legacy = new AuthorizationPolicyBuilder(IdentityConstants.ApplicationScheme, LocalScheme)
                .RequireAuthenticatedUser().RequireClaim("socialflow:local-admin", "true").Build();
            options.DefaultPolicy = legacy;
            options.FallbackPolicy = legacy;
            options.AddPolicy("AccountSession", new AuthorizationPolicyBuilder(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser().Build());
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-SocialFlow.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddScoped<AccountCsrfFilter>();
        services.AddSingleton(new LocalAdministrationAccess(Environment.GetEnvironmentVariable("SOCIALFLOW_VISUAL_ADMIN_SECRET"),
            config["OpenAI:ApiKey"], config["OpenAI:Images:ApiKey"], config["Facebook:AppSecret"], config["LinkedIn:ClientSecret"]));
        services.AddHttpContextAccessor();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IAccountEmailSender, AccountEmailSender>();
        services.AddSingleton<LoginTimingGuard>();
        services.AddScoped<IWorkspaceAccessContext, UnresolvedWorkspaceAccessContext>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.OnRejected = (context, token) => new ValueTask(WriteProblem(context.HttpContext.Response, 429,
                "Trop de tentatives. Réessayez plus tard."));
            options.AddPolicy("Accounts", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 20, Window = TimeSpan.FromMinutes(15), QueueLimit = 0, AutoReplenishment = true }));
        });
    }
    public static Task WriteProblem(HttpResponse response, int status, string title)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title },
            options: null, contentType: "application/problem+json");
    }
}

public sealed class LocalAdministrationAccess(string? secret, params string?[] forbiddenSecrets)
{
    private readonly LocalVisualAccess access = new(true, false, secret, forbiddenSecrets);
    public bool Matches(string supplied) => access.Matches(supplied);
}

public sealed class LocalAdministrationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IConfiguration config, IHostEnvironment environment, LocalAdministrationAccess access)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var host = Request.Host.Host;
        var localHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
        if (!environment.IsDevelopment() || !config.GetValue<bool>("LocalAdministration:Enabled") ||
            !Request.IsHttps || !localHost || Context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) ||
            Request.Headers.Keys.Any(k => k.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) || k.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)) ||
            !Request.Headers.TryGetValue("X-SocialFlow-Visual-Admin", out var secret) || secret.Count != 1 ||
            secret[0] is not { Length: <= 256 } value || !access.Matches(value))
            return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("socialflow:local-admin", "true")], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Response.HasStarted ? Task.CompletedTask : AccountSecurity.WriteProblem(Response, 401, "Accès administratif local requis.");
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Response.HasStarted ? Task.CompletedTask : AccountSecurity.WriteProblem(Response, 403, "Les espaces de travail ne sont pas encore disponibles.");
}

public sealed class AccountCsrfFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method)) return;
        // This identity requires an explicit server-held header on every request, never a cookie.
        if (http.User.Identities.Any(i => i.AuthenticationType == AccountSecurity.LocalScheme && i.IsAuthenticated)) return;
        try { await antiforgery.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 400, Title = "Session de formulaire expirée. Rechargez la page." }) { StatusCode = 400 };
        }
    }
}
