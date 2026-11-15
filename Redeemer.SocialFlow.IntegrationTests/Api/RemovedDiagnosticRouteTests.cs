using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed partial class WorkspaceIsolationTests
{
    [Fact]
    public async Task RetiredBusinessDiagnosticRouteIsNotExposed()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        Assert.DoesNotContain(endpoints.OfType<RouteEndpoint>(), endpoint =>
            endpoint.RoutePattern.RawText?.Contains("facebook-business-diagnostic", StringComparison.Ordinal) == true);
        // Authenticated workspace request with CSRF; no provider or connection is invoked.
        var response = await Send(alice, HttpMethod.Post,
            $"/api/workspaces/{a.Id}/social-connections/{Guid.NewGuid()}/facebook-business-diagnostic",
            new { revision = Guid.NewGuid(), businessId = "123" });
        // An unmatched route uses the existing local-admin fallback policy.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
