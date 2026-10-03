using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Publishing;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Publishing;

public sealed class PublicationOperationTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"publication-{Guid.NewGuid():N}.db");
    private DbContextOptions<SocialFlowDbContext> _options = null!;
    private readonly Clock _clock = new(new DateTimeOffset(2030, 1, 2, 12, 0, 0, TimeSpan.Zero));
    private SocialFlowDbContext Db() => new(_options);
    private SqlitePublicationOperationStore Store() => new(_options, _clock);
    private static PublishScheduledPostRequest Request(Guid postId) => new(postId, Guid.NewGuid(), "TestProvider", "opaque-target");

    public async Task InitializeAsync()
    {
        _options = new DbContextOptionsBuilder<SocialFlowDbContext>()
            .UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=5").Options;
        await using var db = Db();
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
        return Task.CompletedTask;
    }
    private async Task<Guid> Seed(DateTimeOffset? due = null)
    {
        var post = SocialPost.Create("Title", "Snapshot é\ncontent", SocialPlatform.LinkedIn, new Clock(_clock.Now.AddDays(-2)));
        post.SubmitForReview();
        post.Approve();
        post.Schedule(due ?? _clock.Now);
        await using var db = Db();
        db.SocialPosts.Add(post);
        await db.SaveChangesAsync();
        return post.Id;
    }

    [Theory]
    [InlineData(PostPublicationOutcome.Confirmed)]
    [InlineData(PostPublicationOutcome.Failed)]
    [InlineData(PostPublicationOutcome.Indeterminate)]
    public async Task Result_IsDurable_AndAllRepeatedCallsAreReadOnly(PostPublicationOutcome outcome)
    {
        var id = await Seed(_clock.Now.ToOffset(TimeSpan.FromHours(-8))); // equal instant, different offset
        var request = Request(id);
        var claimedAt = _clock.Now;
        using var cancellation = new CancellationTokenSource();
        var publisher = new Publisher(async (sent, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            // An independent context observes the committed claim before the call.
            await using var read = Db();
            var operation = await read.PublicationOperations.SingleAsync();
            Assert.Equal(PublicationOperationState.Indeterminate, operation.State);
            Assert.Null(operation.CompletedAt);
            Assert.Equal(operation.OperationId, sent.OperationId);
            Assert.Equal(operation.Content, sent.Content);
            Assert.Equal(operation.Platform, sent.Platform);
            Assert.Equal(operation.Destination, sent.Destination);
            Assert.Equal("TestProvider", operation.Provider);
            Assert.Equal(SocialPostStatus.Scheduled, (await read.SocialPosts.SingleAsync()).Status);
            // Independent write also succeeds: no transaction spans this external call.
            await read.Database.ExecuteSqlRawAsync("CREATE TABLE ProviderCallProbe (Id INTEGER);");
            _clock.Now = _clock.Now.AddMinutes(1);
            return outcome switch
            {
                PostPublicationOutcome.Confirmed => PostPublicationResult.Confirmed("external-id"),
                PostPublicationOutcome.Failed => PostPublicationResult.Failed(),
                _ => PostPublicationResult.Indeterminate()
            };
        });
        var result = await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request, cancellation.Token);
        Assert.Equal(claimedAt, result.ClaimedAt);
        Assert.Equal(_clock.Now, result.CompletedAt);
        Assert.Equal(_clock.Now, result.UpdatedAt);
        Assert.Equal("Snapshot é\ncontent", result.Content);
        var repeated = await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request);
        var changedIdentity = await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request with
        { OperationId = Guid.NewGuid(), Provider = "OtherProvider", Destination = "other-target" });
        Assert.Equal(result, repeated);
        Assert.Equal(result, changedIdentity);
        Assert.Equal(1, publisher.Calls);
        await using var verify = Db();
        var post = await verify.SocialPosts.SingleAsync();
        var stored = await verify.PublicationOperations.SingleAsync();
        var expectedState = outcome switch
        {
            PostPublicationOutcome.Confirmed => PublicationOperationState.Confirmed,
            PostPublicationOutcome.Failed => PublicationOperationState.Failed,
            _ => PublicationOperationState.Indeterminate
        };
        Assert.Equal(expectedState, stored.State);
        Assert.Equal(outcome == PostPublicationOutcome.Confirmed ? "external-id" : null, stored.ExternalId);
        Assert.Equal(outcome == PostPublicationOutcome.Confirmed ? SocialPostStatus.Published :
            outcome == PostPublicationOutcome.Failed ? SocialPostStatus.Failed : SocialPostStatus.Scheduled, post.Status);
        Assert.Equal(outcome == PostPublicationOutcome.Confirmed ? _clock.Now : (DateTimeOffset?)null, post.PublishedAt);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("cancel")]
    [InlineData("error")]
    public async Task ExceptionAfterPossibleSend_RemainsIndeterminateWithoutRetry(string kind)
    {
        var request = Request(await Seed());
        var publisher = new Publisher((_, _) => throw kind switch
        {
            "timeout" => new TimeoutException("secret-response"),
            "cancel" => new OperationCanceledException(),
            _ => new InvalidOperationException("secret-response")
        });
        var result = await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request);
        Assert.Equal(PublicationOperationState.Indeterminate, result.State);
        Assert.NotNull(result.CompletedAt);
        Assert.Equal(result, await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request));
        Assert.Equal(1, publisher.Calls);
        await using var db = Db();
        Assert.Equal(SocialPostStatus.Scheduled, (await db.SocialPosts.SingleAsync()).Status);
    }

    [Fact]
    public async Task CancellationAfterConfirmedResponse_StillPersistsConfirmation()
    {
        var request = Request(await Seed());
        using var cancellation = new CancellationTokenSource();
        var publisher = new Publisher((_, _) => { cancellation.Cancel(); return Task.FromResult(PostPublicationResult.Confirmed("external")); });
        var result = await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request, cancellation.Token);
        Assert.Equal(PublicationOperationState.Confirmed, result.State);
        await using var db = Db();
        Assert.Equal(SocialPostStatus.Published, (await db.SocialPosts.SingleAsync()).Status);
    }

    [Fact]
    public async Task FutureInstantAndPreCancelledRequest_DoNotClaimOrSend()
    {
        var request = Request(await Seed(_clock.Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(-10))));
        var publisher = new Publisher((_, _) => throw new Exception("Must not send"));
        var useCase = new PublishScheduledPost(Store(), publisher);
        await Assert.ThrowsAsync<DomainException>(() => useCase.ExecuteAsync(request));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.ExecuteAsync(request, cancellation.Token));
        await using var db = Db();
        Assert.Empty(await db.PublicationOperations.ToListAsync());
        Assert.Equal(0, publisher.Calls);
    }

    [Fact]
    public async Task ConcurrentContexts_OnlyOneOwnerCallsPublisher()
    {
        var request = Request(await Seed());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new Publisher(async (_, _) => { entered.TrySetResult(); await release.Task; return PostPublicationResult.Confirmed("external"); });
        // Each store creates its own independent context/connection for every transaction.
        var first = Task.Run(async () => { await start.Task; return await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request); });
        var second = Task.Run(async () => { await start.Task; return await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request with { OperationId = Guid.NewGuid() }); });
        try
        {
            start.SetResult();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var observer = await Task.WhenAny(first, second).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(PublicationOperationState.Indeterminate, (await observer).State);
            Assert.Equal(1, publisher.Calls);
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15));
        await using var db = Db();
        Assert.Single(await db.PublicationOperations.ToListAsync());
        Assert.Equal(SocialPostStatus.Published, (await db.SocialPosts.SingleAsync()).Status);
        Assert.Equal(1, publisher.Calls);
    }

    [Fact]
    public async Task RestartAfterClaim_NeverDispatchesInterruptedOperation()
    {
        var request = Request(await Seed());
        var claim = await Store().TryClaimAsync(request); // simulate process loss before receiving any result
        Assert.True(claim.Acquired);
        var publisher = new Publisher((_, _) => throw new Exception("Must not resend"));
        var result = await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request);
        Assert.Equal(claim.Operation, result);
        Assert.Equal(PublicationOperationState.Indeterminate, result.State);
        Assert.Null(result.CompletedAt);
        Assert.Equal(0, publisher.Calls);
    }

    [Fact]
    public async Task FinalSaveFailure_RollsBackPublishedState_AndRestartDoesNotResend()
    {
        var request = Request(await Seed());
        await using (var setup = Db())
            await setup.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailConfirmation BEFORE UPDATE ON PublicationOperations BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        var publisher = new Publisher((_, _) => Task.FromResult(PostPublicationResult.Confirmed("external")));
        await Assert.ThrowsAsync<DbUpdateException>(() => new PublishScheduledPost(Store(), publisher).ExecuteAsync(request));
        var result = await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request);
        Assert.Equal(PublicationOperationState.Indeterminate, result.State);
        Assert.Null(result.ExternalId);
        Assert.Null(result.CompletedAt);
        Assert.Equal(1, publisher.Calls);
        await using var db = Db();
        var post = await db.SocialPosts.SingleAsync();
        Assert.Equal(SocialPostStatus.Scheduled, post.Status);
        Assert.Null(post.PublishedAt);
    }

    [Fact]
    public async Task StaleCancellationCannotOverwriteConfirmedPost()
    {
        var request = Request(await Seed());
        await using var stale = Db();
        var post = await stale.SocialPosts.SingleAsync();
        var publisher = new Publisher((_, _) => Task.FromResult(PostPublicationResult.Confirmed("external")));
        await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request);
        post.Cancel();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var read = Db();
        Assert.Equal(SocialPostStatus.Published, (await read.SocialPosts.SingleAsync()).Status);
    }

    [Fact]
    public async Task OperationIdCannotBeReusedForAnotherPost()
    {
        var first = Request(await Seed());
        await Store().TryClaimAsync(first);
        var second = first with { PostId = await Seed() };
        await Assert.ThrowsAsync<DomainException>(() => Store().TryClaimAsync(second));
        await using var db = Db();
        Assert.Single(await db.PublicationOperations.ToListAsync());
    }

    [Fact]
    public async Task CancellationOfPostDuringSend_DoesNotInventAForbiddenTransitionOrResend()
    {
        var request = Request(await Seed());
        var publisher = new Publisher(async (_, _) =>
        {
            await using var concurrent = Db();
            (await concurrent.SocialPosts.SingleAsync()).Cancel();
            await concurrent.SaveChangesAsync();
            return PostPublicationResult.Confirmed("external");
        });
        await Assert.ThrowsAsync<DomainException>(() => new PublishScheduledPost(Store(), publisher).ExecuteAsync(request));
        var recovery = await new PublishScheduledPost(Store(), publisher).ExecuteAsync(request);
        Assert.Equal(PublicationOperationState.Indeterminate, recovery.State);
        Assert.Null(recovery.CompletedAt);
        Assert.Equal(1, publisher.Calls);
        await using var read = Db();
        Assert.Equal(SocialPostStatus.Cancelled, (await read.SocialPosts.SingleAsync()).Status);
    }

    [Fact]
    public async Task MigrationPreservesExistingPostAndMatchesModel()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SocialFlowDbContext(new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261002215122_AddKnowledgeBase");
        var post = SocialPost.Create("Existing", "Content", SocialPlatform.LinkedIn);
        db.SocialPosts.Add(post);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(post.Id, (await db.SocialPosts.SingleAsync()).Id);
        Assert.Empty(await db.PublicationOperations.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(201, PublicationOperationState.Confirmed, SocialPostStatus.Published)]
    [InlineData(403, PublicationOperationState.Failed, SocialPostStatus.Failed)]
    [InlineData(503, PublicationOperationState.Indeterminate, SocialPostStatus.Scheduled)]
    public async Task LinkedInAdapterUsesDurableSnapshotAndNeverResends(int status, PublicationOperationState expected, SocialPostStatus postStatus)
    {
        var request = new PublishScheduledPostRequest(await Seed(), Guid.NewGuid(), "LinkedIn", "urn:li:person:member");
        using var handler = new LinkedInHandler(status);
        using var http = new HttpClient(handler);
        var adapter = new Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn.LinkedInPostPublisher(http, new LinkedInCredentials(_clock), _clock);
        var result = await new PublishScheduledPost(Store(), adapter).ExecuteAsync(request);
        Assert.Equal(expected, result.State);
        Assert.Equal(result, await new PublishScheduledPost(Store(), adapter).ExecuteAsync(request));
        Assert.Equal(1, handler.Calls);
        await using var db = Db();
        var operation = await db.PublicationOperations.SingleAsync();
        using var sent = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Assert.Equal(operation.Content, sent.RootElement.GetProperty("commentary").GetString());
        Assert.Equal(operation.Destination, sent.RootElement.GetProperty("author").GetString());
        Assert.Equal(postStatus, (await db.SocialPosts.SingleAsync()).Status);
        Assert.Equal(status == 201 ? "urn:li:share:123" : null, operation.ExternalId);
    }

    private sealed class LinkedInCredentials(TimeProvider clock) : Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn.ILinkedInCredentialProvider
    {
        public Task<Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn.LinkedInMemberCredential?> GetAsync(CancellationToken cancellationToken)
            => Task.FromResult<Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn.LinkedInMemberCredential?>(new()
            { AccessToken = "test-token", Subject = "member", ExpiresAt = clock.GetUtcNow().AddHours(1) });
    }
    private sealed class LinkedInHandler(int status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage((System.Net.HttpStatusCode)status);
            response.Headers.Add("x-restli-id", "urn:li:share:123");
            return response;
        }
    }

    private sealed class Publisher(Func<PostPublicationRequest, CancellationToken, Task<PostPublicationResult>> send) : IPostPublisher
    {
        private int _calls;
        public int Calls => _calls;
        public Task<PostPublicationResult> PublishAsync(PostPublicationRequest request, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref _calls); return send(request, cancellationToken); }
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
