using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Editorial;
using Xunit;
namespace Redeemer.SocialFlow.IntegrationTests.Api;
public sealed partial class WorkspaceIsolationTests
{
    private const string AngleRoot = "/api/workspace-editorial-angles";
    private static AngleInput AngleData(string name, long revision = 0, bool active = true) =>
        new(revision, name, "Description " + name, "Explain", "Do not invent facts", "Ask a question", active, 2);
    private static async Task<AngleDto> AddAngle(HttpClient client, string name)
    {
        var response = await Send(client, HttpMethod.Post, AngleRoot, AngleData(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AngleDto>())!;
    }
    [Fact]
    public async Task Angles_ConfiguredCatalogIsExplicitAndCopiedIndependently()
    {
        Assert.Empty((await alice.GetFromJsonAsync<AngleDto[]>(AngleRoot))!);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(alice, HttpMethod.Post, AngleRoot + "/initialize")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, AngleRoot + "/initialize")).StatusCode);
        var angles = (await alice.GetFromJsonAsync<AngleDto[]>(AngleRoot + "?activeOnly=true"))!;
        Assert.Equal(5, angles.Length);
        Assert.Empty((await bob.GetFromJsonAsync<AngleDto[]>(AngleRoot))!);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(bob, HttpMethod.Post, AngleRoot + "/initialize")).StatusCode);
        var others = (await bob.GetFromJsonAsync<AngleDto[]>(AngleRoot))!;
        Assert.Empty(angles.Select(x => x.Id).Intersect(others.Select(x => x.Id)));
        foreach (var angle in angles)
        {
            var response = await GenerateWithAngle(alice, angle.Id);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<GenerateSocialPostDraftExecutionResult>())!;
            Assert.Equal(angle.Id, result.Metadata!.EditorialAngle!.Id);
            Assert.Equal(angle.Name, draftGenerator.Received!.EditorialAngle!.Name);
            Assert.Equal(a.Id, draftGenerator.Received.EditorialAngle.WorkspaceId);
        }
        await using var db = Db(a.Id);
        Assert.Equal(5, await db.AiGenerations.CountAsync());
        Assert.All(await db.AiGenerations.ToListAsync(), x => Assert.NotNull(x.EditorialAngleJson));
    }
    private static Task<HttpResponseMessage> GenerateWithAngle(HttpClient client, Guid? id) =>
        Send(client, HttpMethod.Post, "/api/posts/generate-draft", new { subject = "scrum", objective = "Explain", audience = "Readers",
            platform = 1, mode = 1, editorialAngleId = id, editorialAngle = new { instructions = "BROWSER INJECTION" } });
    [Fact]
    public async Task Angles_MissingInactiveForeignAndStaleRevisionAreRejectedWithoutGenerating()
    {
        var angle = await AddAngle(alice, "A");
        Assert.Equal(HttpStatusCode.Conflict, (await GenerateWithAngle(bob, angle.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await GenerateWithAngle(alice, Guid.NewGuid())).StatusCode);
        Assert.Null(draftGenerator.Received);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(bob, HttpMethod.Put, AngleRoot + "/" + angle.Id, AngleData("Intrusion", angle.Revision))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(alice, HttpMethod.Put, AngleRoot + "/" + angle.Id, AngleData("A", angle.Revision, false))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Put, AngleRoot + "/" + angle.Id, AngleData("Stale", angle.Revision))).StatusCode);
        Assert.Empty((await alice.GetFromJsonAsync<AngleDto[]>(AngleRoot + "?activeOnly=true"))!);
        var rejected = await GenerateWithAngle(alice, angle.Id);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("EditorialAngleUnavailable", await rejected.Content.ReadAsStringAsync());
        Assert.Null(draftGenerator.Received);
        Assert.Equal(HttpStatusCode.Created, (await GenerateWithAngle(alice, null)).StatusCode);
        Assert.Null(draftGenerator.Received!.EditorialAngle);
        await using var db = Db(a.Id);
        Assert.Null((await db.AiGenerations.SingleAsync()).EditorialAngleJson);
    }
    [Theory]
    [InlineData(WorkspaceRole.Owner, true, true)]
    [InlineData(WorkspaceRole.Admin, true, true)]
    [InlineData(WorkspaceRole.Editor, false, true)]
    [InlineData(WorkspaceRole.Viewer, false, false)]
    public async Task Angles_RolesUseExistingPolicies(WorkspaceRole role, bool manage, bool generate)
    {
        await using (var db = Db(a.Id)) { db.Add(WorkspaceMembership.Join(a.Id, bobId, role)); await db.SaveChangesAsync(); }
        var angle = await AddAngle(alice, "A");
        bob.DefaultRequestHeaders.Remove("X-Workspace-Id"); bob.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync(AngleRoot)).StatusCode);
        Assert.Equal(manage ? HttpStatusCode.Created : HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Post, AngleRoot, AngleData("B"))).StatusCode);
        Assert.Equal(manage ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Put, AngleRoot + "/" + angle.Id, AngleData("Updated", angle.Revision))).StatusCode);
        Assert.Equal(generate ? HttpStatusCode.Created : HttpStatusCode.Forbidden, (await GenerateWithAngle(bob, angle.Id)).StatusCode);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Angles_UseServerSnapshotPreservePermanentProfileAndRemainHistoricalAfterEdits(int mode)
    {
        await Send(alice, HttpMethod.Put, "/api/workspace-identities/editorial", new { revision = 0, organizationName = "Permanent", rules = "No exaggerated promises" });
        await CreateTerm(alice, "Scrum™");
        var angle = await AddAngle(alice, "Original");
        await using (var sourceDb = Db(a.Id))
        {
            var doc = KnowledgeDocument.Create("Source", "scrum", ["scrum"], [KnowledgeUsage.Generation], SourceType.Other, AuthorityLevel.Low, "fr");
            doc.ChangeStatus(KnowledgeDocumentStatus.Approved); sourceDb.Add(doc); await sourceDb.SaveChangesAsync();
            sourceDb.Add(KnowledgeChunk.Create(doc.Id, "scrum source", 0)); await sourceDb.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Created, (await Send(alice, HttpMethod.Post, "/api/posts/generate-draft", new {
            subject = "scrum", objective = "Explain", audience = "Readers", platform = 1, mode, editorialAngleId = angle.Id,
            editorialAngle = new { instructions = "BROWSER INJECTION" } })).StatusCode);
        Assert.Equal("Permanent", draftGenerator.Received!.EditorialIdentity!.OrganizationName);
        Assert.Equal("No exaggerated promises", draftGenerator.Received.EditorialIdentity.Rules);
        Assert.Equal("Scrum™", Assert.Single(draftGenerator.Received.Terms).Canonical);
        Assert.Equal(angle.Instructions, draftGenerator.Received.EditorialAngle!.Instructions);
        await using var db = Db(a.Id);
        var before = await db.AiGenerations.AsNoTracking().SingleAsync();
        Assert.Equal(HttpStatusCode.OK, (await Send(alice, HttpMethod.Put, AngleRoot + "/" + angle.Id, AngleData("Changed", angle.Revision, false))).StatusCode);
        var after = await db.AiGenerations.AsNoTracking().SingleAsync();
        Assert.Equal(before.EditorialAngleJson, after.EditorialAngleJson);
        Assert.Equal("Original", JsonSerializer.Deserialize<EditorialAngle>(after.EditorialAngleJson!)!.Name);
        Assert.Equal(1, (await db.WorkspaceEditorialProfiles.SingleAsync()).Revision);
    }
}
