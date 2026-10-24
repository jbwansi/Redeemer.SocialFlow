using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Redeemer.SocialFlow.Api.Controllers;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Api.Development.Visuals;
namespace Redeemer.SocialFlow.Api.Development.Facebook;
public sealed class FacebookAdminFilter(LocalVisualAccess access, IHostEnvironment environment) : IAsyncResourceFilter
{
    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next) =>
        new LocalVisualAccessFilter(access, environment).OnResourceExecutionAsync(context, next);
}
public sealed class FacebookEndpointsConvention(bool enabled) : IApplicationModelConvention
{
    public void Apply(ApplicationModel model)
    {
        if (enabled) return;
        foreach (var controller in model.Controllers.Where(c => c.ControllerType == typeof(DevFacebookController) || c.ControllerType == typeof(FacebookOAuthController)).ToArray()) model.Controllers.Remove(controller);
    }
}
public sealed class FacebookFlows(TimeProvider clock)
{
    // Separate namespaces from LinkedIn and from each other; reuse the existing bounded, expiring nonce store.
    public LinkedInStateStore States { get; } = new(clock);
    private readonly LinkedInStateStore tickets = new(clock);
    public string Ticket() { var pair = tickets.Create(); return pair.State + "." + pair.Browser; }
    public bool ConsumeTicket(string? ticket)
    {
        var parts = ticket?.Split('.');
        return parts is { Length: 2 } && tickets.TryConsume(parts[0], parts[1]);
    }
    public static bool LocalHttps(HttpContext http) => http.Request.IsHttps && http.Request.Host.Host == "localhost" &&
        http.Request.Host.Port == 65474 && http.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip) &&
        !http.Request.Headers.Keys.Any(k => k.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) || k.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase));
}
