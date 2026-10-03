using System.Net;
using System.Net.Http.Json;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Knowledge;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed class DevelopmentAiApiTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"socialflow-ai-{Guid.NewGuid():N}.db");
    private readonly FakeContentGenerator _generator = new();
    private readonly FakeGroundedGenerator _grounded = new();
    private readonly CapturingLoggerProvider _logs = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("LinkedIn:WorkerEnabled", "false").UseEnvironment("Development")
            .UseSetting("ConnectionStrings:SocialFlow", $"Data Source={_path};Pooling=False")
            .ConfigureLogging(logging => logging.AddProvider(_logs))
            .ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IContentGenerator>(_generator));
                services.Replace(ServiceDescriptor.Singleton<IGenerateGroundedContent>(_grounded));
            }));
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task Generate_ForwardsAllInputsAndReturnsContentWithoutSavingPost()
    {
        using var client = _factory.CreateClient();
        var request = new GenerateContentRequest("Community day", "Invite volunteers", "Local families", SocialPlatform.Instagram);

        using var response = await client.PostAsJsonAsync("/api/dev/ai/generate", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(_generator.Received);
        Assert.Equal(request.Subject, _generator.Received.Subject);
        Assert.Equal(request.Objective, _generator.Received.Objective);
        Assert.Equal(request.Audience, _generator.Received.Audience);
        Assert.Equal(request.Platform, _generator.Received.Platform);
        Assert.Empty(_generator.Received.ReferencePassages);
        var generated = (await response.Content.ReadFromJsonAsync<GeneratedContent>())!;
        Assert.Equal(_generator.Result.Title, generated.Title);
        Assert.Equal(_generator.Result.Content, generated.Content);
        Assert.Equal(_generator.Result.CallToAction, generated.CallToAction);
        Assert.Equal(_generator.Result.VisualBrief, generated.VisualBrief);
        Assert.Equal(_generator.Result.Warnings, generated.Warnings);
        Assert.DoesNotContain(_logs.Messages, message => message.Contains("generated-content-private"));
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().SocialPosts.CountAsync());
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().AiGenerations.CountAsync());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Generate_IsUnavailableOutsideDevelopment(string environment)
    {
        await using var production = _factory.WithWebHostBuilder(builder => builder.UseSetting("LinkedIn:WorkerEnabled", "false").UseEnvironment(environment));
        using var client = production.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/dev/ai/generate",
            new GenerateContentRequest("Subject", "Objective", "Audience", SocialPlatform.Facebook));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(_generator.Received);
        Assert.DoesNotContain(_logs.Messages, message => message.Contains("Development AI generation failed."));
    }

    [Theory]
    [InlineData("", "Objective", "Audience", SocialPlatform.Facebook)]
    [InlineData("Subject", " ", "Audience", SocialPlatform.Facebook)]
    [InlineData("Subject", "Objective", "", SocialPlatform.Facebook)]
    [InlineData("Subject", "Objective", "Audience", (SocialPlatform)999)]
    public async Task Generate_RejectsInvalidInputs(string subject, string objective, string audience, SocialPlatform platform)
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/dev/ai/generate",
            new GenerateContentRequest(subject, objective, audience, platform));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_generator.Received);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{}")]
    public async Task Generate_RejectsInvalidBody(string body)
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/dev/ai/generate", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_generator.Received);
    }

    [Fact]
    public async Task Generate_FailureDoesNotExposeExceptionDetails()
    {
        _generator.Failure = new InvalidOperationException("secret-api-key");
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/dev/ai/generate",
            new GenerateContentRequest("Subject", "Objective", "Audience", SocialPlatform.Facebook));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("secret-api-key", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(_logs.Messages, message => message.Contains("secret-api-key"));
        Assert.Contains(_logs.Messages, message => message.Contains("System.InvalidOperationException") &&
            message.Contains("Content generation failed unexpectedly"));
    }

    [Theory]
    [InlineData(401, "invalid_api_key", "invalid_api_key", "authentication failed")]
    [InlineData(429, "insufficient_quota", "insufficient_quota", "quota is exhausted")]
    [InlineData(404, "model_not_found", "model_not_found", "resource was not found")]
    [InlineData(400, "secret-api-key", "unrecognized", "rejected the request")]
    [InlineData(500, null, "unavailable", "service failed")]
    public async Task Generate_LogsSafeSdkDiagnostics(int status, string? code, string expectedCode, string diagnostic)
    {
#pragma warning disable OPENAI001
        using var http = new HttpClient(new ErrorHandler(status, code));
        var sdk = new ResponsesClient(new ApiKeyCredential("secret-api-key"), new ResponsesClientOptions
        {
            Transport = new HttpClientPipelineTransport(http),
            RetryPolicy = new ClientRetryPolicy(0)
        });
        _generator.Failure = await Assert.ThrowsAsync<ClientResultException>(async () =>
            await sdk.CreateResponseAsync(new CreateResponseOptions { Model = "test" }));
#pragma warning restore OPENAI001
        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/dev/ai/generate",
            new GenerateContentRequest("Subject", "Objective", "Audience", SocialPlatform.Facebook));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Content generation failed.", body);
        Assert.DoesNotContain(diagnostic, body);
        var log = Assert.Single(_logs.Messages, message => message.Contains("Development AI generation failed."));
        Assert.Contains("System.ClientModel.ClientResultException", log);
        Assert.Contains($"OpenAIStatus: {status}", log);
        Assert.Contains($"OpenAIErrorCode: {expectedCode}", log);
        Assert.Contains(diagnostic, log);
        foreach (var secret in new[] { "secret-api-key", "Authorization", "private-header", "generated-content" })
        {
            Assert.DoesNotContain(secret, body);
            Assert.DoesNotContain(_logs.Messages, message => message.Contains(secret));
        }
    }

    [Fact]
    public async Task Generate_LogsSafeConfigurationFailureDuringResolution()
    {
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IContentGenerator>(_ =>
                throw new OptionsValidationException("secret-api-key", typeof(object), ["secret-api-key"])))));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/dev/ai/generate",
            new GenerateContentRequest("Subject", "Objective", "Audience", SocialPlatform.Facebook));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("secret-api-key", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(_logs.Messages, message => message.Contains("secret-api-key"));
        Assert.Contains(_logs.Messages, message => message.Contains("OptionsValidationException") &&
            message.Contains("configuration is missing or invalid"));
    }

    private sealed class ErrorHandler(int status, string? code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    error = new { code, message = "Authorization: Bearer secret-api-key generated-content", type = "private-header" }
                }), System.Text.Encoding.UTF8, "application/json")
            };
            response.Headers.Add("private-header", "secret-api-key");
            return Task.FromResult(response);
        }
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
    }

    [Theory]
    [InlineData(GroundedContentOutcome.Generated)]
    [InlineData(GroundedContentOutcome.NoRelevantPassages)]
    [InlineData(GroundedContentOutcome.ContextLimitExceeded)]
    public async Task Grounded_ReturnsExplicitOutcomeAndReferencesWithoutPersistence(GroundedContentOutcome outcome)
    {
        var passage = new KnowledgePassage(Guid.NewGuid(), Guid.NewGuid(), "Source", 2,
            SourceType.Book, AuthorityLevel.High, "fr", "Reference text", 0, 12, "Section");
        _grounded.Result = new(outcome, outcome == GroundedContentOutcome.Generated
            ? new(_generator.Result, new("Test", "model")) : null,
            outcome == GroundedContentOutcome.Generated ? new[] { passage } : Array.Empty<KnowledgePassage>());
        using var client = _factory.CreateClient();
        var request = new GenerateContentRequest("Subject", "Objective", "Audience", SocialPlatform.LinkedIn);
        using var response = await client.PostAsJsonAsync("/api/dev/ai/generate-grounded", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<GroundedContentResult>())!;
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(_grounded.Result.ReferencePassages, result.ReferencePassages);
        Assert.Equal(_grounded.Result.Generation?.Content.Content, result.Generation?.Content.Content);
        Assert.Equal(_grounded.Result.Generation?.Metadata, result.Generation?.Metadata);
        Assert.Equal(request, _grounded.Received! with { ReferencePassages = request.ReferencePassages });
        Assert.True(_grounded.Token.CanBeCanceled);
        Assert.Null(_generator.Received);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        Assert.Empty(await db.SocialPosts.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.AiGenerations.ToListAsync());
        Assert.Empty(await db.KnowledgeDocuments.ToListAsync());
        Assert.Empty(await db.KnowledgeChunks.ToListAsync());
        Assert.DoesNotContain(_logs.Messages, message => message.Contains("generated-content-private"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"subject\":\" \",\"objective\":\"O\",\"audience\":\"A\",\"platform\":1}")]
    [InlineData("{\"subject\":\"S\",\"objective\":\"\",\"audience\":\"A\",\"platform\":1}")]
    [InlineData("{\"subject\":\"S\",\"objective\":\"O\",\"audience\":\"\",\"platform\":1}")]
    [InlineData("{\"subject\":\"S\",\"objective\":\"O\",\"audience\":\"A\",\"platform\":999}")]
    public async Task Grounded_InvalidRequestDoesNotGenerate(string body)
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/dev/ai/generate-grounded", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_grounded.Received);
    }

    [Fact]
    public async Task Grounded_IsUnavailableInProduction()
    {
        await using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("LinkedIn:WorkerEnabled", "false").UseEnvironment("Production"));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/dev/ai/generate-grounded",
            new GenerateContentRequest("S", "O", "A", SocialPlatform.Facebook));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(_grounded.Received);
    }

    [Fact]
    public async Task Grounded_FailureUsesSafeDiagnostics()
    {
        _grounded.Failure = new InvalidOperationException("secret-api-key");
        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/dev/ai/generate-grounded",
            new GenerateContentRequest("S", "O", "A", SocialPlatform.Facebook));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Content generation failed.", body);
        Assert.DoesNotContain("secret-api-key", body);
        Assert.DoesNotContain(_logs.Messages, message => message.Contains("secret-api-key"));
        Assert.Contains(_logs.Messages, message => message.Contains("System.InvalidOperationException"));
    }

    [Theory]
    [InlineData("generate")]
    [InlineData("generate-grounded")]
    public async Task ClientReferences_CannotReachGenerator(string endpoint)
    {
        var serverPassage = new KnowledgePassage(Guid.NewGuid(), Guid.NewGuid(), "Server source", 1,
            SourceType.Other, AuthorityLevel.Low, "fr", "Server content", 0, null, null);
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IGenerateGroundedContent, GenerateGroundedContent>());
            services.Replace(ServiceDescriptor.Singleton<IKnowledgePassageSearch>(new ServerSearch(serverPassage)));
        }));
        using var client = factory.CreateClient();
        var clientPassage = serverPassage with { Content = "CLIENT-INJECTED", DocumentTitle = "Invented source" };
        using var response = await client.PostAsJsonAsync($"/api/dev/ai/{endpoint}", new
        {
            subject = "Subject", objective = "Objective", audience = "Audience", platform = 2,
            referencePassages = new[] { clientPassage }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(_generator.Received);
        if (endpoint == "generate")
            Assert.Empty(_generator.Received.ReferencePassages);
        else
            Assert.Equal(serverPassage, Assert.Single(_generator.Received.ReferencePassages));
        Assert.DoesNotContain("CLIENT-INJECTED", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("generate")]
    [InlineData("generate-grounded")]
    public async Task OpenApi_RequestExposesOnlyBriefFields(string endpoint)
    {
        using var client = _factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var schema = root.GetProperty("paths").GetProperty($"/api/dev/ai/{endpoint}")
            .GetProperty("post").GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        if (schema.TryGetProperty("oneOf", out var alternatives))
            schema = alternatives.EnumerateArray().Single(item => item.TryGetProperty("$ref", out _));
        if (schema.TryGetProperty("$ref", out var reference))
            schema = root.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]);
        Assert.True(schema.TryGetProperty("properties", out _), schema.GetRawText());
        Assert.Equal(new[] { "audience", "objective", "platform", "subject" },
            schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).OrderBy(name => name));
    }

    private sealed class ServerSearch(KnowledgePassage passage) : IKnowledgePassageSearch
    {
        public Task<IReadOnlyList<KnowledgePassage>> SearchForGenerationAsync(SearchKnowledgePassagesRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<KnowledgePassage>>(new[] { passage });
    }

    private sealed class FakeGroundedGenerator : IGenerateGroundedContent
    {
        public GroundedContentResult Result { get; set; } = new(GroundedContentOutcome.NoRelevantPassages, null, []);
        public GenerateContentRequest? Received { get; private set; }
        public CancellationToken Token { get; private set; }
        public Exception? Failure { get; set; }
        public Task<GroundedContentResult> ExecuteAsync(GenerateContentRequest request, CancellationToken cancellationToken = default)
        {
            Received = request;
            Token = cancellationToken;
            if (Failure is not null) throw Failure;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeContentGenerator : IContentGenerator
    {
        public GenerateContentRequest? Received { get; private set; }
        public Exception? Failure { get; set; }
        public GeneratedContent Result { get; } = new("Title", "generated-content-private", "Join us", "People together", ["Review date"]);

        public Task<ContentGenerationResult> GenerateAsync(GenerateContentRequest request, CancellationToken cancellationToken = default)
        {
            Received = request;
            if (Failure is not null) throw Failure;
            return Task.FromResult(new ContentGenerationResult(Result, new ContentGenerationMetadata("TestProvider", "test-model")));
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + exception);

            private sealed class NullScope : IDisposable
            {
                public static NullScope Instance { get; } = new();
                public void Dispose() { }
            }
        }
    }
}
