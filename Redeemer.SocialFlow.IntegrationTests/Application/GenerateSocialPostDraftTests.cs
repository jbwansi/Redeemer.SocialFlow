using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Knowledge;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Application;

public sealed class GenerateSocialPostDraftTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly StubGenerator _generator = new();
    private readonly StubGroundedGenerator _grounded = new();
    private ServiceProvider _provider = null!;
    private static readonly GenerateContentRequest Request = new("Subject", "Objective", "Audience", SocialPlatform.Instagram);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<SocialFlowDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ISocialFlowDbContext>(sp => sp.GetRequiredService<SocialFlowDbContext>());
        services.AddScoped<IKnowledgeDbContext>(sp => sp.GetRequiredService<SocialFlowDbContext>());
        services.AddSingleton<IContentGenerator>(_generator);
        services.AddSingleton<IKnowledgePassageSearch, UnusedSearch>();
        services.AddApplication();
        services.AddSingleton<IGenerateGroundedContent>(_grounded);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().Database.MigrateAsync();
    }

    [Theory]
    [InlineData(" Body ", " Act ", " Image ")]
    [InlineData("", null, null)]
    public async Task Execute_PersistsMappedDraftReadableFromAnotherScope(string content, string? cta, string? brief)
    {
        IReadOnlyList<string> warnings = cta is null ? Array.Empty<string>() :
            new[] { " Human review required ", "", "Check facts", " Human review required " };
        _generator.Result = new(" Title ", content, cta, brief, warnings);
        GenerateSocialPostDraftResult result;
        await using (var scope = _provider.CreateAsyncScope())
        {
            var useCase = scope.ServiceProvider.GetRequiredService<IGenerateSocialPostDraft>();
            Assert.Same(useCase, scope.ServiceProvider.GetRequiredService<IGenerateSocialPostDraft>());
            result = await useCase.ExecuteAsync(Request);
        }

        await using var readScope = _provider.CreateAsyncScope();
        var posts = await readScope.ServiceProvider.GetRequiredService<ISocialPostService>().ListAsync();
        var saved = Assert.Single(posts);
        Assert.Equal(result.Post, saved);
        Assert.Same(warnings, result.Warnings);
        Assert.Equal(warnings, result.Warnings);
        Assert.Equal("Title", saved.Title);
        Assert.Equal(content.Trim(), saved.Content);
        Assert.Equal(cta?.Trim(), saved.CallToAction);
        Assert.Equal(brief?.Trim(), saved.VisualBrief);
        Assert.Equal(Request.Platform, saved.Platform);
        Assert.Equal(SocialPostStatus.Draft, saved.Status);
        Assert.Null(saved.ScheduledAt);
        Assert.Null(saved.PublishedAt);
        Assert.Null(saved.VisualUrl);
        var audit = Assert.Single(await readScope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().AiGenerations.ToListAsync());
        Assert.Equal(saved.Id, audit.SocialPostId);
        Assert.Equal(Request.Subject, audit.Subject);
        Assert.Equal(Request.Objective, audit.Objective);
        Assert.Equal(Request.Audience, audit.Audience);
        Assert.Equal(Request.Platform, audit.Platform);
        Assert.Equal("TestProvider", audit.Provider);
        Assert.Equal("test-model", audit.Model);
        Assert.Equal(warnings, audit.Warnings);
        Assert.NotEqual(default, audit.GeneratedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GenerationOrDomainFailure_LeavesNoTrackedOrPersistedPost(bool generationFails)
    {
        _generator.Fail = generationFails;
        _generator.Result = new(" ", "Body", null, null, []);
        await using (var scope = _provider.CreateAsyncScope())
        {
            var useCase = scope.ServiceProvider.GetRequiredService<IGenerateSocialPostDraft>();
            if (generationFails)
                await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(Request));
            else
                await Assert.ThrowsAsync<DomainException>(() => useCase.ExecuteAsync(Request));
            var context = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
            Assert.Empty(context.ChangeTracker.Entries());
            await context.SaveChangesAsync();
        }
        await using var readScope = _provider.CreateAsyncScope();
        Assert.Empty(await readScope.ServiceProvider.GetRequiredService<ISocialPostService>().ListAsync());
        Assert.Empty(await readScope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().AiGenerations.ToListAsync());
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null) await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData(GroundedContentOutcome.Generated, false)]
    [InlineData(GroundedContentOutcome.NoRelevantPassages, false)]
    [InlineData(GroundedContentOutcome.ContextLimitExceeded, false)]
    [InlineData(GroundedContentOutcome.Generated, true)]
    public async Task Grounded_PersistsOnlySuccessfulDraftAndAudit(GroundedContentOutcome outcome, bool fail)
    {
        _generator.Fail = true; // Any accidental editorial fallback fails the test.
        _grounded.Outcome = outcome;
        _grounded.Fail = fail;
        GenerateSocialPostDraftExecutionResult? result = null;
        await using (var scope = _provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IGenerateSocialPostDraft>();
            if (fail)
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(Request, SocialPostGenerationMode.Grounded));
            else
                result = await service.ExecuteAsync(Request, SocialPostGenerationMode.Grounded);
        }
        await using var read = _provider.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        if (!fail && outcome == GroundedContentOutcome.Generated)
        {
            var post = Assert.Single(await db.SocialPosts.ToListAsync());
            var audit = Assert.Single(await db.AiGenerations.ToListAsync());
            Assert.Equal(SocialPostStatus.Draft, post.Status);
            Assert.Equal("Grounded title", post.Title);
            Assert.Equal("Grounded body", post.Content);
            Assert.Equal(Request.Platform, post.Platform);
            Assert.Null(post.ScheduledAt);
            Assert.Null(post.PublishedAt);
            Assert.Equal(post.Id, result!.PostId);
            Assert.Equal(post.Id, audit.SocialPostId);
            Assert.Equal("GroundedProvider", audit.Provider);
            Assert.Equal("grounded-model", audit.Model);
            Assert.Equal(new[] { "Human review", "Human review" }, result.Draft!.Warnings);
            Assert.Equal(result.Draft.Warnings, audit.Warnings);
            Assert.Equal(_grounded.Passage, Assert.Single(result.ReferencePassages));
            Assert.Empty(await db.KnowledgeDocuments.ToListAsync());
            Assert.Empty(await db.KnowledgeChunks.ToListAsync());
        }
        else
        {
            Assert.Empty(await db.SocialPosts.ToListAsync());
            Assert.Empty(await db.AiGenerations.ToListAsync());
            if (!fail) Assert.Null(result!.Draft);
        }
    }

    private sealed class StubGroundedGenerator : IGenerateGroundedContent
    {
        public GroundedContentOutcome Outcome { get; set; }
        public bool Fail { get; set; }
        public KnowledgePassage Passage { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "Source", 1,
            SourceType.Other, AuthorityLevel.Low, "fr", "Reference", 0, null, null);
        public Task<GroundedContentResult> ExecuteAsync(GenerateContentRequest request, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("Generation failed");
            return Task.FromResult(new GroundedContentResult(Outcome, Outcome == GroundedContentOutcome.Generated
                ? new(new("Grounded title", "Grounded body", null, null, ["Human review", "Human review"]), new("GroundedProvider", "grounded-model"))
                : null, Outcome == GroundedContentOutcome.Generated ? new[] { Passage } : []));
        }
    }

    private sealed class UnusedSearch : IKnowledgePassageSearch
    {
        public Task<IReadOnlyList<KnowledgePassage>> SearchForGenerationAsync(SearchKnowledgePassagesRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The existing draft workflow must not search knowledge passages.");
    }

    private sealed class StubGenerator : IContentGenerator
    {
        public GeneratedContent Result { get; set; } = new("Title", "Body", null, null, []);
        public bool Fail { get; set; }
        public Task<ContentGenerationResult> GenerateAsync(GenerateContentRequest request, CancellationToken cancellationToken = default) =>
            Fail ? Task.FromException<ContentGenerationResult>(new InvalidOperationException("Generation failed")) : Task.FromResult(new ContentGenerationResult(Result, new ContentGenerationMetadata("TestProvider", "test-model")));
    }
}
