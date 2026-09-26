using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed class GenerateDraftApiTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"socialflow-draft-api-{Guid.NewGuid():N}.db");
    private readonly Generator _generator = new();
    private readonly Logs _logs = new();
    private WebApplicationFactory<Program> _factory = null!;
    private static readonly GenerateContentRequest Request = new("Subject", "Objective", "Audience", SocialPlatform.LinkedIn);

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:SocialFlow", $"Data Source={_path};Pooling=False")
            .ConfigureLogging(logging => logging.AddProvider(_logs))
            .ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IContentGenerator>(_generator))));
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task Success_PersistsExactlyOneDraftAndReturnsWarningsAndLocation()
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/posts/generate-draft", Request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<GenerateSocialPostDraftResult>())!;
        Assert.Equal(Request, _generator.Received);
        Assert.Equal(1, _generator.Calls);
        Assert.Equal(_generator.Result.Warnings, result.Warnings);
        Assert.EndsWith($"/api/posts/{result.Post.Id}", response.Headers.Location!.ToString());
        Assert.Equal(result.Post, await client.GetFromJsonAsync<SocialPostDto>(response.Headers.Location));
        await using var scope = _factory.Services.CreateAsyncScope();
        var saved = Assert.Single(await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().SocialPosts.ToListAsync());
        Assert.Equal(result.Post.Id, saved.Id);
        Assert.Equal(SocialPostStatus.Draft, saved.Status);
        Assert.Equal(SocialPostStatus.Draft, result.Post.Status);
        Assert.Equal(Request.Platform, saved.Platform);
        Assert.Equal(_generator.Result.Title, saved.Title);
        Assert.Equal(_generator.Result.Content, saved.Content);
        Assert.Equal(_generator.Result.CallToAction, saved.CallToAction);
        Assert.Equal(_generator.Result.VisualBrief, saved.VisualBrief);
        Assert.Null(saved.ScheduledAt);
        Assert.Null(saved.PublishedAt);
        Assert.Null(saved.VisualUrl);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"subject\":\" \",\"objective\":\"O\",\"audience\":\"A\",\"platform\":1}")]
    [InlineData("{\"subject\":\"S\",\"objective\":null,\"audience\":\"A\",\"platform\":1}")]
    [InlineData("{\"subject\":\"S\",\"objective\":\"O\",\"audience\":\"\",\"platform\":1}")]
    [InlineData("{\"subject\":\"S\",\"objective\":\"O\",\"audience\":\"A\"}")]
    [InlineData("{\"subject\":\"S\",\"objective\":\"O\",\"audience\":\"A\",\"platform\":999}")]
    public async Task InvalidRequest_ReturnsProblemWithoutGeneratingOrSaving(string json)
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/posts/generate-draft", content);
        await AssertProblem(response, HttpStatusCode.BadRequest);
        Assert.Equal(0, _generator.Calls);
        await AssertNoPosts();
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public async Task Failure_ReturnsSafeProblemAndDoesNotSave(string environment, bool resolutionFails)
    {
        _generator.Failure = new InvalidOperationException("Authorization: Bearer secret-api-key sensitive-generated-text");
        await using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            if (resolutionFails)
                builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Scoped<IGenerateSocialPostDraft>(
                    _ => throw new InvalidOperationException("secret-api-key"))));
        });
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/posts/generate-draft", Request);
        await AssertProblem(response, HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { "secret-api-key", "Authorization", "sensitive-generated-text", "InvalidOperationException" })
        {
            Assert.DoesNotContain(secret, body);
            Assert.DoesNotContain(_logs.Messages, message => message.Contains(secret));
        }
        await AssertNoPosts();
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal((int)status, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.Equal("/api/posts/generate-draft", problem.Instance);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }

    private async Task AssertNoPosts()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().SocialPosts.ToListAsync());
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
    }

    private sealed class Generator : IContentGenerator
    {
        public int Calls { get; private set; }
        public GenerateContentRequest? Received { get; private set; }
        public Exception? Failure { get; set; }
        public GeneratedContent Result { get; } = new("Title", "Body", "Act", "Visual", [" Review ", "", " Review "]);
        public Task<GeneratedContent> GenerateAsync(GenerateContentRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Received = request;
            return Failure is null ? Task.FromResult(Result) : Task.FromException<GeneratedContent>(Failure);
        }
    }

    private sealed class Logs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception) + exception);
        }
    }
}
