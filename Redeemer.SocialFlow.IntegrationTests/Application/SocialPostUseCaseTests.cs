using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Application;

public sealed class SocialPostUseCaseTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _provider = null!;
    private static readonly DateTimeOffset InitialTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<SocialFlowDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ISocialFlowDbContext>(sp => sp.GetRequiredService<SocialFlowDbContext>());
        services.AddApplication();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task CreateGetUpdate_ReturnDtosAndPersistAllFields()
    {
        var created = await Run(service => service.CreateAsync(new(" Title ", null, SocialPlatform.Instagram)));
        Assert.Equal("Title", created.Title);
        Assert.Equal(string.Empty, created.Content);
        Assert.Equal(SocialPostStatus.Draft, created.Status);
        Assert.Equal(created, await Run(service => service.GetByIdAsync(created.Id)));

        var updated = await Run(service => service.UpdateAsync(created.Id,
            new(" Revised ", " Text ", " CTA ", " Brief ", " https://example.com/a.png ")));
        Assert.Equal("Revised", updated.Title);
        Assert.Equal("Text", updated.Content);
        Assert.Equal("CTA", updated.CallToAction);
        Assert.Equal("Brief", updated.VisualBrief);
        Assert.Equal("https://example.com/a.png", updated.VisualUrl);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal(created.Platform, updated.Platform);
        Assert.Equal(updated, await Run(service => service.GetByIdAsync(created.Id)));

        var cleared = await Run(service => service.UpdateAsync(created.Id, new("Revised", null)));
        Assert.Equal(string.Empty, cleared.Content);
        Assert.Null(cleared.CallToAction);
        Assert.Null(cleared.VisualBrief);
        Assert.Null(cleared.VisualUrl);
        Assert.Equal(cleared, await Run(service => service.GetByIdAsync(created.Id)));
    }

    [Theory]
    [InlineData("", SocialPlatform.Facebook)]
    [InlineData("   ", SocialPlatform.Facebook)]
    [InlineData(null, SocialPlatform.Facebook)]
    [InlineData("Title", (SocialPlatform)999)]
    public async Task InvalidCreate_UsesDomainValidationAndWritesNothing(string? title, SocialPlatform platform)
    {
        await Assert.ThrowsAsync<DomainException>(() => Run(service => service.CreateAsync(new(title!, "Content", platform))));
        Assert.Empty(await Run(service => service.ListAsync()));
    }

    [Fact]
    public async Task ReviewWorkflow_SupportsRejectionRevisionResubmissionAndCancellation()
    {
        var post = await Run(service => service.CreateAsync(new("Title", "Content", SocialPlatform.LinkedIn)));
        await Run(service => service.SubmitForReviewAsync(post.Id));
        await Run(service => service.RejectAsync(post.Id));
        await Run(service => service.UpdateAsync(post.Id, new("Revised", "New content")));
        await Run(service => service.SubmitForReviewAsync(post.Id));
        await Run(service => service.ApproveAsync(post.Id));
        var date = DateTimeOffset.UtcNow.AddDays(2).ToOffset(TimeSpan.FromHours(5.5)).AddTicks(123);
        var scheduled = await Run(service => service.ScheduleAsync(post.Id, new(date)));
        Assert.Equal(SocialPostStatus.Scheduled, scheduled.Status);
        var persisted = await Run(service => service.GetByIdAsync(post.Id));
        Assert.Equal(date.ToString("O"), persisted!.ScheduledAt?.ToString("O"));
        var cancelled = await Run(service => service.CancelAsync(post.Id));
        Assert.Equal(SocialPostStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.ScheduledAt);
        Assert.Equal(cancelled, await Run(service => service.GetByIdAsync(post.Id)));
    }

    public static IEnumerable<object[]> WorkflowCases()
    {
        foreach (var status in Enum.GetValues<SocialPostStatus>())
        foreach (var operation in Enum.GetValues<Mutation>())
            yield return new object[] { status, operation };
    }

    [Theory]
    [MemberData(nameof(WorkflowCases))]
    public async Task Mutations_EnforceEveryStateAndPersistOnlySuccessfulChanges(SocialPostStatus status, Mutation operation)
    {
        var post = await Seed(status);
        var before = await Run(service => service.GetByIdAsync(post.Id));
        var expected = (status, operation) switch
        {
            (SocialPostStatus.Draft or SocialPostStatus.Rejected, Mutation.Update) => status,
            (SocialPostStatus.Draft or SocialPostStatus.Rejected, Mutation.Delete) => status,
            (SocialPostStatus.Draft or SocialPostStatus.Rejected, Mutation.Submit) => SocialPostStatus.ReadyForReview,
            (SocialPostStatus.ReadyForReview, Mutation.Approve) => SocialPostStatus.Approved,
            (SocialPostStatus.ReadyForReview, Mutation.Reject) => SocialPostStatus.Rejected,
            (SocialPostStatus.Approved, Mutation.Schedule) => SocialPostStatus.Scheduled,
            (SocialPostStatus.Scheduled, Mutation.Cancel) => SocialPostStatus.Cancelled,
            _ => (SocialPostStatus?)null
        };

        if (expected is null)
        {
            await Assert.ThrowsAsync<DomainException>(() => Run(service => Apply(service, post.Id, operation)));
            Assert.Equal(before, await Run(service => service.GetByIdAsync(post.Id)));
        }
        else
        {
            var result = await Run(service => Apply(service, post.Id, operation));
            var saved = await Run(service => service.GetByIdAsync(post.Id));
            if (operation == Mutation.Delete) Assert.Null(saved);
            else
            {
                Assert.Equal(expected, saved!.Status);
                Assert.Equal(result, saved);
            }
        }
    }

    [Fact]
    public async Task InvalidContentTitleAndSchedule_DoNotChangePersistedPost()
    {
        var draft = await Run(service => service.CreateAsync(new("Title", null, SocialPlatform.Facebook)));
        await Assert.ThrowsAsync<DomainException>(() => Run(service => service.SubmitForReviewAsync(draft.Id)));
        await Assert.ThrowsAsync<DomainException>(() => Run(service => service.UpdateAsync(draft.Id, new(" ", "Changed"))));
        Assert.Equal(draft, await Run(service => service.GetByIdAsync(draft.Id)));

        var approved = await Seed(SocialPostStatus.Approved);
        var before = await Run(service => service.GetByIdAsync(approved.Id));
        await Assert.ThrowsAsync<DomainException>(() => Run(service => service.ScheduleAsync(approved.Id, new(DateTimeOffset.UtcNow.AddMinutes(-1)))));
        Assert.Equal(before, await Run(service => service.GetByIdAsync(approved.Id)));
    }

    [Fact]
    public async Task MissingGet_ReturnsNullAndEmptyListReturnsNoPosts()
    {
        Assert.Null(await Run(service => service.GetByIdAsync(Guid.NewGuid())));
        Assert.Empty(await Run(service => service.ListAsync()));
    }

    [Theory]
    [InlineData(Mutation.Update)]
    [InlineData(Mutation.Delete)]
    [InlineData(Mutation.Submit)]
    [InlineData(Mutation.Approve)]
    [InlineData(Mutation.Reject)]
    [InlineData(Mutation.Schedule)]
    [InlineData(Mutation.Cancel)]
    public async Task MissingMutation_ThrowsApplicationNotFound(Mutation operation)
    {
        var id = Guid.NewGuid();
        var exception = await Assert.ThrowsAsync<PostNotFoundException>(() => Run(service => Apply(service, id, operation)));
        Assert.Equal(id, exception.PostId);
        Assert.Empty(await Run(service => service.ListAsync()));
    }

    [Fact]
    public async Task List_FiltersIndividuallyAndTogetherWithInclusiveOffsetAwareBounds()
    {
        var early = await Seed(SocialPostStatus.Draft, SocialPlatform.Facebook, InitialTime.AddTicks(-1));
        var match = await Seed(SocialPostStatus.Draft, SocialPlatform.Facebook, InitialTime);
        var late = await Seed(SocialPostStatus.Rejected, SocialPlatform.LinkedIn, InitialTime.AddTicks(1));
        var sameTime = await Seed(SocialPostStatus.Rejected, SocialPlatform.Facebook, InitialTime.ToOffset(TimeSpan.FromHours(5.5)));

        var all = await Run(service => service.ListAsync());
        Assert.Equal(new[] { late.Id }.Concat(new[] { match.Id, sameTime.Id }.Order()).Append(early.Id), all.Select(p => p.Id));
        Assert.Equal(3, (await Run(service => service.ListAsync(new(Platform: SocialPlatform.Facebook)))).Count);
        Assert.Equal(2, (await Run(service => service.ListAsync(new(Status: SocialPostStatus.Rejected)))).Count);
        Assert.Equal(3, (await Run(service => service.ListAsync(new(CreatedFrom: InitialTime)))).Count);
        Assert.Equal(3, (await Run(service => service.ListAsync(new(CreatedTo: InitialTime)))).Count);
        var filtered = await Run(service => service.ListAsync(new(SocialPlatform.Facebook, SocialPostStatus.Draft,
            InitialTime.ToOffset(TimeSpan.FromHours(14)), InitialTime.ToOffset(TimeSpan.FromHours(-12)))));
        Assert.Equal(match.Id, Assert.Single(filtered).Id);
        Assert.Empty(await Run(service => service.ListAsync(new(Platform: SocialPlatform.Instagram))));
        Assert.Empty(await Run(service => service.ListAsync(new(CreatedFrom: InitialTime.AddDays(1)))));
        await Assert.ThrowsAsync<ArgumentException>(() => Run(service => service.ListAsync(new(CreatedFrom: InitialTime.AddTicks(1), CreatedTo: InitialTime))));
    }

    [Fact]
    public async Task ReadOperations_DoNotTrackEntitiesAndServiceIsScoped()
    {
        var post = await Seed(SocialPostStatus.Draft);
        await using var scope = _provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ISocialPostService>();
        Assert.Same(service, scope.ServiceProvider.GetRequiredService<ISocialPostService>());
        await service.GetByIdAsync(post.Id);
        await service.ListAsync();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().ChangeTracker.Entries());
        await using var other = _provider.CreateAsyncScope();
        Assert.NotSame(service, other.ServiceProvider.GetRequiredService<ISocialPostService>());
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Get")]
    [InlineData("List")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("Submit")]
    [InlineData("Approve")]
    [InlineData("Reject")]
    [InlineData("Schedule")]
    [InlineData("Cancel")]
    public async Task EveryOperation_RespectsCancellationWithoutChangingPersistence(string operation)
    {
        var post = await Seed(SocialPostStatus.Draft);
        var before = await Run(service => service.GetByIdAsync(post.Id));
        using var source = new CancellationTokenSource();
        source.Cancel();
        await using var scope = _provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ISocialPostService>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            switch (operation)
            {
                case "Create": await service.CreateAsync(new("Title", "Content", SocialPlatform.Facebook), source.Token); break;
                case "Get": await service.GetByIdAsync(post.Id, source.Token); break;
                case "List": await service.ListAsync(cancellationToken: source.Token); break;
                default: await Apply(service, post.Id, Enum.Parse<Mutation>(operation), source.Token); break;
            }
        });
        Assert.Empty(scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().ChangeTracker.Entries());
        Assert.Equal(before, Assert.Single(await Run(s => s.ListAsync())));
    }

    [Fact]
    public async Task SaveChanges_ReceivesCallerCancellationToken()
    {
        await using var scope = _provider.CreateAsyncScope();
        var recording = new RecordingContext(scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>());
        var service = new SocialPostService(recording);
        using var source = new CancellationTokenSource();
        var post = await service.CreateAsync(new("Title", "Content", SocialPlatform.Facebook), source.Token);
        await service.UpdateAsync(post.Id, new("Updated", "Content"), source.Token);
        await service.SubmitForReviewAsync(post.Id, source.Token);
        await service.RejectAsync(post.Id, source.Token);
        await service.SubmitForReviewAsync(post.Id, source.Token);
        await service.ApproveAsync(post.Id, source.Token);
        await service.ScheduleAsync(post.Id, new(DateTimeOffset.UtcNow.AddDays(1)), source.Token);
        await service.CancelAsync(post.Id, source.Token);
        var draft = await service.CreateAsync(new("Delete", null, SocialPlatform.Facebook), source.Token);
        await service.DeleteAsync(draft.Id, source.Token);
        Assert.Equal(10, recording.Tokens.Count);
        Assert.All(recording.Tokens, token => Assert.Equal(source.Token, token));
    }

    private async Task<T> Run<T>(Func<ISocialPostService, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ISocialPostService>());
    }

    private async Task<SocialPost> Seed(SocialPostStatus status, SocialPlatform platform = SocialPlatform.Facebook, DateTimeOffset? createdAt = null)
    {
        var clock = new FixedClock(createdAt ?? InitialTime);
        var post = SocialPost.Create("Title", "Content", platform, clock);
        if (status != SocialPostStatus.Draft)
        {
            post.SubmitForReview();
            if (status == SocialPostStatus.Rejected) post.Reject();
            else if (status != SocialPostStatus.ReadyForReview)
            {
                post.Approve();
                if (status != SocialPostStatus.Approved)
                {
                    post.Schedule(clock.GetUtcNow().AddDays(1));
                    if (status == SocialPostStatus.Published) post.MarkAsPublished();
                    if (status == SocialPostStatus.Failed) post.MarkAsFailed();
                    if (status == SocialPostStatus.Cancelled) post.Cancel();
                }
            }
        }
        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        context.SocialPosts.Add(post);
        await context.SaveChangesAsync();
        return post;
    }

    private static async Task<SocialPostDto?> Apply(ISocialPostService service, Guid id, Mutation operation, CancellationToken token = default)
    {
        if (operation == Mutation.Delete)
        {
            await service.DeleteAsync(id, token);
            return null;
        }
        return operation switch
        {
            Mutation.Update => await service.UpdateAsync(id, new("Updated", "Updated content"), token),
            Mutation.Submit => await service.SubmitForReviewAsync(id, token),
            Mutation.Approve => await service.ApproveAsync(id, token),
            Mutation.Reject => await service.RejectAsync(id, token),
            Mutation.Schedule => await service.ScheduleAsync(id, new(DateTimeOffset.UtcNow.AddDays(1)), token),
            Mutation.Cancel => await service.CancelAsync(id, token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null) await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public enum Mutation { Update, Delete, Submit, Approve, Reject, Schedule, Cancel }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingContext(SocialFlowDbContext inner) : ISocialFlowDbContext
    {
        public List<CancellationToken> Tokens { get; } = [];
        public DbSet<SocialPost> SocialPosts => inner.SocialPosts;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            return inner.SaveChangesAsync(cancellationToken);
        }
    }
}
