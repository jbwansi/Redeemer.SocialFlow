using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed class PostsApiTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"socialflow-api-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:SocialFlow", $"Data Source={_path};Pooling=False"));
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().Database.MigrateAsync();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task CreateGetUpdateDelete_ReturnCorrectStatusesAndPersistDtos()
    {
        using var response = await _client.PostAsJsonAsync("/api/posts", new CreatePostRequest(" Title ", null, SocialPlatform.Facebook));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<SocialPostDto>())!;
        Assert.Equal("Title", created.Title);
        Assert.Equal(SocialPostStatus.Draft, created.Status);
        Assert.EndsWith($"/api/posts/{created.Id}", response.Headers.Location!.ToString());
        Assert.Equal(created, await _client.GetFromJsonAsync<SocialPostDto>(response.Headers.Location));

        using var update = await _client.PutAsJsonAsync($"/api/posts/{created.Id}", new UpdatePostRequest("New", "Content", "CTA", "Brief", "https://example.com/a.png"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<SocialPostDto>())!;
        Assert.Equal("New", updated.Title);
        Assert.Equal("CTA", updated.CallToAction);
        Assert.Equal("Brief", updated.VisualBrief);
        Assert.Equal("https://example.com/a.png", updated.VisualUrl);
        Assert.Equal(updated, await _client.GetFromJsonAsync<SocialPostDto>($"/api/posts/{created.Id}"));

        using var delete = await _client.DeleteAsync($"/api/posts/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(string.Empty, await delete.Content.ReadAsStringAsync());
        await AssertProblem(await _client.GetAsync($"/api/posts/{created.Id}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Workflow_AllActionsReturnUpdatedDtos()
    {
        var post = await Create();
        await Transition(post.Id, "submit-for-review", SocialPostStatus.ReadyForReview);
        await Transition(post.Id, "reject", SocialPostStatus.Rejected);
        await Transition(post.Id, "submit-for-review", SocialPostStatus.ReadyForReview);
        await Transition(post.Id, "approve", SocialPostStatus.Approved);
        var date = DateTimeOffset.UtcNow.AddDays(1).ToOffset(TimeSpan.FromHours(5.5));
        using var schedule = await _client.PostAsJsonAsync($"/api/posts/{post.Id}/schedule", new SchedulePostRequest(date));
        Assert.Equal(HttpStatusCode.OK, schedule.StatusCode);
        var scheduled = (await schedule.Content.ReadFromJsonAsync<SocialPostDto>())!;
        Assert.Equal(SocialPostStatus.Scheduled, scheduled.Status);
        Assert.Equal(date.ToString("O"), scheduled.ScheduledAt?.ToString("O"));
        var cancelled = await Transition(post.Id, "cancel", SocialPostStatus.Cancelled);
        Assert.Null(cancelled.ScheduledAt);
        Assert.Equal(cancelled, await _client.GetFromJsonAsync<SocialPostDto>($"/api/posts/{post.Id}"));
    }

    [Fact]
    public async Task RejectedPost_CanBeDeleted()
    {
        var post = await Create();
        await Transition(post.Id, "submit-for-review", SocialPostStatus.ReadyForReview);
        await Transition(post.Id, "reject", SocialPostStatus.Rejected);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/posts/{post.Id}")).StatusCode);
    }

    [Fact]
    public async Task List_BindsAllFiltersAndInclusiveDateBounds()
    {
        Assert.Empty((await _client.GetFromJsonAsync<SocialPostDto[]>("/api/posts"))!);
        var first = await Create();
        await Create(SocialPlatform.LinkedIn);
        var reviewed = await Create();
        await Transition(reviewed.Id, "submit-for-review", SocialPostStatus.ReadyForReview);
        var from = Uri.EscapeDataString(first.CreatedAt.ToOffset(TimeSpan.FromHours(2)).ToString("O"));
        var to = Uri.EscapeDataString(first.CreatedAt.ToOffset(TimeSpan.FromHours(-5)).ToString("O"));
        var filtered = await _client.GetFromJsonAsync<SocialPostDto[]>($"/api/posts?platform=Facebook&status=Draft&createdFrom={from}&createdTo={to}");
        Assert.Equal(first, Assert.Single(filtered!));
        Assert.Equal(2, (await _client.GetFromJsonAsync<SocialPostDto[]>("/api/posts?platform=Facebook"))!.Length);
        Assert.Single((await _client.GetFromJsonAsync<SocialPostDto[]>("/api/posts?status=ReadyForReview"))!);
        Assert.Equal(3, (await _client.GetFromJsonAsync<SocialPostDto[]>($"/api/posts?createdFrom={from}"))!.Length);
        Assert.Single((await _client.GetFromJsonAsync<SocialPostDto[]>($"/api/posts?createdTo={to}"))!);
        Assert.Empty((await _client.GetFromJsonAsync<SocialPostDto[]>("/api/posts?platform=Instagram"))!);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "")]
    [InlineData("DELETE", "")]
    [InlineData("POST", "/submit-for-review")]
    [InlineData("POST", "/approve")]
    [InlineData("POST", "/reject")]
    [InlineData("POST", "/schedule")]
    [InlineData("POST", "/cancel")]
    public async Task MissingPost_AlwaysReturns404Problem(string method, string suffix)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/posts/{Guid.NewGuid()}{suffix}");
        if (method == "PUT") request.Content = JsonContent.Create(new UpdatePostRequest("Title", "Content"));
        if (suffix == "/schedule") request.Content = JsonContent.Create(new SchedulePostRequest(DateTimeOffset.UtcNow.AddDays(1)));
        var problem = await AssertProblem(await _client.SendAsync(request), HttpStatusCode.NotFound);
        Assert.Equal("Post not found", problem.Title);
    }

    [Theory]
    [InlineData("{\"title\":\" \",\"platform\":1}")]
    [InlineData("{\"title\":null,\"platform\":1}")]
    [InlineData("{\"title\":\"Title\",\"platform\":999}")]
    public async Task DomainValidation_Returns400AndDoesNotCreatePost(string json)
    {
        var problem = await AssertProblem(await _client.PostAsync("/api/posts", new StringContent(json, Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest);
        Assert.Equal("Domain validation failed", problem.Title);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.Empty((await _client.GetFromJsonAsync<SocialPostDto[]>("/api/posts"))!);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("schedule")]
    [InlineData("cancel")]
    public async Task InvalidDraftTransition_Returns400WithoutMutation(string action)
    {
        var post = await Create();
        var response = action == "schedule"
            ? await _client.PostAsJsonAsync($"/api/posts/{post.Id}/{action}", new SchedulePostRequest(DateTimeOffset.UtcNow.AddDays(1)))
            : await _client.PostAsync($"/api/posts/{post.Id}/{action}", null);
        await AssertProblem(response, HttpStatusCode.BadRequest);
        Assert.Equal(post, await _client.GetFromJsonAsync<SocialPostDto>($"/api/posts/{post.Id}"));
    }

    [Fact]
    public async Task InvalidContentUpdateDeleteAndPastSchedule_Return400()
    {
        using var create = await _client.PostAsJsonAsync("/api/posts", new CreatePostRequest("Title", null, SocialPlatform.Facebook));
        var post = (await create.Content.ReadFromJsonAsync<SocialPostDto>())!;
        await AssertProblem(await _client.PostAsync($"/api/posts/{post.Id}/submit-for-review", null), HttpStatusCode.BadRequest);
        await AssertProblem(await _client.PutAsJsonAsync($"/api/posts/{post.Id}", new UpdatePostRequest(" ", "Changed")), HttpStatusCode.BadRequest);
        var valid = await Create();
        await Transition(valid.Id, "submit-for-review", SocialPostStatus.ReadyForReview);
        await AssertProblem(await _client.PutAsJsonAsync($"/api/posts/{valid.Id}", new UpdatePostRequest("Changed", "Content")), HttpStatusCode.BadRequest);
        await AssertProblem(await _client.DeleteAsync($"/api/posts/{valid.Id}"), HttpStatusCode.BadRequest);
        await Transition(valid.Id, "approve", SocialPostStatus.Approved);
        await AssertProblem(await _client.PostAsJsonAsync($"/api/posts/{valid.Id}/schedule", new SchedulePostRequest(DateTimeOffset.UtcNow.AddDays(-1))), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/posts/not-a-guid")]
    [InlineData("/api/posts?platform=unknown")]
    [InlineData("/api/posts?status=unknown")]
    [InlineData("/api/posts?createdFrom=not-a-date")]
    [InlineData("/api/posts?createdFrom=2026-02-01T00:00:00Z&createdTo=2026-01-01T00:00:00Z")]
    public async Task InvalidRouteOrQuery_Returns400Problem(string url) =>
        await AssertProblem(await _client.GetAsync(url), HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("")]
    public async Task InvalidBody_Returns400Problem(string body) =>
        await AssertProblem(await _client.PostAsync("/api/posts", new StringContent(body, Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnexpectedFailure_ReturnsGeneric500Problem(string environment)
    {
        await using var failingFactory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment)
            .ConfigureServices(services => services.Replace(ServiceDescriptor.Scoped<ISocialFlowDbContext, FailingContext>())));
        using var client = failingFactory.CreateClient();
        var problem = await AssertProblem(await client.GetAsync("/api/posts"), HttpStatusCode.InternalServerError);
        Assert.Equal("An unexpected error occurred", problem.Title);
        Assert.DoesNotContain("secret-database", JsonSerializer.Serialize(problem));
        Assert.False(problem.Extensions.ContainsKey("exception"));
    }

    [Fact]
    public async Task UnsupportedMediaTypeAndMethod_ReturnProblemDetails()
    {
        await AssertProblem(await _client.PostAsync("/api/posts", new StringContent("plain text")), HttpStatusCode.UnsupportedMediaType);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/posts/{Guid.NewGuid()}");
        await AssertProblem(await _client.SendAsync(request), HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Documentation_IsDevelopmentOnlyAndDescribesAllOperations()
    {
        await AssertProblem(await _client.GetAsync("/openapi/v1.json"), HttpStatusCode.NotFound);
        await AssertProblem(await _client.GetAsync("/scalar/v1"), HttpStatusCode.NotFound);
        await using var development = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = development.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(10, paths.EnumerateObject().Sum(path => path.Value.EnumerateObject().Count()));
        Assert.True(paths.GetProperty("/api/posts").GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(paths.GetProperty("/api/posts/{id}").GetProperty("delete").GetProperty("responses").TryGetProperty("204", out _));
        var parameters = paths.GetProperty("/api/posts").GetProperty("get").GetProperty("parameters");
        Assert.Equal(4, parameters.GetArrayLength());
        foreach (var action in new[] { "submit-for-review", "approve", "reject", "schedule", "cancel" })
            Assert.True(paths.GetProperty($"/api/posts/{{id}}/{action}").TryGetProperty("post", out _));
        var scalar = await client.GetAsync("/scalar/v1");
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
        Assert.Equal("text/html", scalar.Content.Headers.ContentType!.MediaType);
    }

    private async Task<SocialPostDto> Create(SocialPlatform platform = SocialPlatform.Facebook)
    {
        using var response = await _client.PostAsJsonAsync("/api/posts", new CreatePostRequest("Title", "Content", platform));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SocialPostDto>())!;
    }

    private async Task<SocialPostDto> Transition(Guid id, string action, SocialPostStatus expected)
    {
        using var response = await _client.PostAsync($"/api/posts/{id}/{action}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<SocialPostDto>())!;
        Assert.Equal(expected, result.Status);
        return result;
    }

    private static async Task<ProblemDetails> AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
            Assert.Equal((int)expected, problem.Status);
            Assert.False(string.IsNullOrWhiteSpace(problem.Title));
            Assert.False(string.IsNullOrWhiteSpace(problem.Type));
            Assert.False(string.IsNullOrWhiteSpace(problem.Instance));
            Assert.True(problem.Extensions.ContainsKey("traceId"));
            return problem;
        }
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
    }

    private sealed class FailingContext : ISocialFlowDbContext
    {
        public DbSet<SocialPost> SocialPosts => throw new InvalidOperationException("secret-database connection details");
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("secret-database");
    }
}
