using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed class LinkedInPublicationApiTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"linkedin-ui-{Guid.NewGuid():N}.db");
    private readonly Clock _clock = new();
    private readonly Credentials _credentials = new();
    private readonly Publisher _publisher = new();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private static string Route(Guid id, string action) => $"/api/dev/linkedin/posts/{id}/{action}";
    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .UseSetting("ConnectionStrings:SocialFlow", $"Data Source={_path};Pooling=False")
            .UseSetting("LinkedIn:PublicationEnabled", "true").UseSetting("LinkedIn:WorkerEnabled", "false")
            .ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));
                services.Replace(ServiceDescriptor.Singleton<ILinkedInCredentialProvider>(_credentials));
                services.Replace(ServiceDescriptor.Singleton<IPostPublisher>(_publisher));
            }));
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().Database.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        _client.Dispose(); await _factory.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
    }
    private async Task<Guid> Seed(bool scheduled = true, bool future = false, SocialPlatform platform = SocialPlatform.LinkedIn)
    {
        var post = SocialPost.Create("Title", "Contenu exact\nà publier", platform, new Clock { Now = _clock.Now.AddDays(-1) });
        post.SubmitForReview(); post.Approve();
        if (scheduled) post.Schedule(future ? _clock.Now.AddHours(1) : _clock.Now);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        db.SocialPosts.Add(post); await db.SaveChangesAsync(); return post.Id;
    }
    private async Task Tick(WebApplicationFactory<Program>? factory = null)
    {
        await using var scope = (factory ?? _factory).Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LinkedInPublicationService>().ProcessDueAsync(default);
    }
    private WebApplicationFactory<Program> WorkerFactory() => _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        // Test the worker's single scan explicitly; never register/start a real timer in these tests.
        services.Replace(ServiceDescriptor.Singleton(new LinkedInPublicationSettings(true, true)))));

    [Theory]
    [InlineData(PostPublicationOutcome.Confirmed, PublicationOperationState.Confirmed, SocialPostStatus.Published)]
    [InlineData(PostPublicationOutcome.Failed, PublicationOperationState.Failed, SocialPostStatus.Failed)]
    [InlineData(PostPublicationOutcome.Indeterminate, PublicationOperationState.Indeterminate, SocialPostStatus.Scheduled)]
    public async Task ManualResultIsDurableAndSharedWithWorker(PostPublicationOutcome outcome, PublicationOperationState state, SocialPostStatus status)
    {
        _publisher.Outcome = outcome;
        var id = await Seed();
        var preview = (await _client.GetFromJsonAsync<LinkedInPreview>(Route(id, "preview")))!;
        Assert.Equal("urn:li:person:member", preview.Destination);
        Assert.Equal("Contenu exact\nà publier", preview.Content);
        var payload = new { preview.Confirmation, destination = "urn:li:person:attacker" };
        using var response = await _client.PostAsJsonAsync(Route(id, "publish"), payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var operation = (await response.Content.ReadFromJsonAsync<PublicationOperationDto>())!;
        Assert.Equal(state, operation.State);
        Assert.Equal(preview.Content, _publisher.Received!.Content);
        Assert.Equal(preview.Destination, _publisher.Received.Destination);
        using var repeated = await _client.PostAsJsonAsync(Route(id, "publish"), payload);
        Assert.Equal(operation, await repeated.Content.ReadFromJsonAsync<PublicationOperationDto>());
        await using var worker = WorkerFactory();
        await Tick(worker);
        var reloaded = (await worker.CreateClient().GetFromJsonAsync<LinkedInPublicationView>(Route(id, "publication")))!;
        Assert.Equal(operation, reloaded.Operation);
        Assert.False(reloaded.CanPublish);
        Assert.Equal(1, _publisher.Calls);
        Assert.Equal(status, (await _client.GetFromJsonAsync<SocialPostDto>($"/api/posts/{id}"))!.Status);
    }

    [Theory]
    [InlineData(PostPublicationOutcome.Confirmed)]
    [InlineData(PostPublicationOutcome.Failed)]
    [InlineData(PostPublicationOutcome.Indeterminate)]
    public async Task WorkerOnlyProcessesDueLinkedInPostsAndRequiresOptIn(PostPublicationOutcome outcome)
    {
        _publisher.Outcome = outcome;
        var due = await Seed();
        await Seed(future: true); await Seed(scheduled: false); await Seed(platform: SocialPlatform.Facebook);
        await Tick(); Assert.Equal(0, _publisher.Calls);
        await using var worker = WorkerFactory();
        await Tick(worker); await Tick(worker);
        Assert.Equal(1, _publisher.Calls);
        Assert.Equal(outcome == PostPublicationOutcome.Confirmed ? SocialPostStatus.Published :
            outcome == PostPublicationOutcome.Failed ? SocialPostStatus.Failed : SocialPostStatus.Scheduled,
            (await _client.GetFromJsonAsync<SocialPostDto>($"/api/posts/{due}"))!.Status);
    }

    [Fact]
    public async Task WorkerSkipsInterruptedClaimAndHistorySurvivesDisconnect()
    {
        var id = await Seed();
        await using (var scope = _factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPublicationOperationStore>()
                .TryClaimAsync(new(id, Guid.NewGuid(), "LinkedIn", "urn:li:person:member"));
        await using var worker = WorkerFactory();
        await Tick(worker);
        Assert.Equal(0, _publisher.Calls);
        _credentials.Missing = true;
        var history = (await _client.GetFromJsonAsync<LinkedInPublicationView>(Route(id, "publication")))!;
        Assert.Equal(PublicationOperationState.Indeterminate, history.Operation!.State);
        Assert.Null(history.Operation.CompletedAt);
    }

    [Fact]
    public async Task DisabledPublicationRejectsManualSend()
    {
        var id = await Seed();
        var preview = (await _client.GetFromJsonAsync<LinkedInPreview>(Route(id, "preview")))!;
        await using var disabled = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton(new LinkedInPublicationSettings(false, false)))));
        using var client = disabled.CreateClient();
        using var response = await client.PostAsJsonAsync(Route(id, "publish"), new { preview.Confirmation });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, _publisher.Calls);
    }

    [Fact]
    public async Task WorkerAndManualRaceDispatchOnce()
    {
        var id = await Seed();
        var preview = (await _client.GetFromJsonAsync<LinkedInPreview>(Route(id, "preview")))!;
        _publisher.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var manual = _client.PostAsJsonAsync(Route(id, "publish"), new { preview.Confirmation });
        try
        {
            await _publisher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var worker = WorkerFactory();
            await Tick(worker);
            Assert.Equal(1, _publisher.Calls);
        }
        finally { _publisher.Release.TrySetResult(); }
        using var response = await manual;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("future")]
    [InlineData("approved")]
    [InlineData("platform")]
    [InlineData("disconnected")]
    public async Task IneligiblePostsHaveNoPreviewOrPublication(string reason)
    {
        var id = await Seed(scheduled: reason != "approved", future: reason == "future",
            platform: reason == "platform" ? SocialPlatform.Facebook : SocialPlatform.LinkedIn);
        _credentials.Missing = reason == "disconnected";
        using var preview = await _client.GetAsync(Route(id, "preview"));
        Assert.Equal(HttpStatusCode.Conflict, preview.StatusCode);
        await using var worker = WorkerFactory();
        await Tick(worker); Assert.Equal(0, _publisher.Calls);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("identity")]
    [InlineData("tampered")]
    public async Task ConfirmationCannotBeReusedForChangedContext(string change)
    {
        var id = await Seed();
        var preview = (await _client.GetFromJsonAsync<LinkedInPreview>(Route(id, "preview")))!;
        if (change == "expired") _clock.Now = _clock.Now.AddMinutes(6);
        if (change == "identity") _credentials.Subject = "different";
        using var response = await _client.PostAsJsonAsync(Route(id, "publish"), new { confirmation = change == "tampered" ? "invalid" : preview.Confirmation });
        Assert.Equal(change == "identity" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _publisher.Calls);
    }

    [Fact]
    public async Task RuntimeHasNoSecretAndMissingPostReturns404()
    {
        var runtime = (await _client.GetFromJsonAsync<LinkedInRuntime>("/api/dev/linkedin/runtime"))!;
        Assert.True(runtime.Connected); Assert.True(runtime.PublicationEnabled); Assert.False(runtime.WorkerEnabled);
        Assert.DoesNotContain("private-token", System.Text.Json.JsonSerializer.Serialize(runtime));
        using var missing = await _client.GetAsync(Route(Guid.NewGuid(), "publication"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task EndpointsAreUnavailableOutsideDevelopment(string environment)
    {
        await using var other = _factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = other.CreateClient();
        foreach (var path in new[] { "/api/dev/linkedin/runtime", Route(Guid.NewGuid(), "publication"), Route(Guid.NewGuid(), "preview") })
        { using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
        using var publish = await client.PostAsJsonAsync(Route(Guid.NewGuid(), "publish"), new { confirmation = "invalid" });
        Assert.Equal(HttpStatusCode.NotFound, publish.StatusCode);
        Assert.Equal(0, _publisher.Calls);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Credentials : ILinkedInCredentialProvider
    {
        public bool Missing { get; set; }
        public string Subject { get; set; } = "member";
        public Task<LinkedInMemberCredential?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Missing ? null : new LinkedInMemberCredential
        { Subject = Subject, AccessToken = "private-token", ExpiresAt = new(2031, 1, 1, 0, 0, 0, TimeSpan.Zero) });
    }
    private sealed class Publisher : IPostPublisher
    {
        public int Calls;
        public PostPublicationOutcome Outcome { get; set; } = PostPublicationOutcome.Confirmed;
        public PostPublicationRequest? Received;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Release;
        public async Task<PostPublicationResult> PublishAsync(PostPublicationRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls); Received = request; Entered.TrySetResult();
            if (Release is not null) await Release.Task;
            return Outcome switch { PostPublicationOutcome.Confirmed => PostPublicationResult.Confirmed("urn:li:share:123"),
                PostPublicationOutcome.Failed => PostPublicationResult.Failed(), _ => PostPublicationResult.Indeterminate() };
        }
    }
}
