using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Redeemer.SocialFlow.Application.Visuals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Application.AI;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed partial class WorkspaceIsolationTests
{
    [Fact]
    public async Task ConceptRejectsMissingTamperedStaleAndCrossWorkspaceRequestsBeforeGeneratorResolution()
    {
        var own = await Create(alice, "Saved topic");
        var foreign = await Create(bob, "Other workspace");
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IVisualGenerator>(_ =>
                throw new InvalidOperationException("Must not resolve a paid provider")))));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost:65474") });
        await Send(client, HttpMethod.Post, "/api/auth/login", new { email = "alice@example.test", password = Password });
        client.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        var input = new { identityMode = "Temporary", identityId = "brand", name = "Brand", style = "Flat", colors = new[] { "#123456" },
            title = "Untrusted browser title", content = "Untrusted browser content" };
        var path = $"/api/posts/{own.Id}/workspace-visuals";
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, path + "/generate", input)).StatusCode);
        var prepared = await Send(client, HttpMethod.Post, path + "/concept", input);
        Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
        var concept = await prepared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(own.Title, concept.GetProperty("subject").GetString());
        Assert.Equal(own.Content, concept.GetProperty("message").GetString());
        var validToken = concept.GetProperty("conceptToken").GetString();
        object Payload(string? token) => new { input.identityMode, input.identityId, input.name, input.style, input.colors, conceptToken = token };
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, path + "/generate", Payload("tampered"))).StatusCode);
        var expired = host.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("VisualConcept", VisualConcept.Version, aliceId).ToTimeLimitedDataProtector()
            .Protect("expired-fingerprint", DateTimeOffset.UtcNow.AddMinutes(-1));
        var expiredResponse = await Send(client, HttpMethod.Post, path + "/generate", Payload(expired));
        Assert.Equal(HttpStatusCode.Conflict, expiredResponse.StatusCode);
        Assert.Contains("VisualConceptExpiredOrInvalid", await expiredResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, path + "/generate",
            new { input.identityMode, input.identityId, input.name, style = "Different style", input.colors, conceptToken = validToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Post,
            $"/api/posts/{foreign.Id}/workspace-visuals/concept", input)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Post,
            $"/api/posts/{foreign.Id}/workspace-visuals/generate", Payload(validToken))).StatusCode);
        await using (var db = Db(a.Id))
        {
            var post = await db.SocialPosts.SingleAsync(p => p.Id == own.Id);
            post.Update("Changed title", "Changed editorial message", null, null);
            await db.SaveChangesAsync();
        }
        var stale = await Send(client, HttpMethod.Post, path + "/generate", Payload(validToken));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("VisualConceptStale", await stale.Content.ReadAsStringAsync());
        await using (var db = Db(a.Id))
        {
            var post = await db.SocialPosts.SingleAsync(p => p.Id == own.Id);
            post.Update("Title only", "", null, null);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, HttpMethod.Post, path + "/concept", input)).StatusCode);
    }
}
