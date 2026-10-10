using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;
using SkiaSharp;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Publishing;

public sealed class LinkedInImagePublicationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "li-image-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new();
    private readonly Handler _handler = new();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _postId, _visualId;
    private string _key = "";
    private byte[] _bytes = [];
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .UseSetting("ConnectionStrings:SocialFlow", $"Data Source={Path.Combine(_root, "test.db")};Pooling=False")
            .UseSetting("LinkedIn:PublicationEnabled", "true").UseSetting("LinkedIn:WorkerEnabled", "false")
            .UseSetting("Visuals:Storage:RootPath", Path.Combine(_root, "files"))
            .ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));
                services.Replace(ServiceDescriptor.Singleton<ILinkedInCredentialProvider>(new Credentials(_clock)));
                // Exercise exactly the scan called by the worker, without starting its timer.
                services.Replace(ServiceDescriptor.Singleton(new LinkedInPublicationSettings(true, true)));
                services.AddHttpClient<LinkedInPostPublisher>().ConfigurePrimaryHttpMessageHandler(() => _handler);
            }));
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        await db.Database.MigrateAsync();
        var post = SocialPost.Create("Image", "Exact durable text", SocialPlatform.LinkedIn, new Clock(_clock.GetUtcNow().AddDays(-1)));
        db.SocialPosts.Add(post); await db.SaveChangesAsync();
        using var bitmap = new SKBitmap(16, 12); bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        _bytes = encoded.ToArray();
        var visual = PostVisual.Create(post, VisualOrigin.Uploaded, "image/png", 16, 12, _bytes.Length,
            Convert.ToHexString(SHA256.HashData(_bytes)), null, _clock.GetUtcNow());
        visual.Complete();
        db.PostVisuals.Add(visual); await db.SaveChangesAsync();
        post.SelectVisual(visual, _clock.GetUtcNow());
        post.SubmitForReview(); post.Approve(); post.Schedule(_clock.GetUtcNow());
        await db.SaveChangesAsync();
        _postId = post.Id; _visualId = visual.Id; _key = visual.StorageKey;
        await scope.ServiceProvider.GetRequiredService<IVisualStorage>().WriteAsync(_key, _bytes);
    }
    public async Task DisposeAsync()
    {
        _client.Dispose(); await _factory.DisposeAsync();
        Directory.Delete(_root, true);
    }
    private string Route(string action) => $"/api/dev/linkedin/posts/{_postId}/{action}";
    private async Task Tick()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LinkedInPublicationService>().ProcessDueAsync(default);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualAndAutomaticPublishImageExactlyOnce(bool automatic)
    {
        var preview = (await _client.GetFromJsonAsync<LinkedInPreview>(Route("preview")))!;
        if (automatic) await Tick();
        else
        {
            using var response = await _client.PostAsJsonAsync(Route("publish"), new { preview.Confirmation });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(_key, text);
            Assert.DoesNotContain("StorageKey", text);
        }
        await Tick();
        using var repeat = await _client.PostAsJsonAsync(Route("publish"), new { preview.Confirmation });
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(3, _handler.Calls);
        Assert.Equal(_bytes, _handler.Upload);
        Assert.Contains("urn:li:image:SNAPSHOT", _handler.Post);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        var operation = await db.PublicationOperations.SingleAsync();
        Assert.Equal(PublicationOperationState.Confirmed, operation.State);
        Assert.Equal(_visualId, operation.Visual!.VisualId);
        Assert.Equal(_key, operation.Visual.StorageKey);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(_bytes)).ToLowerInvariant(), operation.Visual.Sha256);
        Assert.Equal(SocialPostStatus.Published, (await db.SocialPosts.SingleAsync()).Status);
        Assert.Equal(_bytes, await scope.ServiceProvider.GetRequiredService<IVisualStorage>().ReadAsync(_key));
    }
    [Theory]
    [InlineData("upload", 503, PublicationOperationState.Failed, 2)]
    [InlineData("post", 403, PublicationOperationState.Failed, 3)]
    [InlineData("post", 500, PublicationOperationState.Indeterminate, 3)]
    public async Task DurableFailureNeverRetriesOrFallsBack(string stage, int code, PublicationOperationState state, int calls)
    {
        _handler.FailStage = stage; _handler.Code = code;
        await Tick(); await Tick();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        var operation = await db.PublicationOperations.SingleAsync();
        Assert.Equal(state, operation.State);
        Assert.NotNull(operation.Visual);
        Assert.Equal(calls, _handler.Calls);
        if (stage == "upload") Assert.Null(_handler.Post);
        Assert.Equal(_bytes, await scope.ServiceProvider.GetRequiredService<IVisualStorage>().ReadAsync(_key));
    }
    [Fact]
    public async Task SnapshotSurvivesSelectionChangeAfterClaimAndProtectsMedia()
    {
        _handler.OnInitialize = async () =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
            Assert.Equal(_visualId, (await db.PublicationOperations.SingleAsync()).Visual!.VisualId);
            // Simulate an administrative database edit after claim; publisher must never re-read selection.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SocialPosts SET SelectedVisualId = NULL WHERE Id = {_postId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PostVisuals SET IsDeleted = 1, DeletedAt = {_clock.GetUtcNow().AddDays(-3)} WHERE Id = {_visualId}");
            await scope.ServiceProvider.GetRequiredService<IPostVisualStore>().CleanupAsync(_clock.GetUtcNow().AddDays(-1), default);
            Assert.True(File.Exists(Path.Combine(_root, "files", _key)));
        };
        await Tick();
        Assert.Equal(3, _handler.Calls);
        Assert.Equal(_bytes, _handler.Upload);
    }
    [Fact]
    public async Task ManualConfirmationRejectsChangedVisual()
    {
        var preview = (await _client.GetFromJsonAsync<LinkedInPreview>(Route("preview")))!;
        await using (var scope = _factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE SocialPosts SET SelectedVisualId = NULL WHERE Id = {_postId}");
        using var result = await _client.PostAsJsonAsync(Route("publish"), new { preview.Confirmation });
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal(0, _handler.Calls);
    }
    [Fact]
    public async Task ConcurrentManualAndWorkerShareTheSameClaim()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.OnInitialize = async () => { entered.SetResult(); await release.Task; };
        var preview = (await _client.GetFromJsonAsync<LinkedInPreview>(Route("preview")))!;
        var send = _client.PostAsJsonAsync(Route("publish"), new { preview.Confirmation });
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await Tick(); Assert.Equal(1, _handler.Calls); }
        finally { release.TrySetResult(); }
        using var result = await send;
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(3, _handler.Calls);
    }
    [Fact]
    public async Task MigrationPreservesExistingTextOnlyOperations()
    {
        await using var db = new SocialFlowDbContext(new DbContextOptionsBuilder<SocialFlowDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "old.db")};Pooling=False").Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261010005243_AddPostVisuals");
        var postId = Guid.NewGuid(); var opId = Guid.NewGuid(); var now = _clock.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO SocialPosts (Id,Title,Content,Platform,Status,CreatedAt,UpdatedAt,IsDeleted)
            VALUES ({postId},'Old','Old text',2,4,{now},{now},0)");
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO PublicationOperations
            (OperationId,SocialPostId,Provider,Destination,Content,Platform,State,ClaimedAt,UpdatedAt)
            VALUES ({opId},{postId},'LinkedIn','urn:li:person:member','Old text',2,3,{now},{now})");
        await migrator.MigrateAsync();
        var op = await db.PublicationOperations.SingleAsync();
        Assert.Equal(opId, op.OperationId); Assert.Null(op.Visual); Assert.Equal("Old text", op.Content);
        Assert.False(db.Database.HasPendingModelChanges());
    }
    [Theory]
    [InlineData("deleted")]
    [InlineData("unvalidated")]
    [InlineData("notready")]
    public async Task UnavailableSelectedVisualBlocksTheClaim(string reason)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        var sql = reason switch {
            "deleted" => "UPDATE PostVisuals SET IsDeleted = 1",
            "unvalidated" => "UPDATE PostVisuals SET ValidatedAt = NULL",
            _ => "UPDATE PostVisuals SET IsReady = 0"
        };
        await db.Database.ExecuteSqlRawAsync(sql);
        await Assert.ThrowsAsync<Redeemer.SocialFlow.Domain.Exceptions.DomainException>(() =>
            scope.ServiceProvider.GetRequiredService<IPublicationOperationStore>()
                .TryClaimAsync(new(_postId, Guid.NewGuid(), "LinkedIn", "urn:li:person:member")));
        Assert.Empty(await db.PublicationOperations.ToListAsync());
        Assert.Equal(0, _handler.Calls);
    }

    private sealed class Clock(DateTimeOffset? now = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now ?? new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
    private sealed class Credentials(Clock clock) : ILinkedInCredentialProvider
    {
        public Task<LinkedInMemberCredential?> GetAsync(CancellationToken token) => Task.FromResult<LinkedInMemberCredential?>(
            new() { Subject = "member", AccessToken = "test-only-token", ExpiresAt = clock.GetUtcNow().AddHours(1) });
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls, Code;
        public string? FailStage, Post;
        public byte[]? Upload;
        public Func<Task>? OnInitialize;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            var stage = request.RequestUri!.AbsolutePath == "/rest/images" ? "init" : request.Method == HttpMethod.Put ? "upload" : "post";
            if (stage == "upload") Upload = await request.Content!.ReadAsByteArrayAsync(token);
            if (stage == "post") Post = await request.Content!.ReadAsStringAsync(token);
            if (stage == FailStage) return new((HttpStatusCode)Code);
            if (stage == "init")
            {
                if (OnInitialize is not null) await OnInitialize();
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = new {
                    image = "urn:li:image:SNAPSHOT", uploadUrl = "https://www.linkedin.com/dms-uploads/image",
                    uploadUrlExpiresAt = new DateTimeOffset(2031,1,1,0,0,0,TimeSpan.Zero).ToUnixTimeMilliseconds() } }) };
            }
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            if (stage == "post") response.Headers.Add("x-restli-id", "urn:li:share:987");
            return response;
        }
    }
}
