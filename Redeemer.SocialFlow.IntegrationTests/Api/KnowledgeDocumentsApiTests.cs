using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.Knowledge;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed class KnowledgeDocumentsApiTests : IAsyncLifetime
{
    private const string Url = "/api/dev/knowledge/documents";
    private const string Valid = """
        {"title":"Test", "primaryTheme":"bénévoles", "themes":["bénévoles","soutien"],
         "usages":[1], "sourceType":7, "authorityLevel":1, "language":"fr",
         "chunks":[{"content":"Texte fictif", "chunkIndex":0, "pageNumber":1, "section":"Test"}]}
        """;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"knowledge-api-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("LinkedIn:WorkerEnabled", "false").UseEnvironment("Development").UseSetting("ConnectionStrings:SocialFlow", $"Data Source={_path};Pooling=False"));
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().Database.MigrateAsync();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task CreateReadApproveAndToggle_PersistWithoutAcceptingClientStatusOrIds()
    {
        var suppliedId = Guid.NewGuid();
        var body = JsonNode.Parse(Valid)!;
        body["id"] = suppliedId.ToString();
        body["status"] = (int)KnowledgeDocumentStatus.Approved;
        body["chunks"]![0]!["id"] = suppliedId.ToString();
        body["chunks"]![0]!["knowledgeDocumentId"] = suppliedId.ToString();
        using var response = await _client.PostAsJsonAsync(Url, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<KnowledgeDocumentDetailsDto>())!;
        Assert.Equal(KnowledgeDocumentStatus.Draft, created.Document.Status);
        Assert.NotEqual(suppliedId, created.Document.Id);
        Assert.NotEqual(suppliedId, Assert.Single(created.Chunks).Id);
        Assert.Equal(created.Document.Id, created.Chunks[0].KnowledgeDocumentId);
        Assert.EndsWith($"{Url}/{created.Document.Id}", response.Headers.Location!.ToString());
        var read = (await _client.GetFromJsonAsync<KnowledgeDocumentDetailsDto>(response.Headers.Location))!;
        Assert.Equal(created.Chunks, read.Chunks);
        Assert.Equal(created.Document.Id, read.Document.Id);
        Assert.Equal("fr", read.Document.Language);
        Assert.Equal(new[] { "bénévoles", "soutien" }, read.Document.Themes);

        using var approve = await _client.PostAsync($"{Url}/{created.Document.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(KnowledgeDocumentStatus.Approved, (await approve.Content.ReadFromJsonAsync<KnowledgeDocumentDto>())!.Status);
        foreach (var active in new[] { false, true })
        {
            using var toggle = await _client.PutAsJsonAsync($"{Url}/{created.Document.Id}/active", new { isActive = active });
            Assert.Equal(HttpStatusCode.OK, toggle.StatusCode);
            Assert.Equal(active, (await toggle.Content.ReadFromJsonAsync<KnowledgeDocumentDto>())!.IsActive);
            var saved = (await _client.GetFromJsonAsync<KnowledgeDocumentDetailsDto>(response.Headers.Location))!;
            Assert.Equal(active, saved.Document.IsActive);
            Assert.Equal(KnowledgeDocumentStatus.Approved, saved.Document.Status);
        }
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        Assert.Single(await db.KnowledgeDocuments.ToListAsync());
        Assert.Single(await db.KnowledgeChunks.ToListAsync());
        Assert.Empty(await db.SocialPosts.ToListAsync());
        Assert.Empty(await db.AiGenerations.ToListAsync());
    }

    [Fact]
    public async Task List_PaginatesAndFilters()
    {
        for (var i = 0; i < 4; i++)
        {
            var body = JsonNode.Parse(Valid)!;
            if (i == 3) body["language"] = "en";
            using var response = await _client.PostAsJsonAsync(Url, body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        var query = $"{Url}?pageSize=2&status={(int)KnowledgeDocumentStatus.Draft}&theme=soutien&language=fr";
        var first = (await _client.GetFromJsonAsync<KnowledgeDocumentPage>(query))!;
        var second = (await _client.GetFromJsonAsync<KnowledgeDocumentPage>(query + "&pageNumber=2"))!;
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(3, first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count());
        Assert.Equal(first.Items.Select(x => x.Id), (await _client.GetFromJsonAsync<KnowledgeDocumentPage>(query))!.Items.Select(x => x.Id));
        Assert.Empty((await _client.GetFromJsonAsync<KnowledgeDocumentPage>($"{Url}?status={(int)KnowledgeDocumentStatus.Approved}"))!.Items);
    }

    [Theory]
    [InlineData("title", "\" \"")]
    [InlineData("themes", "[]")]
    [InlineData("themes", "null")]
    [InlineData("usages", "[999]")]
    [InlineData("usages", "null")]
    [InlineData("sourceType", "999")]
    [InlineData("authorityLevel", "null")]
    [InlineData("language", "\"\"")]
    [InlineData("version", "0")]
    [InlineData("chunks", "null")]
    [InlineData("chunks", "[null]")]
    [InlineData("chunks", "[{\"content\":\"\",\"chunkIndex\":0}]")]
    [InlineData("chunks", "[{\"content\":\"Text\"}]")]
    [InlineData("chunks", "[{\"content\":\"Text\",\"chunkIndex\":-1}]")]
    public async Task Create_InvalidRequestIs400WithoutPersistence(string field, string value)
    {
        var body = JsonNode.Parse(Valid)!;
        body[field] = JsonNode.Parse(value);
        await AssertProblem(await _client.PostAsJsonAsync(Url, body), HttpStatusCode.BadRequest);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        Assert.Empty(await db.KnowledgeDocuments.ToListAsync());
        Assert.Empty(await db.KnowledgeChunks.ToListAsync());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("")]
    public async Task Create_RejectsMissingOrMalformedBody(string body) =>
        await AssertProblem(await _client.PostAsync(Url, Json(body)), HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("pageNumber=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=999")]
    [InlineData("theme=%20")]
    [InlineData("language=%20")]
    public async Task List_InvalidFiltersAre400(string query) =>
        await AssertProblem(await _client.GetAsync($"{Url}?{query}"), HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"isActive\":null}")]
    [InlineData("{\"isActive\":\"true\"}")]
    public async Task Active_RequiresExplicitBoolean(string body) =>
        await AssertProblem(await _client.PutAsync($"{Url}/{Guid.NewGuid()}/active", Json(body)), HttpStatusCode.BadRequest);

    [Fact]
    public async Task MissingDocumentsAre404()
    {
        var path = $"{Url}/{Guid.NewGuid()}";
        await AssertProblem(await _client.GetAsync(path), HttpStatusCode.NotFound);
        await AssertProblem(await _client.PostAsync(path + "/approve", null), HttpStatusCode.NotFound);
        await AssertProblem(await _client.PutAsJsonAsync(path + "/active", new { isActive = false }), HttpStatusCode.NotFound);
        await AssertProblem(await _client.GetAsync(Url + "/invalid-id"), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task AllRoutesAreUnavailableOutsideDevelopment(string environment)
    {
        await using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("LinkedIn:WorkerEnabled", "false").UseEnvironment(environment));
        using var client = factory.CreateClient();
        var path = $"{Url}/{Guid.NewGuid()}";
        await AssertProblem(await client.PostAsync(Url, Json(Valid)), HttpStatusCode.NotFound);
        await AssertProblem(await client.GetAsync(Url), HttpStatusCode.NotFound);
        await AssertProblem(await client.GetAsync(path), HttpStatusCode.NotFound);
        await AssertProblem(await client.PostAsync(path + "/approve", null), HttpStatusCode.NotFound);
        await AssertProblem(await client.PutAsJsonAsync(path + "/active", new { isActive = true }), HttpStatusCode.NotFound);
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");
    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal((int)status, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Status);
        }
    }
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
    }
}
