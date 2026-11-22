using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Application.Terminology;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Terminology;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed partial class WorkspaceIsolationTests
{
    private const string TermRoot = "/api/workspace-terminology";
    private static async Task<TermDto> CreateTerm(HttpClient client, string canonical, bool validate = true)
    {
        var created = await Send(client, HttpMethod.Post, TermRoot, new { revision = 0, canonical, aliases = new[] { "scrum" }, category = 2, source = "https://reference.test/method" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var term = (await created.Content.ReadFromJsonAsync<TermDto>())!;
        Assert.Equal(TermStatus.Proposed, term.Status); Assert.Null(term.VerifiedAt);
        if (!validate) return term;
        var response = await Send(client, HttpMethod.Post, TermRoot + $"/{term.Id}/validate", new { revision = term.Revision });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TermDto>())!;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Terminology_GenerationsUseOwnValidatedRulesAndPreserveSources(int mode)
    {
        var first = await CreateTerm(alice, "Scrum™");
        await CreateTerm(bob, "Scrum®");
        var generator = new TerminologyDraftGenerator();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IContentGenerator>(generator))));
        foreach (var pair in new[] { (a.Id, "alice@example.test", "Scrum™"), (b.Id, "bob@example.test", "Scrum®") })
        {
            await using (var db = Db(pair.Id))
            {
                var doc = KnowledgeDocument.Create("Source", "scrum", ["scrum"], [KnowledgeUsage.Generation], SourceType.Other, AuthorityLevel.Low, "fr");
                doc.ChangeStatus(KnowledgeDocumentStatus.Approved); db.Add(doc); await db.SaveChangesAsync();
                db.Add(KnowledgeChunk.Create(doc.Id, "scrum is a source excerpt", 0)); await db.SaveChangesAsync();
            }
            using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost:65474") });
            await Send(client, HttpMethod.Post, "/api/auth/login", new { email = pair.Item2, password = Password });
            client.DefaultRequestHeaders.Add("X-Workspace-Id", pair.Id.ToString());
            var response = await Send(client, HttpMethod.Post, "/api/posts/generate-draft",
                new { subject = "scrum", objective = "Inform", audience = "Readers", platform = 1, mode, terms = new[] { "CLIENT-INJECTED" } });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var draft = (await response.Content.ReadFromJsonAsync<GenerateSocialPostDraftExecutionResult>())!;
            Assert.Equal(pair.Item3, draft.Draft!.Post.Title);
            Assert.Equal(pair.Item3, draft.Draft.Post.CallToAction);
            Assert.Equal(pair.Item3, draft.Draft.Post.VisualBrief);
            Assert.Contains("https://reference.test/scrum", draft.Draft.Post.Content);
            Assert.Contains(pair.Item3, draft.Draft.Post.Content);
            Assert.Equal(pair.Item3, Assert.Single(generator.Received!.Terms).Canonical);
            Assert.Equal(pair.Id, draft.Metadata!.Terminology!.WorkspaceId);
            if (mode == 2) Assert.Equal("scrum is a source excerpt", Assert.Single(generator.Received.ReferencePassages).Content);
            await using var auditDb = Db(pair.Id);
            var evidence = JsonSerializer.Deserialize<TerminologyEvidence>((await auditDb.AiGenerations.SingleAsync()).TerminologyEvidenceJson!);
            Assert.Equal(pair.Item3, Assert.Single(evidence!.Terms).Canonical);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Send(bob, HttpMethod.Post, TermRoot + $"/{first.Id}/retire", new { revision = first.Revision })).StatusCode);
    }

    [Theory]
    [InlineData(WorkspaceRole.Owner, true)]
    [InlineData(WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Editor, false)]
    [InlineData(WorkspaceRole.Viewer, false)]
    public async Task Terminology_ValidationAndManagementUseExistingOwnerAdminPolicy(WorkspaceRole role, bool allowed)
    {
        await using (var db = Db(a.Id)) { db.Add(WorkspaceMembership.Join(a.Id, bobId, role)); await db.SaveChangesAsync(); }
        var term = await CreateTerm(alice, "Scrum™", validate: false);
        bob.DefaultRequestHeaders.Remove("X-Workspace-Id"); bob.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync(TermRoot)).StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await Send(bob, HttpMethod.Post, TermRoot + $"/{term.Id}/validate", new { revision = term.Revision })).StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden,
            (await Send(bob, HttpMethod.Post, TermRoot, new { revision = 0, canonical = "DISC", aliases = Array.Empty<string>(), category = 6 })).StatusCode);
    }

    [Fact]
    public async Task Terminology_ConceptsAndMetadataAreCheckedAndInvalidatedWithoutRewritingPosts()
    {
        var post = await Create(alice, "scrum");
        var foreign = await Create(bob, "Foreign");
        var term = await CreateTerm(alice, "Scrum™");
        var generator = new WorkspaceImageGenerator();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IVisualGenerator>(generator))));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost:65474") });
        await Send(client, HttpMethod.Post, "/api/auth/login", new { email = "alice@example.test", password = Password });
        client.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/posts/{foreign.Id}/terminology-check")).StatusCode);
        Assert.Contains("Scrum", await (await client.GetAsync($"/api/posts/{post.Id}/terminology-check")).Content.ReadAsStringAsync());
        var route = $"/api/posts/{post.Id}/workspace-visuals";
        var input = new { identityId = "test", name = "Test", style = "Flat", colors = new[] { "#123456" }, identityMode = "Temporary" };
        var response = await Send(client, HttpMethod.Post, route + "/concept", input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var concept = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Scrum™", concept.GetProperty("subject").GetString());
        Assert.NotEmpty(concept.GetProperty("warnings").EnumerateArray()); Assert.Equal(0, generator.Calls);
        var token = concept.GetProperty("conceptToken").GetString();
        var generate = new { input.identityId, input.name, input.style, input.colors, input.identityMode, conceptToken = token };
        var created = await Send(client, HttpMethod.Post, route + "/generate", generate);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("Scrum™", generator.LastRequest!.Title);
        Assert.Equal(term.Revision, Assert.Single(generator.LastRequest.Terms).Revision);
        var visual = await created.Content.ReadFromJsonAsync<VisualDto>();
        Assert.Equal(term.Id, Assert.Single(visual!.Generation!.Terminology!.Terms).Id);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Post, TermRoot + $"/{term.Id}/retire", new { revision = term.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, route + "/generate", generate)).StatusCode);
        Assert.Equal(1, generator.Calls);
        await using var db = Db(a.Id);
        Assert.Equal("scrum", (await db.SocialPosts.SingleAsync(x => x.Id == post.Id)).Title);
        Assert.Equal(3, await db.WorkspaceTermRevisions.CountAsync());
        Assert.Equal(TermStatus.Retired, (await db.WorkspaceTerms.SingleAsync()).Status);
    }

    [Fact]
    public async Task Terminology_UnvalidatedOrRetiredTermsDoNotReachGenerationAndSourceIsRequired()
    {
        var proposed = await CreateTerm(alice, "Scrum™", validate: false);
        var response = await Send(alice, HttpMethod.Post, "/api/posts/generate-draft",
            new { subject = "scrum", objective = "Inform", audience = "Readers", platform = 1, mode = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Empty(draftGenerator.Received!.Terms);
        var update = await Send(alice, HttpMethod.Put, TermRoot + $"/{proposed.Id}",
            new { revision = proposed.Revision, canonical = proposed.Canonical, aliases = proposed.Aliases, category = 2 });
        var noSource = (await update.Content.ReadFromJsonAsync<TermDto>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, TermRoot + $"/{noSource.Id}/validate", new { revision = noSource.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, TermRoot + $"/{noSource.Id}/retire", new { revision = proposed.Revision })).StatusCode);
    }
    private sealed class TerminologyDraftGenerator : IContentGenerator
    {
        public GenerateContentRequest? Received;
        public Task<ContentGenerationResult> GenerateAsync(GenerateContentRequest request, CancellationToken cancellationToken = default)
        {
            Received = request;
            return Task.FromResult(new ContentGenerationResult(new("scrum", "scrum — https://reference.test/scrum", "scrum", "scrum", []), new("Fake", "Fake")));
        }
    }
}
