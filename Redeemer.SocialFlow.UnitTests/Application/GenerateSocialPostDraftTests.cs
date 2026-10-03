using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Application.Knowledge;
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
            Assert.Empty(context.Audits.Added);
            return Task.FromResult(Content);
        });

        var applicationResult = await new GenerateSocialPostDraft(generator, context, new UnusedGroundedGenerator()).ExecuteAsync(request, cancellation.Token);
        var result = applicationResult.Post;
        Assert.Same(Content.Warnings, applicationResult.Warnings);

        var saved = Assert.Single(context.Posts.Added);
        var audit = Assert.Single(context.Audits.Added);
        Assert.Equal(saved.Id, audit.SocialPostId);
        Assert.Equal(request.Subject, audit.Subject);
        Assert.Equal("TestProvider", audit.Provider);
        Assert.Equal("test-model", audit.Model);
        Assert.Equal(Content.Warnings, audit.Warnings);
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

        var result = await new GenerateSocialPostDraft(generator, context, new UnusedGroundedGenerator()).ExecuteAsync(Request);

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
            new GenerateSocialPostDraft(generator, context, new UnusedGroundedGenerator()).ExecuteAsync(Request));
        Assert.Same(failure, thrown);
        Assert.Empty(context.Posts.Added);
        Assert.Empty(context.Audits.Added);
        Assert.Empty(context.SaveTokens);
    }

    [Theory]
    [InlineData(" ", SocialPlatform.LinkedIn)]
    [InlineData("Title", (SocialPlatform)999)]
    public async Task DomainValidationFailure_DoesNotAddOrSave(string title, SocialPlatform platform)
    {
        var context = new RecordingContext();
        var generator = new StubGenerator((_, _) => Task.FromResult(Content with { Title = title }));
        await Assert.ThrowsAsync<DomainException>(() => new GenerateSocialPostDraft(generator, context, new UnusedGroundedGenerator())
            .ExecuteAsync(Request with { Platform = platform }));
        Assert.Empty(context.Posts.Added);
        Assert.Empty(context.Audits.Added);
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
            new GenerateSocialPostDraft(generator, context, new UnusedGroundedGenerator()).ExecuteAsync(Request, cancellation.Token));
        Assert.Equal(cancelBefore ? 0 : 1, generator.Calls);
        Assert.Empty(context.Posts.Added);
        Assert.Empty(context.Audits.Added);
        Assert.Empty(context.SaveTokens);
    }

    [Theory]
    [InlineData(GroundedContentOutcome.Generated)]
    [InlineData(GroundedContentOutcome.NoRelevantPassages)]
    [InlineData(GroundedContentOutcome.ContextLimitExceeded)]
    public async Task Grounded_OnlySuccessfulGenerationCreatesDraftAndPreservesReferences(GroundedContentOutcome outcome)
    {
        var context = new RecordingContext();
        var editorial = new StubGenerator((_, _) => throw new InvalidOperationException("No fallback allowed"));
        var passage = new KnowledgePassage(Guid.NewGuid(), Guid.NewGuid(), "Source", 1, SourceType.Other,
            AuthorityLevel.Low, "fr", "Reference content", 0, null, null);
        IReadOnlyList<KnowledgePassage> references = outcome == GroundedContentOutcome.Generated ? new[] { passage } : [];
        using var cancellation = new CancellationTokenSource();
        var grounded = new GroundedGenerator((request, token) =>
        {
            Assert.Same(Request, request);
            Assert.Equal(cancellation.Token, token);
            return Task.FromResult(new GroundedContentResult(outcome,
                outcome == GroundedContentOutcome.Generated ? new(Content, new("Provider", "Model")) : null, references));
        });
        var result = await new GenerateSocialPostDraft(editorial, context, grounded)
            .ExecuteAsync(Request, SocialPostGenerationMode.Grounded, cancellation.Token);
        Assert.Equal(0, editorial.Calls);
        if (outcome == GroundedContentOutcome.Generated)
        {
            Assert.Equal(SocialPostDraftOutcome.Created, result.Outcome);
            Assert.Equal(Assert.Single(context.Posts.Added).Id, result.PostId);
            Assert.Equal(SocialPostStatus.Draft, result.Draft!.Post.Status);
            Assert.Same(Content.Warnings, result.Draft.Warnings);
            Assert.Same(references, result.ReferencePassages);
            Assert.Equal("Model", result.Metadata!.Model);
            Assert.Equal("Provider", Assert.Single(context.Audits.Added).Provider);
            Assert.Equal(cancellation.Token, Assert.Single(context.SaveTokens));
        }
        else
        {
            Assert.Equal(outcome == GroundedContentOutcome.NoRelevantPassages ? SocialPostDraftOutcome.NoRelevantPassages : SocialPostDraftOutcome.ContextLimitExceeded, result.Outcome);
            Assert.Null(result.Draft);
            Assert.Null(result.PostId);
            Assert.Empty(context.Posts.Added);
            Assert.Empty(context.Audits.Added);
            Assert.Empty(context.SaveTokens);
        }
    }

    [Theory]
    [InlineData("error")]
    [InlineData("invalid-content")]
    [InlineData("cancel-before")]
    [InlineData("cancel-during")]
    [InlineData("invalid-result")]
    public async Task Grounded_FailureOrCancellationDoesNotSave(string scenario)
    {
        var context = new RecordingContext();
        var editorial = new StubGenerator((_, _) => throw new InvalidOperationException("No fallback"));
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("Generation failure");
        var grounded = new GroundedGenerator((_, _) =>
        {
            if (scenario == "error") throw failure;
            if (scenario == "cancel-during") cancellation.Cancel();
            var content = scenario == "invalid-content" ? Content with { Title = " " } : Content;
            IReadOnlyList<KnowledgePassage> references = scenario == "invalid-result" ? [] :
                [new(Guid.NewGuid(), Guid.NewGuid(), "Source", 1, SourceType.Other, AuthorityLevel.Low, "fr", "Text", 0, null, null)];
            return Task.FromResult(new GroundedContentResult(GroundedContentOutcome.Generated, new(content, new("Provider", "Model")), references));
        });
        if (scenario == "cancel-before") cancellation.Cancel();
        var exception = await Record.ExceptionAsync(() => new GenerateSocialPostDraft(editorial, context, grounded)
            .ExecuteAsync(Request, SocialPostGenerationMode.Grounded, cancellation.Token));
        Assert.NotNull(exception);
        if (scenario == "error") Assert.Same(failure, exception);
        if (scenario == "invalid-content") Assert.IsType<DomainException>(exception);
        if (scenario.StartsWith("cancel")) Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(0, editorial.Calls);
        Assert.Empty(context.Posts.Added);
        Assert.Empty(context.Audits.Added);
        Assert.Empty(context.SaveTokens);
    }

    [Fact]
    public async Task InvalidModeDoesNotGenerateOrSave()
    {
        var context = new RecordingContext();
        var editorial = new StubGenerator((_, _) => throw new InvalidOperationException("Not called"));
        await Assert.ThrowsAsync<ArgumentException>(() => new GenerateSocialPostDraft(editorial, context, new UnusedGroundedGenerator())
            .ExecuteAsync(Request, (SocialPostGenerationMode)999));
        Assert.Equal(0, editorial.Calls);
        Assert.Empty(context.SaveTokens);
    }

    private sealed class GroundedGenerator(Func<GenerateContentRequest, CancellationToken, Task<GroundedContentResult>> generate) : IGenerateGroundedContent
    {
        public Task<GroundedContentResult> ExecuteAsync(GenerateContentRequest request, CancellationToken cancellationToken = default) => generate(request, cancellationToken);
    }

    private sealed class UnusedGroundedGenerator : IGenerateGroundedContent
    {
        public Task<GroundedContentResult> ExecuteAsync(GenerateContentRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Editorial generation must not invoke grounded generation.");
    }

    private sealed class StubGenerator(Func<GenerateContentRequest, CancellationToken, Task<GeneratedContent>> generate) : IContentGenerator
    {
        public int Calls { get; private set; }
        public async Task<ContentGenerationResult> GenerateAsync(GenerateContentRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return new ContentGenerationResult(await generate(request, cancellationToken), new ContentGenerationMetadata("TestProvider", "test-model"));
        }
    }

    private sealed class RecordingContext : ISocialFlowDbContext
    {
        public RecordingSet Posts { get; } = new();
        public DbSet<SocialPost> SocialPosts => Posts;
        public RecordingAuditSet Audits { get; } = new();
        public DbSet<AiGeneration> AiGenerations => Audits;
        public List<CancellationToken> SaveTokens { get; } = [];
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveTokens.Add(cancellationToken);
            return Task.FromResult(1);
        }
    }

    private sealed class RecordingAuditSet : DbSet<AiGeneration>
    {
        public override Microsoft.EntityFrameworkCore.Metadata.IEntityType EntityType => throw new NotSupportedException();
        public List<AiGeneration> Added { get; } = [];
        public override EntityEntry<AiGeneration> Add(AiGeneration entity)
        {
            Added.Add(entity);
            return null!;
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
