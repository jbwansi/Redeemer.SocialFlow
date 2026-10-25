using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Api.Security;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed class LocalAdministrationTests
{
    private const string Secret = "test-only-local-administration-secret-045";
    private static WebApplicationFactory<Program> Factory(string environment = "Development", bool enabled = true, bool loopback = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment(environment)
            .UseSetting("ConnectionStrings:SocialFlow", "Data Source=:memory:")
            .UseSetting("ConnectionStrings:Accounts", "Data Source=:memory:")
            .UseSetting("LocalAdministration:Enabled", enabled.ToString())
            .ConfigureServices(services =>
            {
                services.RemoveAll<LocalAdministrationAccess>();
                services.AddSingleton(new LocalAdministrationAccess(Secret));
                services.AddSingleton<IStartupFilter>(new Address(loopback));
            }));
    private static HttpClient Client(WebApplicationFactory<Program> factory, string url = "https://localhost:65474") =>
        factory.CreateClient(new() { BaseAddress = new Uri(url), AllowAutoRedirect = false });

    [Fact]
    public async Task ServerHeldSecret_AllowsTicket_WithoutCreatingAnAccountSession()
    {
        await using var factory = Factory(); using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/dev/linkedin/connect-ticket", new { })).StatusCode);
        client.DefaultRequestHeaders.Add("X-SocialFlow-Visual-Admin", Secret);
        var response = await client.PostAsJsonAsync("/api/dev/linkedin/connect-ticket", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.False(factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>().GetValue<bool>("LinkedIn:WorkerEnabled"));
        Assert.False(factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>().GetValue<bool>("Facebook:WorkerEnabled"));
    }

    [Theory]
    [InlineData("Development", false, true, "https://localhost:65474", false)]
    [InlineData("Development", true, false, "https://localhost:65474", false)]
    [InlineData("Development", true, true, "http://localhost:65474", false)]
    [InlineData("Development", true, true, "https://hostile.example:65474", false)]
    [InlineData("Development", true, true, "https://localhost:65474", true)]
    [InlineData("Production", true, true, "https://localhost:65474", false)]
    public async Task SecretAlone_CannotBypassLocalBoundary(string environment, bool enabled, bool loopback, string url, bool forwarded)
    {
        await using var factory = Factory(environment, enabled, loopback); using var client = Client(factory, url);
        client.DefaultRequestHeaders.Add("X-SocialFlow-Visual-Admin", Secret);
        if (forwarded) client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/posts")).StatusCode);
    }

    [Fact]
    public async Task PublicOAuthEntry_RejectsMissingInvalidAndReplayedTicket()
    {
        await using var factory = Factory(); using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dev/linkedin/connect")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dev/linkedin/callback?state=invalid&code=fake")).StatusCode);
        client.DefaultRequestHeaders.Add("X-SocialFlow-Visual-Admin", Secret);
        var response = await client.PostAsJsonAsync("/api/dev/linkedin/connect-ticket", new { });
        var ticket = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizeUrl").GetString()!;
        client.DefaultRequestHeaders.Remove("X-SocialFlow-Visual-Admin");
        // Entry only constructs a redirect; it never calls the provider.
        var start = await client.GetAsync(ticket);
        Assert.Contains(start.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.ServiceUnavailable });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ticket)).StatusCode);
    }
    private sealed class Address(bool local) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextRequest) => { context.Connection.RemoteIpAddress = IPAddress.Parse(local ? "127.0.0.1" : "192.0.2.1"); return nextRequest(context); });
            next(app);
        };
    }
}
