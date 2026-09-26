using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Application;

public sealed class GenerateSocialPostDraftTests
{
    private static readonly GenerateContentRequest Request = new("Subject", "Objective", "Audience", SocialPlatform.LinkedIn);
    private static readonly GeneratedContent Content = new(" Title ", " Body ", " Act ", " Image ", ["Review this draft"]);

    [Theory]
    [InlineData(SocialPlatform.Facebook)]
    [InlineData(SocialPlatform.LinkedIn)]
    [InlineData(SocialPlatform.Instagram)]
    public async Task Execute_MapsGeneratedFieldsAndSavesOnlyADraft(SocialPlatform platform)
    {
        var context = new RecordingContext();
        var request = Request with { Platform = platform };
        using var cancellation = new CancellationTokenSource();
        var generator = new StubGenerator((received, token) =>
        {
            Assert.Same(request, received);
            Assert.Equal(cancellation.Token, token);
            Assert.Empty(context.Posts.Added);
            return Task.FromResult(Content);
        });

        var applicationResult = await new GenerateSocialPostDraft(generator, context).ExecuteAsync(request, cancellation.Token);
        var result = applicationResult.Post;
        Assert.Same(Content.Warnings, applicationResult.Warnings);

        var saved = Assert.Single(context.Posts.Added);
        Assert.Equal(saved.Id, result.Id);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Title", result.Title);
        Assert.Equal("Body", result.Content);
        Assert.Equal("Act", result.CallToAction);
        Assert.Equal("Image", result.VisualBrief);
        Assert.Equal(platform, result.Platform);
        Assert.Equal(SocialPostStatus.Draft, saved.Status);
        Assert.Equal(SocialPostStatus.Draft, result.Status);
        Assert.Null(result.ScheduledAt);
        Assert.Null(result.PublishedAt);
        Assert.Null(result.VisualUrl);
        Assert.Equal(cancellation.Token, Assert.Single(context.SaveTokens));
        Assert.Equal(1, generator.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Execute_PreservesWarningsExactly(bool empty)
    {
        IReadOnlyList<string> warnings = empty ? Array.Empty<string>() :
            new[] { " Review this draft ", "", "Second warning", " Review this draft " };
        var generated = Content with { Warnings = warnings };
        var context = new RecordingContext();
        var generator = new StubGenerator((_, _) => Task.FromResult(generated));

        var result = await new GenerateSocialPostDraft(generator, context).ExecuteAsync(Request);

        Assert.Same(warnings, result.Warnings);
        Assert.Equal(warnings, result.Warnings);
        Assert.Equal(SocialPostStatus.Draft, result.Post.Status);
        Assert.Single(context.Posts.Added);
        Assert.Single(context.SaveTokens);
    }

    [Fact]
    public async Task GenerationFailure_DoesNotAddOrSave()
    {
        var context = new RecordingContext();
        var failure = new InvalidOperationException("Generation failed");
        var generator = new StubGenerator((_, _) => Task.FromException<GeneratedContent>(failure));
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GenerateSocialPostDraft(generator, context).ExecuteAsync(Request));
        Assert.Same(failure, thrown);
        Assert.Empty(context.Posts.Added);
        Assert.Empty(context.SaveTokens);
    }

    [Theory]
    [InlineData(" ", SocialPlatform.LinkedIn)]
    [InlineData("Title", (SocialPlatform)999)]
    public async Task DomainValidationFailure_DoesNotAddOrSave(string title, SocialPlatform platform)
    {
        var context = new RecordingContext();
        var generator = new StubGenerator((_, _) => Task.FromResult(Content with { Title = title }));
        await Assert.ThrowsAsync<DomainException>(() => new GenerateSocialPostDraft(generator, context)
            .ExecuteAsync(Request with { Platform = platform }));
        Assert.Empty(context.Posts.Added);
        Assert.Empty(context.SaveTokens);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationBeforeOrDuringGeneration_DoesNotAddOrSave(bool cancelBefore)
    {
        var context = new RecordingContext();
        using var cancellation = new CancellationTokenSource();
        var generator = new StubGenerator((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(Content);
        });
        if (cancelBefore) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GenerateSocialPostDraft(generator, context).ExecuteAsync(Request, cancellation.Token));
        Assert.Equal(cancelBefore ? 0 : 1, generator.Calls);
        Assert.Empty(context.Posts.Added);
        Assert.Empty(context.SaveTokens);
    }

    private sealed class StubGenerator(Func<GenerateContentRequest, CancellationToken, Task<GeneratedContent>> generate) : IContentGenerator
    {
        public int Calls { get; private set; }
        public Task<GeneratedContent> GenerateAsync(GenerateContentRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return generate(request, cancellationToken);
        }
    }

    private sealed class RecordingContext : ISocialFlowDbContext
    {
        public RecordingSet Posts { get; } = new();
        public DbSet<SocialPost> SocialPosts => Posts;
        public List<CancellationToken> SaveTokens { get; } = [];
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveTokens.Add(cancellationToken);
            return Task.FromResult(1);
        }
    }

    private sealed class RecordingSet : DbSet<SocialPost>
    {
        public override Microsoft.EntityFrameworkCore.Metadata.IEntityType EntityType => throw new NotSupportedException();
        public List<SocialPost> Added { get; } = [];
        public override EntityEntry<SocialPost> Add(SocialPost entity)
        {
            Added.Add(entity);
            return null!;
        }
    }
}
