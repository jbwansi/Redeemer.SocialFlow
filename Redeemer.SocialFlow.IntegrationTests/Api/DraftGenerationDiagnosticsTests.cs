#pragma warning disable OPENAI001
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Infrastructure.AI;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed partial class GenerateDraftApiTests
{
    private const string Sensitive = "secret-api-key private-prompt generated-private-text person@example.test";

    [Theory]
    [InlineData("key", "Configuration", "ConfigurationFailure", "OptionsValidationException", null)]
    [InlineData("model", "Configuration", "ConfigurationFailure", "OptionsValidationException", null)]
    [InlineData("provider", "ProviderCall", "ProviderRejected", "ClientResultException", 429)]
    [InlineData("transport", "ProviderCall", "ProviderTransportFailure", "HttpRequestException", 503)]
    [InlineData("invalid", "ResponseValidation", "InvalidGeneration", "ContentGenerationException", null)]
    [InlineData("search", "KnowledgeSearch", "KnowledgeSearchFailure", "OtherException", null)]
    [InlineData("save", "Persistence", "PersistenceFailure", "DbUpdateException", null)]
    public async Task Diagnostic_ClassifiesFailureAndCorrelatesOnlySafeFields(string failure, string stage,
        string category, string exceptionType, int? status)
    {
        using var transport = new InvalidOutputTransport { Reject = failure == "provider" };
        using var http = new HttpClient(transport);
        await using var factory = _factory.WithWebHostBuilder(builder => builder
            .UseSetting("OpenAI:ApiKey", failure == "key" ? "" : Sensitive)
            .UseSetting("OpenAI:Model", failure == "model" ? "" : "test-model")
            .ConfigureServices(services =>
            {
                if (failure is "key" or "model")
                    services.Replace(ServiceDescriptor.Singleton<IContentGenerator, OpenAIContentGenerator>());
                if (failure is "invalid" or "provider")
                    services.Replace(ServiceDescriptor.Singleton<IContentGenerator>(new OpenAIContentGenerator(
                        new ResponsesClient(new ApiKeyCredential("test-only"), new ResponsesClientOptions
                        {
                            Transport = new HttpClientPipelineTransport(http),
                            RetryPolicy = new ClientRetryPolicy(0)
                        }), Options.Create(new OpenAIContentGeneratorOptions { Model = "test-model" }))));
                if (failure == "save")
                    services.Replace(ServiceDescriptor.Scoped<ISocialFlowDbContext>(p =>
                        new FailingSave(p.GetRequiredService<SocialFlowDbContext>())));
            }));
        if (failure == "transport") _generator.Failure = new HttpRequestException(Sensitive, null, HttpStatusCode.ServiceUnavailable);
        if (failure == "search") _search.Failure = new PrivateFailure(Sensitive);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/posts/generate-draft", Request with
        {
            Subject = Sensitive,
            Mode = failure == "search" ? SocialPostGenerationMode.Grounded : SocialPostGenerationMode.Editorial
        });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("DraftGenerationFailed", json.RootElement.GetProperty("code").GetString());
        var trace = json.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(trace));
        var log = Assert.Single(_logs.Events);
        Assert.Null(log.Exception);
        Assert.Equal(5101, log.Event.Id);
        Assert.Equal(stage, log.Fields["Stage"]);
        Assert.Equal(category, log.Fields["Category"]);
        Assert.Equal(trace, log.Fields["TraceId"]);
        Assert.Equal(exceptionType, log.Fields["ExceptionType"]);
        Assert.Equal(status, log.Fields["ProviderHttpStatus"]);
        Assert.Equal(failure != "key", log.Fields["ApiKeyPresent"]);
        Assert.Equal(failure != "model", log.Fields["ModelPresent"]);
        foreach (var marker in Sensitive.Split(' ').Append(nameof(PrivateFailure)))
        {
            Assert.DoesNotContain(marker, body);
            Assert.DoesNotContain(_logs.Messages, message => message.Contains(marker));
            Assert.DoesNotContain(marker, JsonSerializer.Serialize(log.Fields));
        }
        Assert.DoesNotContain(exceptionType, body);
        if (failure == "invalid") Assert.Equal(3, transport.Calls);
        if (failure == "provider") Assert.Equal(1, transport.Calls);
        if (failure is "transport" or "save") Assert.Equal(1, _generator.Calls);
        if (failure is "key" or "model" or "search") Assert.Equal(0, _generator.Calls);
        await AssertNoPosts();
    }

    [Fact]
    public async Task Diagnostic_DoesNotLogFailureForSuccessfulDraft()
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/posts/generate-draft", Request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty(_logs.Events);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        Assert.Single(await db.SocialPosts.ToListAsync());
        Assert.Single(await db.AiGenerations.ToListAsync());
    }

    private sealed class PrivateFailure(string message) : Exception(message);
    private sealed class FailingSave(SocialFlowDbContext db) : ISocialFlowDbContext
    {
        public DbSet<SocialPost> SocialPosts => db.SocialPosts;
        public DbSet<AiGeneration> AiGenerations => db.AiGenerations;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException(Sensitive, new Exception(Sensitive));
    }
    private sealed class InvalidOutputTransport : HttpMessageHandler
    {
        public bool Reject { get; init; }
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Reject) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = JsonContent.Create(new { error = new { message = Sensitive, type = "rate_limit_error", code = "rate_limit_exceeded" } })
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = "resp_test", @object = "response", created_at = 1790000000,
                    status = "completed", model = "test-model",
                    output = new[] { new { id = "msg_test", type = "message", status = "completed", role = "assistant",
                        content = new[] { new { type = "output_text", text = Sensitive, annotations = Array.Empty<object>() } } } }
                })
            });
        }
    }
}
