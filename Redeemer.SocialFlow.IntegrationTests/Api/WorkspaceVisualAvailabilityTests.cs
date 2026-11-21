using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Domain.Entities;
using SkiaSharp;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed partial class WorkspaceIsolationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task VisualStatus_IsAuthenticatedAndDoesNotConstructGenerator(bool enabled, bool configured)
    {
        await using var host = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Visuals:WorkspaceEnabled", enabled.ToString())
            .UseSetting("OpenAI:Images:Model", configured ? "gpt-image-1.5" : "")
            .UseSetting("OpenAI:Images:ApiKey", "fake-private-test-key")
            .ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IVisualGenerator>(
                _ => throw new InvalidOperationException("Generator must not be constructed by status or list")))));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost:65474") });
        const string path = "/api/posts/workspace-visuals/status";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, "/api/auth/login",
            new { email = "alice@example.test", password = Password })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(enabled, json.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(enabled, json.RootElement.GetProperty("uploadEnabled").GetBoolean());
        Assert.Equal(enabled && configured, json.RootElement.GetProperty("generationEnabled").GetBoolean());
        Assert.DoesNotContain("fake-private-test-key", body);
        var post = await Create(alice, "Visual status test");
        Assert.Equal(enabled ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/posts/{post.Id}/workspace-visuals")).StatusCode);
        // Unknown/disabled action routes retain the existing fail-closed fallback policy.
        client.DefaultRequestHeaders.Remove("X-Workspace-Id");
        client.DefaultRequestHeaders.Add("X-Workspace-Id", b.Id.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData(WorkspaceRole.Owner)]
    [InlineData(WorkspaceRole.Editor)]
    [InlineData(WorkspaceRole.Viewer)]
    public async Task WorkspaceVisuals_RolesIsolationAndSimulatedMutations(WorkspaceRole role)
    {
        await using (var db = Db(a.Id))
        {
            db.WorkspaceMemberships.Add(WorkspaceMembership.Join(a.Id, bobId, role));
            await db.SaveChangesAsync();
        }
        var own = await Create(alice, "Own post");
        var foreign = await Create(bob, "Foreign post");
        var generator = new WorkspaceImageGenerator();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IVisualGenerator>(generator))));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost:65474") });
        await Send(client, HttpMethod.Post, "/api/auth/login", new { email = "bob@example.test", password = Password });
        client.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        var path = $"/api/posts/{own.Id}/workspace-visuals";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/posts/{foreign.Id}/workspace-visuals")).StatusCode);
        Assert.Equal(0, generator.Calls);
        var generated = await Send(client, HttpMethod.Post, path + "/generate",
            new { identityId = "test", name = "Test", style = "Simple", colors = new[] { "#123456" }, visualBrief = "Test image" });
        Assert.Equal(role == WorkspaceRole.Viewer ? HttpStatusCode.Forbidden : HttpStatusCode.Created, generated.StatusCode);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(WorkspaceImageGenerator.Png()), "file", "test.png");
        using var upload = new HttpRequestMessage(HttpMethod.Post, path + "/upload") { Content = form };
        upload.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(role == WorkspaceRole.Viewer ? HttpStatusCode.Forbidden : HttpStatusCode.Created,
            (await client.SendAsync(upload)).StatusCode);
        Assert.Equal(role == WorkspaceRole.Viewer ? 0 : 1, generator.Calls);
    }

    private sealed class WorkspaceImageGenerator : IVisualGenerator
    {
        public int Calls;
        public static byte[] Png()
        {
            using var bitmap = new SKBitmap(16, 12);
            bitmap.Erase(SKColors.Blue);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }
        public Task<GeneratedVisual> GenerateAsync(GenerateVisualRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            var bytes = Png();
            return Task.FromResult(new GeneratedVisual(bytes, new("Fake", "test", request.Identity.Id,
                request.Platform, request.VisualBrief, "image/png", 16, 12, bytes.Length, "medium", DateTimeOffset.UtcNow)));
        }
    }
}
