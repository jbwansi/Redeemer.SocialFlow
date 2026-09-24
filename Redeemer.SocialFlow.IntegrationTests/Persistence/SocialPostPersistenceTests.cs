using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Persistence;

public sealed class SocialPostPersistenceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"socialflow-tests-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:SocialFlow", $"Data Source={_databasePath};Pooling=False"));

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        await context.Database.MigrateAsync();
    }

    [Theory]
    [InlineData(SocialPlatform.Facebook)]
    [InlineData(SocialPlatform.LinkedIn)]
    [InlineData(SocialPlatform.Instagram)]
    public async Task Draft_RoundTripsWithEmptyContentAndNullOptionalFields(SocialPlatform platform)
    {
        var post = SocialPost.Create(" Draft title ", null, platform);
        await SaveNewPost(post);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ISocialFlowDbContext>();
        var loaded = await context.SocialPosts.AsNoTracking().SingleAsync(p => p.Id == post.Id);

        Assert.NotSame(post, loaded);
        AssertEquivalent(post, loaded);
        Assert.Equal(string.Empty, loaded.Content);
        Assert.Throws<DomainException>(loaded.SubmitForReview);
    }

    [Fact]
    public async Task PublishedPost_RoundTripsEveryPropertyAndPreservesInvariants()
    {
        var clock = new TestClock();
        var post = SocialPost.Create("Publication", "Content with Unicode: é 漢字", SocialPlatform.LinkedIn, clock);
        post.Update(post.Title, post.Content, "Read more", "A bright landscape", "https://example.com/image.png");
        post.SubmitForReview();
        post.Approve();
        post.Schedule(clock.Now.AddDays(1).ToOffset(TimeSpan.FromHours(5.5)));
        clock.Now = clock.Now.AddDays(1);
        post.MarkAsPublished();
        await SaveNewPost(post);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ISocialFlowDbContext>();
        var loaded = await context.SocialPosts.SingleAsync(p => p.Id == post.Id);

        AssertEquivalent(post, loaded);
        Assert.Throws<DomainException>(() => loaded.Update("Changed", "Content", null, null));
        Assert.Throws<DomainException>(loaded.Approve);
        Assert.Throws<DomainException>(loaded.Cancel);
        Assert.Equal(0, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task LoadedPost_CanBeUpdatedAndScheduledUsingItsInitializedClock()
    {
        var post = SocialPost.Create("Original", "Original content", SocialPlatform.Instagram, new TestClock());
        await SaveNewPost(post);
        var scheduledAt = DateTimeOffset.UtcNow.AddDays(2);
        var beforeUpdate = DateTimeOffset.UtcNow;
        DateTimeOffset updatedAt;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ISocialFlowDbContext>();
            var loaded = await context.SocialPosts.SingleAsync(p => p.Id == post.Id);
            loaded.Update(" Revised ", " Revised content ", " Learn more ", " New visual ");
            loaded.SubmitForReview();
            loaded.Approve();
            Assert.Throws<DomainException>(() => loaded.Schedule(DateTimeOffset.UtcNow.AddMinutes(-1)));
            loaded.Schedule(scheduledAt);
            updatedAt = loaded.UpdatedAt;
            Assert.InRange(updatedAt, beforeUpdate, DateTimeOffset.UtcNow);
            Assert.Equal(1, await context.SaveChangesAsync());
        }

        await using var readScope = _factory.Services.CreateAsyncScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<ISocialFlowDbContext>();
        var saved = await readContext.SocialPosts.AsNoTracking().SingleAsync(p => p.Id == post.Id);
        Assert.Equal("Revised", saved.Title);
        Assert.Equal("Revised content", saved.Content);
        Assert.Equal("Learn more", saved.CallToAction);
        Assert.Equal("New visual", saved.VisualBrief);
        Assert.Equal(SocialPostStatus.Scheduled, saved.Status);
        Assert.Equal(scheduledAt, saved.ScheduledAt);
        Assert.Equal(updatedAt, saved.UpdatedAt);
        Assert.Equal(post.CreatedAt, saved.CreatedAt);
    }

    [Fact]
    public async Task Api_UsesConfiguredSqliteDatabaseAndSharesContextWithinScope()
    {
        await using var firstScope = _factory.Services.CreateAsyncScope();
        var context = firstScope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        Assert.Same(context, firstScope.ServiceProvider.GetRequiredService<ISocialFlowDbContext>());
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
        Assert.Equal(_databasePath, context.Database.GetDbConnection().DataSource);
        Assert.True(File.Exists(_databasePath));
        var migration = Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        Assert.EndsWith("_InitialCreate", migration);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());

        await using var secondScope = _factory.Services.CreateAsyncScope();
        Assert.NotSame(context, secondScope.ServiceProvider.GetRequiredService<ISocialFlowDbContext>());
    }

    private async Task SaveNewPost(SocialPost post)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ISocialFlowDbContext>();
        context.SocialPosts.Add(post);
        Assert.Equal(1, await context.SaveChangesAsync());
    }

    private static void AssertEquivalent(SocialPost expected, SocialPost actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Content, actual.Content);
        Assert.Equal(expected.Platform, actual.Platform);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.CallToAction, actual.CallToAction);
        Assert.Equal(expected.VisualBrief, actual.VisualBrief);
        Assert.Equal(expected.VisualUrl, actual.VisualUrl);
        Assert.Equal(expected.CreatedAt.ToString("O"), actual.CreatedAt.ToString("O"));
        Assert.Equal(expected.UpdatedAt.ToString("O"), actual.UpdatedAt.ToString("O"));
        Assert.Equal(expected.ScheduledAt?.ToString("O"), actual.ScheduledAt?.ToString("O"));
        Assert.Equal(expected.PublishedAt?.ToString("O"), actual.PublishedAt?.ToString("O"));
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(_databasePath + suffix);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(2)).AddTicks(1234567);
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }
}
