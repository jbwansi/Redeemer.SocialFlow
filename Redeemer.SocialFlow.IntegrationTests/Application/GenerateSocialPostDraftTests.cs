using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Application.AI;
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
    private ServiceProvider _provider = null!;
    private static readonly GenerateContentRequest Request = new("Subject", "Objective", "Audience", SocialPlatform.Instagram);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<SocialFlowDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ISocialFlowDbContext>(sp => sp.GetRequiredService<SocialFlowDbContext>());
        services.AddSingleton<IContentGenerator>(_generator);
        services.AddApplication();
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
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null) await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class StubGenerator : IContentGenerator
    {
        public GeneratedContent Result { get; set; } = new("Title", "Body", null, null, []);
        public bool Fail { get; set; }
        public Task<GeneratedContent> GenerateAsync(GenerateContentRequest request, CancellationToken cancellationToken = default) =>
            Fail ? Task.FromException<GeneratedContent>(new InvalidOperationException("Generation failed")) : Task.FromResult(Result);
    }
}
