using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Api.Security;

namespace Redeemer.SocialFlow.IntegrationTests;

/// <summary>
/// Historical business contract tests run with an explicit simulated administrator.
/// Existing visual/Facebook resource filters still run. Authentication boundary tests
/// use WebApplicationFactory directly and never use this fixture.
/// </summary>
public sealed class BusinessApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureServices(services =>
    {
        TestWorkspace.Register(services);
        services.AddTransient<BusinessAdministrator>();
        services.PostConfigure<AuthenticationOptions>(options =>
            options.SchemeMap[AccountSecurity.LocalScheme].HandlerType = typeof(BusinessAdministrator));
    });
    public sealed class BusinessAdministrator(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("socialflow:local-admin", "true")], AccountSecurity.LocalScheme)), Scheme.Name)));
    }
}
