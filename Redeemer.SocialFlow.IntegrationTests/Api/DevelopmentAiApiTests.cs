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
    private readonly CapturingLoggerProvider _logs = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseSetting("ConnectionStrings:SocialFlow", $"Data Source={_path};Pooling=False")
            .ConfigureLogging(logging => logging.AddProvider(_logs))
            .ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IContentGenerator>(_generator))));
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
        Assert.Equal(request, _generator.Received);
        var generated = (await response.Content.ReadFromJsonAsync<GeneratedContent>())!;
        Assert.Equal(_generator.Result.Title, generated.Title);
        Assert.Equal(_generator.Result.Content, generated.Content);
        Assert.Equal(_generator.Result.CallToAction, generated.CallToAction);
        Assert.Equal(_generator.Result.VisualBrief, generated.VisualBrief);
        Assert.Equal(_generator.Result.Warnings, generated.Warnings);
        Assert.DoesNotContain(_logs.Messages, message => message.Contains("generated-content-private"));
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().SocialPosts.CountAsync());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Generate_IsUnavailableOutsideDevelopment(string environment)
    {
        await using var production = _factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
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

    private sealed class FakeContentGenerator : IContentGenerator
    {
        public GenerateContentRequest? Received { get; private set; }
        public Exception? Failure { get; set; }
        public GeneratedContent Result { get; } = new("Title", "generated-content-private", "Join us", "People together", ["Review date"]);

        public Task<GeneratedContent> GenerateAsync(GenerateContentRequest request, CancellationToken cancellationToken = default)
        {
            Received = request;
            if (Failure is not null) throw Failure;
            return Task.FromResult(Result);
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
