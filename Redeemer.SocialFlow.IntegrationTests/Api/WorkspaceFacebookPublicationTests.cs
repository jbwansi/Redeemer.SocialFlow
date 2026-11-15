using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Api.Publishing;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Social;
using SkiaSharp;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;
public sealed partial class WorkspaceIsolationTests
{
    private sealed class FacebookClock : TimeProvider
    { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private WebApplicationFactory<Program> FacebookHost(FacebookDispatch handler, FacebookClock clock) => factory.WithWebHostBuilder(builder => builder
        .UseSetting("SocialConnections:Providers:Facebook:PublicationEnabled", "true")
        .UseSetting("SocialConnections:Publication:WorkerEnabled", "false")
        .UseSetting("Visuals:Storage:RootPath", Path.Combine(root, "facebook-images"))
        .ConfigureServices(services => {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            services.RemoveAll<IWorkspaceOAuthProvider>();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Authentication:PublicOrigin"] = "https://localhost:65474",
                ["SocialConnections:Providers:Facebook:Enabled"] = "true",
                ["SocialConnections:Providers:Facebook:ClientId"] = "42",
                ["SocialConnections:Providers:Facebook:ClientSecret"] = "SECRET-app",
                ["SocialConnections:Providers:Facebook:ApiVersion"] = "v26.0",
                ["SocialConnections:Providers:Facebook:Scopes"] = "pages_show_list pages_manage_posts pages_read_engagement"
            }).Build();
            services.AddScoped<IWorkspaceOAuthProvider>(_ => new FacebookWorkspaceOAuthProvider(new(handler, false), config, clock));
            services.AddHttpClient("WorkspaceFacebookPublication").ConfigurePrimaryHttpMessageHandler(() => handler);
        }));
    private async Task<HttpClient> FacebookClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost:65474"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, "/api/auth/login", new { email = "alice@example.test", password = Password })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString()); return client;
    }
    private async Task<(Guid Post, Guid Connection)> FacebookData(WebApplicationFactory<Program> host, FacebookClock clock, bool image = false, bool granted = true, bool future = false, bool approvedOnly = false)
    {
        await using var db = Db(a.Id);
        using var scope = host.Services.CreateScope();
        var post = SocialPost.Create("Facebook", "Workspace Facebook content", SocialPlatform.Facebook, new PastClock());
        db.SocialPosts.Add(post); await db.SaveChangesAsync();
        if (image)
        {
            using var bitmap = new SKBitmap(16, 12); bitmap.Erase(SKColors.Blue);
            using var picture = SKImage.FromBitmap(bitmap); using var encoded = picture.Encode(SKEncodedImageFormat.Png, 90); var bytes = encoded.ToArray();
            var visual = PostVisual.Create(post, VisualOrigin.Uploaded, "image/png", 16, 12, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), null, clock.Now);
            visual.Complete(); db.PostVisuals.Add(visual); await db.SaveChangesAsync(); post.SelectVisual(visual, clock.Now);
            await scope.ServiceProvider.GetRequiredService<IVisualStorage>().WriteAsync(visual.StorageKey, bytes);
        }
        post.SubmitForReview(); post.Approve();
        if (!approvedOnly) post.Schedule(clock.Now.AddMinutes(future ? 10 : -1));
        var connection = new SocialConnection { WorkspaceId = a.Id, Platform = SocialPlatform.Facebook, ExternalAccountId = "123", DisplayName = "Page",
            AccountType = "Page", ConnectionStatus = SocialConnectionStatus.Connected, PublishingPermissionGranted = granted,
            ConnectedByUserId = aliceId, ExpiresAtUtc = null };
        connection.ProtectedCredential = scope.ServiceProvider.GetRequiredService<ISocialSecretProtector>().Protect(JsonSerializer.Serialize(
            new SocialCredential("SECRET-page", null, null, null, ["pages_manage_posts", "pages_read_engagement"])), $"SocialFlow/SaaS/v1/{a.Id}/{connection.Id}/credential");
        db.SocialConnections.Add(connection); await db.SaveChangesAsync(); return (post.Id, connection.Id);
    }
    private static string FacebookRoute(Guid id, string action) => $"/api/facebook/posts/{id}/{action}";
    private async Task FacebookTick(WebApplicationFactory<Program> host)
    {
        // No timer is registered by this host. Exercise exactly the shared service called by the existing worker.
        host.Services.GetRequiredService<IConfiguration>()["SocialConnections:Publication:WorkerEnabled"] = "true";
        await using var scope = host.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<WorkspaceAccessContext>().Bind(a.Id);
        await scope.ServiceProvider.GetRequiredService<WorkspacePublicationService>().ProcessDueAsync(default);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task WorkspaceFacebook_TextAndImageShareManualAndScheduledDurablePipeline(bool image, bool automatic)
    {
        var handler = new FacebookDispatch(); var clock = new FacebookClock();
        await using var host = FacebookHost(handler, clock); using var client = await FacebookClient(host);
        var data = await FacebookData(host, clock, image, future: automatic);
        if (automatic)
        {
            await FacebookTick(host); Assert.Equal(0, handler.Publications);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "automatic"), new { connectionId = data.Connection })).StatusCode);
            await FacebookTick(host); Assert.Equal(0, handler.Publications);
            clock.Now = clock.Now.AddMinutes(11);
            await Task.WhenAll(Task.Run(() => FacebookTick(host)), Task.Run(() => FacebookTick(host)));
        }
        else
        {
            var previewResponse = await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "preview"), new { connectionId = data.Connection });
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = (await previewResponse.Content.ReadFromJsonAsync<LinkedInPreview>())!;
            Assert.Equal("123", preview.Destination); Assert.Equal(0, handler.Publications);
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Send(client, HttpMethod.Post, FacebookRoute(data.Post, "publish"), new { preview.Confirmation })));
            Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            Assert.DoesNotContain("SECRET", await results[0].Content.ReadAsStringAsync());
        }
        await FacebookTick(host);
        Assert.Equal(1, handler.Publications); Assert.EndsWith(image ? "/photos" : "/feed", handler.Path);
        Assert.Contains("Workspace", handler.Body); Assert.Contains("appsecret_proof", handler.Body);
        await using var db = Db(a.Id);
        var operation = await db.PublicationOperations.SingleAsync();
        Assert.Equal(PublicationOperationState.Confirmed, operation.State); Assert.Equal("123_789", operation.ExternalId);
        Assert.Equal(image, operation.Visual is not null);
        Assert.Equal(SocialPostStatus.Published, (await db.SocialPosts.SingleAsync()).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(FacebookRoute(data.Post, "publication"))).StatusCode);
    }

    [Theory]
    [InlineData("permission")] [InlineData("expired")] [InlineData("type")] [InlineData("app")] [InlineData("identity")] [InlineData("scope")] [InlineData("graph")]
    public async Task WorkspaceFacebook_PreflightBlocksWithoutPublishing_AndCanResumeAfterExplicitReauthorization(string fault)
    {
        var handler = new FacebookDispatch(); var clock = new FacebookClock();
        await using var host = FacebookHost(handler, clock); using var client = await FacebookClient(host);
        var data = await FacebookData(host, clock);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "automatic"), new { connectionId = data.Connection })).StatusCode);
        handler.Fault = fault;
        if (fault == "permission") { await using var db = Db(a.Id); var c = await db.SocialConnections.SingleAsync(); c.PublishingPermissionGranted = false; await db.SaveChangesAsync(); }
        await FacebookTick(host); await FacebookTick(host);
        Assert.Equal(0, handler.Publications);
        await using (var db = Db(a.Id))
        {
            Assert.Empty(await db.PublicationOperations.ToListAsync());
            Assert.Equal(SocialPostStatus.Scheduled, (await db.SocialPosts.SingleAsync()).Status);
            var plan = await db.LinkedInAutomations.SingleAsync(); Assert.Equal(LinkedInAutomationState.Blocked, plan.State);
            Assert.Equal("authorization_or_validation_failed", plan.BlockReason);
            // Test fixture models a new authoritative OAuth grant; production never infers it from a token.
            var c = await db.SocialConnections.SingleAsync(); c.PublishingPermissionGranted = true; await db.SaveChangesAsync();
        }
        handler.Fault = null;
        await FacebookTick(host); Assert.Equal(0, handler.Publications);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "automatic"), new { connectionId = data.Connection })).StatusCode);
        await FacebookTick(host); Assert.Equal(1, handler.Publications);
    }

    [Theory]
    [InlineData(400, PublicationOperationState.Failed)] [InlineData(403, PublicationOperationState.Failed)]
    [InlineData(500, PublicationOperationState.Indeterminate)] [InlineData(0, PublicationOperationState.Indeterminate)]
    [InlineData(200, PublicationOperationState.Indeterminate)]
    public async Task WorkspaceFacebook_ProviderErrorsAndLostAcknowledgementsAreNeverRetried(int status, PublicationOperationState expected)
    {
        var handler = new FacebookDispatch { Status = status }; var clock = new FacebookClock();
        await using var host = FacebookHost(handler, clock); using var client = await FacebookClient(host);
        var data = await FacebookData(host, clock);
        var preview = (await (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "preview"), new { connectionId = data.Connection })).Content.ReadFromJsonAsync<LinkedInPreview>())!;
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "publish"), new { preview.Confirmation })).StatusCode);
        await FacebookTick(host); Assert.Equal(1, handler.Publications);
        var view = (await client.GetFromJsonAsync<LinkedInPublicationView>(FacebookRoute(data.Post, "publication")))!;
        Assert.Equal(expected, view.Operation!.State); Assert.Null(view.Operation.ExternalId);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(view));
    }

    [Fact]
    public async Task WorkspaceFacebook_UnverifiedGrantIsExplicit_AndCannotBeScheduledOrPublished()
    {
        var handler = new FacebookDispatch(); var clock = new FacebookClock();
        await using var host = FacebookHost(handler, clock); using var client = await FacebookClient(host);
        var data = await FacebookData(host, clock, granted: false);
        var view = (await client.GetFromJsonAsync<LinkedInPublicationView>(FacebookRoute(data.Post, "publication")))!;
        Assert.False(view.CanPublish); Assert.Equal("publishing_permission_unverified", view.BlockReason);
        foreach (var action in new[] { "preview", "automatic" })
            Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, action), new { connectionId = data.Connection })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(FacebookRoute(data.Post, "automatic"), new { connectionId = data.Connection })).StatusCode);
        await FacebookTick(host); Assert.Equal(0, handler.Publications);
    }

    [Fact]
    public async Task WorkspaceFacebook_CancelledOrChangedPlansCannotDispatch()
    {
        var handler = new FacebookDispatch(); var clock = new FacebookClock();
        await using var host = FacebookHost(handler, clock); using var client = await FacebookClient(host);
        var data = await FacebookData(host, clock);
        await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "automatic"), new { connectionId = data.Connection });
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Delete, FacebookRoute(data.Post, "automatic"))).StatusCode);
        await FacebookTick(host); Assert.Equal(0, handler.Publications);
        await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "automatic"), new { connectionId = data.Connection });
        await using (var db = Db(a.Id)) { (await db.SocialPosts.SingleAsync()).Cancel(); await db.SaveChangesAsync(); }
        await FacebookTick(host); Assert.Equal(0, handler.Publications);
    }

    [Theory]
    [InlineData(PublishingCapabilityStatus.Unverified)]
    [InlineData(PublishingCapabilityStatus.Denied)]
    public async Task WorkspaceFacebook_ExplicitCapabilityBlocksLegacyBooleanWithoutDispatch(PublishingCapabilityStatus status)
    {
        var handler = new FacebookDispatch(); var clock = new FacebookClock();
        await using var host = FacebookHost(handler, clock); using var client = await FacebookClient(host);
        var data = await FacebookData(host, clock, granted: true);
        await using (var db = Db(a.Id))
        {
            var connection = await db.SocialConnections.SingleAsync();
            connection.PublishingCapabilityStatus = status;
            await db.SaveChangesAsync();
        }
        var view = (await client.GetFromJsonAsync<LinkedInPublicationView>(FacebookRoute(data.Post, "publication")))!;
        Assert.False(view.CanPublish);
        foreach (var action in new[] { "preview", "automatic" })
            Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, action), new { connectionId = data.Connection })).StatusCode);
        Assert.Equal(0, handler.Publications);
    }

    [Theory]
    [InlineData(false, 201, SocialPostStatus.Published)]
    [InlineData(true, 201, SocialPostStatus.Published)]
    [InlineData(false, 403, SocialPostStatus.Failed)]
    [InlineData(false, 500, SocialPostStatus.Approved)]
    public async Task WorkspaceFacebook_ApprovedManualUsesDurableClaimWithoutScheduling(bool image, int status, SocialPostStatus expected)
    {
        var handler = new FacebookDispatch { Status = status }; var clock = new FacebookClock();
        await using var host = FacebookHost(handler, clock); using var client = await FacebookClient(host);
        var data = await FacebookData(host, clock, image, approvedOnly: true);
        var view = (await client.GetFromJsonAsync<LinkedInPublicationView>(FacebookRoute(data.Post, "publication")))!;
        Assert.True(view.CanPublish); Assert.Null(view.ManualBlockReason); Assert.Null(view.BlockReason);
        Assert.False(view.WorkerEnabled);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "automatic"), new { connectionId = data.Connection })).StatusCode);
        var previews = new List<LinkedInPreview>();
        for (var i = 0; i < 2; i++)
            previews.Add((await (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "preview"), new { connectionId = data.Connection })).Content.ReadFromJsonAsync<LinkedInPreview>())!);
        Assert.Equal(0, handler.Publications);
        var responses = await Task.WhenAll(previews.Select(p => Send(client, HttpMethod.Post, FacebookRoute(data.Post, "publish"), new { p.Confirmation })));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, handler.Publications); Assert.EndsWith(image ? "/photos" : "/feed", handler.Path);
        await using var db = Db(a.Id);
        var post = await db.SocialPosts.SingleAsync();
        Assert.Equal(expected, post.Status); Assert.Null(post.ScheduledAt);
        Assert.Single(await db.PublicationOperations.ToListAsync()); Assert.Empty(await db.LinkedInAutomations.ToListAsync());
        Assert.DoesNotContain("SECRET", await responses[0].Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("disabled", "publication_disabled")]
    [InlineData("unverified", "publishing_permission_unverified")]
    [InlineData("denied", "publishing_permission_unverified")]
    [InlineData("future", "scheduled_not_due")]
    public async Task WorkspaceFacebook_ManualBlockersRemainEnforced(string fault, string reason)
    {
        var handler = new FacebookDispatch(); var clock = new FacebookClock();
        await using var host = FacebookHost(handler, clock); using var client = await FacebookClient(host);
        var data = await FacebookData(host, clock, future: true, approvedOnly: fault != "future");
        if (fault == "disabled") host.Services.GetRequiredService<IConfiguration>()["SocialConnections:Providers:Facebook:PublicationEnabled"] = "false";
        if (fault is "unverified" or "denied")
        {
            await using var db = Db(a.Id); var connection = await db.SocialConnections.SingleAsync();
            connection.PublishingCapabilityStatus = fault == "unverified" ? PublishingCapabilityStatus.Unverified : PublishingCapabilityStatus.Denied;
            await db.SaveChangesAsync();
        }
        var view = (await client.GetFromJsonAsync<LinkedInPublicationView>(FacebookRoute(data.Post, "publication")))!;
        Assert.False(view.CanPublish); Assert.Equal(reason, view.ManualBlockReason);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "preview"), new { connectionId = data.Connection })).StatusCode);
        Assert.Equal(0, handler.Publications);
        await using var check = Db(a.Id); Assert.Empty(await check.PublicationOperations.ToListAsync());
    }

    [Theory]
    [InlineData("revoked")] [InlineData("rescheduled")] [InlineData("disabled")]
    public async Task WorkspaceFacebook_ApprovedConfirmationRechecksChangesBeforeClaim(string change)
    {
        var handler = new FacebookDispatch(); var clock = new FacebookClock();
        await using var host = FacebookHost(handler, clock); using var client = await FacebookClient(host);
        var data = await FacebookData(host, clock, approvedOnly: true);
        var preview = (await (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "preview"), new { connectionId = data.Connection })).Content.ReadFromJsonAsync<LinkedInPreview>())!;
        await using (var db = Db(a.Id))
        {
            if (change == "revoked") (await db.SocialConnections.SingleAsync()).PublishingCapabilityStatus = PublishingCapabilityStatus.Denied;
            if (change == "rescheduled") (await db.SocialPosts.SingleAsync()).Schedule(clock.Now.AddHours(1));
            await db.SaveChangesAsync();
        }
        if (change == "disabled") host.Services.GetRequiredService<IConfiguration>()["SocialConnections:Providers:Facebook:PublicationEnabled"] = "false";
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, FacebookRoute(data.Post, "publish"), new { preview.Confirmation })).StatusCode);
        Assert.Equal(0, handler.Publications);
        await using var check = Db(a.Id); Assert.Empty(await check.PublicationOperations.ToListAsync());
    }

    private sealed class FacebookDispatch : HttpMessageHandler
    {
        public int Publications; public int Status = 201; public string? Fault;
        public string Path = "", Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("graph.facebook.com", request.RequestUri!.Host);
            if (request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref Publications); Path = request.RequestUri.AbsolutePath; Body = await request.Content!.ReadAsStringAsync(ct);
                Assert.Equal("SECRET-page", request.Headers.Authorization!.Parameter);
                Assert.DoesNotContain("SECRET", Body);
                if (Status == 0) throw new HttpRequestException("SECRET lost acknowledgement");
                return new((HttpStatusCode)Status) { Content = new StringContent(Status == 201 ? """{"id":"123_789","post_id":"123_789"}""" : """{"id":"123_789","error":{"code":190,"message":"SECRET"}}""") };
            }
            if (Fault == "graph") return new(HttpStatusCode.OK) { Content = new StringContent("""{"error":{"code":190,"message":"SECRET"}}""") };
            if (request.RequestUri.AbsolutePath.EndsWith("debug_token"))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = new { is_valid = true, app_id = Fault == "app" ? "99" : "42",
                    type = Fault == "type" ? "USER" : "PAGE", expires_at = Fault == "expired" ? 1 : 0,
                    scopes = Fault == "scope" ? Array.Empty<string>() : new[] { "pages_manage_posts", "pages_read_engagement" } } }) };
            Assert.Contains("appsecret_proof", request.RequestUri.Query);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = Fault == "identity" ? "999" : "123" }) };
        }
    }
}
