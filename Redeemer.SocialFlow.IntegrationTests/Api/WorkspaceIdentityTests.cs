using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed partial class WorkspaceIsolationTests
{
    private const string IdentityRoot = "/api/workspace-identities";
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WorkspaceIdentity_EditorialIsResolvedForBothModesAndNeverLeaks(int mode)
    {
        var brief = new { subject = "theme", objective = "Inform", audience = "Readers", platform = 1, mode };
        foreach (var id in new[] { a.Id, b.Id })
        {
            await using var db = Db(id);
            var doc = KnowledgeDocument.Create("Reference", "theme", ["theme"], [KnowledgeUsage.Generation], SourceType.Other, AuthorityLevel.Low, "fr");
            doc.ChangeStatus(KnowledgeDocumentStatus.Approved);
            db.Add(doc); await db.SaveChangesAsync();
            db.Add(KnowledgeChunk.Create(doc.Id, "theme useful workspace content", 0)); await db.SaveChangesAsync();
        }
        var neutral = await Send(alice, HttpMethod.Post, "/api/posts/generate-draft", brief);
        Assert.Equal(HttpStatusCode.Created, neutral.StatusCode);
        Assert.Null(draftGenerator.Received!.EditorialIdentity);
        Assert.Contains("politique neutre", await neutral.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await Send(alice, HttpMethod.Put, IdentityRoot + "/editorial",
            new { revision = 0, organizationName = "Organization A", tone = "Formal", values = "Precision" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(bob, HttpMethod.Put, IdentityRoot + "/editorial",
            new { revision = 0, organizationName = "Organization B", tone = "Warm" })).StatusCode);
        await Send(alice, HttpMethod.Post, "/api/posts/generate-draft", brief);
        Assert.Equal("Organization A", draftGenerator.Received!.EditorialIdentity!.OrganizationName);
        Assert.Equal("Formal", draftGenerator.Received.EditorialIdentity.Tone);
        Assert.Equal(a.Id, draftGenerator.Received.EditorialIdentity.WorkspaceId);
        await Send(bob, HttpMethod.Post, "/api/posts/generate-draft", brief);
        Assert.Equal("Organization B", draftGenerator.Received!.EditorialIdentity!.OrganizationName);
        Assert.Equal("Warm", draftGenerator.Received.EditorialIdentity.Tone);
        Assert.DoesNotContain("Organization A", await (await bob.GetAsync(IdentityRoot + "/editorial")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Put, IdentityRoot + "/editorial",
            new { revision = 0, organizationName = "Stale overwrite" })).StatusCode);
    }

    [Theory]
    [InlineData(WorkspaceRole.Owner, true)]
    [InlineData(WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Editor, false)]
    [InlineData(WorkspaceRole.Viewer, false)]
    public async Task WorkspaceIdentity_ManagementAndApprovalRequireExistingAdminPermission(WorkspaceRole role, bool allowed)
    {
        await using (var db = Db(a.Id)) { db.Add(WorkspaceMembership.Join(a.Id, bobId, role)); await db.SaveChangesAsync(); }
        var created = await Send(alice, HttpMethod.Post, IdentityRoot + "/visual",
            new { identityId = "A", name = "A", style = "Flat", colors = new[] { "#112233" } });
        var identity = (await created.Content.ReadFromJsonAsync<LibraryIdentityDto>())!;
        bob.DefaultRequestHeaders.Remove("X-Workspace-Id"); bob.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync(IdentityRoot + "/visual")).StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await Send(bob, HttpMethod.Put, IdentityRoot + "/editorial", new { revision = 0, organizationName = "A" })).StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await Send(bob, HttpMethod.Post, IdentityRoot + $"/visual/{identity.Id}/approve", new { revision = identity.Revision })).StatusCode);
    }

    [Fact]
    public async Task WorkspaceIdentity_ApprovalBindsExactRevisionAndRejectsTamperingAndForeignIdentity()
    {
        var generator = new WorkspaceImageGenerator();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IVisualGenerator>(generator))));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost:65474") });
        await Send(client, HttpMethod.Post, "/api/auth/login", new { email = "alice@example.test", password = Password });
        client.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        var post = await Create(alice, "Editorial subject");
        var input = new { identityId = "A", name = "Organization A", style = "Flat", colors = new[] { "#123456" } };
        var created = await Send(client, HttpMethod.Post, IdentityRoot + "/visual", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var identity = (await created.Content.ReadFromJsonAsync<LibraryIdentityDto>())!;
        var foreign = await Send(bob, HttpMethod.Post, IdentityRoot + "/visual",
            new { identityId = "B", name = "Organization B", style = "Photo", colors = new[] { "#ABCDEF" } });
        var other = (await foreign.Content.ReadFromJsonAsync<LibraryIdentityDto>())!;
        Assert.DoesNotContain("Organization B", await (await client.GetAsync(IdentityRoot + "/visual")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Post, IdentityRoot + $"/visual/{other.Id}/approve", new { revision = other.Revision })).StatusCode);
        object Request(string name, string? token = null, Guid? id = null) => new { input.identityId, name, input.style, input.colors,
            libraryId = id ?? identity.Id, revision = identity.Revision, identityMode = "Approved", conceptToken = token };
        var route = $"/api/posts/{post.Id}/workspace-visuals";
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, route + "/concept", Request(input.name))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Post, IdentityRoot + $"/visual/{identity.Id}/approve", new { revision = identity.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Post, IdentityRoot + $"/visual/{identity.Id}/approve", new { revision = identity.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, route + "/concept", Request("Browser substitution"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Post, route + "/concept", Request(input.name, id: other.Id))).StatusCode);
        var preview = await Send(client, HttpMethod.Post, route + "/concept", Request(input.name));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var concept = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", concept.GetProperty("identityStatus").GetString());
        Assert.Equal(0, generator.Calls);
        var token = concept.GetProperty("conceptToken").GetString();
        Assert.Equal(HttpStatusCode.Created, (await Send(client, HttpMethod.Post, route + "/generate", Request(input.name, token))).StatusCode);
        Assert.Equal(input.name, generator.LastRequest!.Identity.Name);
        Assert.Equal(input.colors, generator.LastRequest.Identity.Colors);
        Assert.NotNull(generator.LastRequest.Identity.ApprovalId);
        using var secondClient = host.CreateClient(new() { BaseAddress = new Uri("https://localhost:65474") });
        await Send(secondClient, HttpMethod.Post, "/api/auth/login", new { email = "bob@example.test", password = Password });
        secondClient.DefaultRequestHeaders.Add("X-Workspace-Id", b.Id.ToString());
        var secondPost = await Create(secondClient, "Other editorial subject");
        await Send(secondClient, HttpMethod.Post, IdentityRoot + $"/visual/{other.Id}/approve", new { revision = other.Revision });
        var secondInput = new { other.IdentityId, other.Name, other.Style, other.Colors, libraryId = other.Id,
            revision = other.Revision, identityMode = "Approved" };
        var secondRoute = $"/api/posts/{secondPost.Id}/workspace-visuals";
        var secondPreview = await Send(secondClient, HttpMethod.Post, secondRoute + "/concept", secondInput);
        Assert.Equal(HttpStatusCode.OK, secondPreview.StatusCode);
        var secondToken = (await secondPreview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("conceptToken").GetString();
        Assert.Equal(HttpStatusCode.Created, (await Send(secondClient, HttpMethod.Post, secondRoute + "/generate",
            new { other.IdentityId, other.Name, other.Style, other.Colors, libraryId = other.Id, revision = other.Revision,
                identityMode = "Approved", conceptToken = secondToken })).StatusCode);
        Assert.Equal("Organization B", generator.LastRequest!.Identity.Name);
        Assert.Equal(new[] { "#ABCDEF" }, generator.LastRequest.Identity.Colors);
        var changed = await Send(client, HttpMethod.Put, IdentityRoot + $"/visual/{identity.Id}",
            new { revision = identity.Revision, identity = new { input.identityId, input.name, style = "Photo", input.colors } });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, route + "/generate", Request(input.name, token))).StatusCode);
        Assert.Equal(2, generator.Calls);
        await using var db = Db(a.Id);
        var evidence = Assert.Single(await db.VisualIdentityApprovals.ToListAsync());
        Assert.Equal(identity.Revision, evidence.Revision);
        Assert.Contains("Flat", evidence.SnapshotJson);
        Assert.Null((await (await client.GetAsync(IdentityRoot + "/visual")).Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("approvalId").GetString());
        var updated = (await changed.Content.ReadFromJsonAsync<LibraryIdentityDto>())!;
        var inactiveResponse = await Send(client, HttpMethod.Put, IdentityRoot + $"/visual/{identity.Id}",
            new { revision = updated.Revision, identity = new { input.identityId, input.name, input.style, input.colors, isActive = false } });
        var inactive = (await inactiveResponse.Content.ReadFromJsonAsync<LibraryIdentityDto>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post,
            IdentityRoot + $"/visual/{identity.Id}/approve", new { revision = inactive.Revision })).StatusCode);
        db.Remove(evidence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
