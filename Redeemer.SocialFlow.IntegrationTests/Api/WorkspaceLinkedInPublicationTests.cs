using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;
public sealed partial class WorkspaceIsolationTests
{
    private readonly LinkedInDispatch linkedInDispatch = new();
    private sealed class LinkedInDispatch : HttpMessageHandler
    {
        public int Calls; public HttpStatusCode Status = HttpStatusCode.Created;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("https://api.linkedin.com/rest/posts", request.RequestUri!.AbsoluteUri);
            Assert.Equal("workspace-secret", request.Headers.Authorization!.Parameter);
            Assert.Contains("urn:li:person:member", await request.Content!.ReadAsStringAsync(ct));
            var result = new HttpResponseMessage(Status); result.Headers.Add("x-restli-id", "urn:li:share:12345"); return result;
        }
    }
    private sealed class PastClock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddHours(-2); }
    private async Task<(Guid Post, Guid Connection)> LinkedInData(Guid workspace, bool granted = true, bool approvedOnly = false)
    {
        await using var db = Db(workspace);
        var post = SocialPost.Create("LinkedIn test", "Confirmed content", SocialPlatform.LinkedIn, new PastClock());
        post.SubmitForReview(); post.Approve();
        if (!approvedOnly) post.Schedule(DateTimeOffset.UtcNow.AddHours(-1));
        db.SocialPosts.Add(post);
        var connection = new SocialConnection { WorkspaceId = workspace, Platform = SocialPlatform.LinkedIn, ExternalAccountId = "member", DisplayName = "Member", AccountType = "Profile", ConnectionStatus = SocialConnectionStatus.Connected, PublishingPermissionGranted = granted, ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), ConnectedByUserId = aliceId };
        using var scope = factory.Services.CreateScope();
        connection.ProtectedCredential = scope.ServiceProvider.GetRequiredService<ISocialSecretProtector>().Protect(JsonSerializer.Serialize(
            new SocialCredential("workspace-secret", null, connection.ExpiresAtUtc, null, granted ? ["openid", "profile", "w_member_social"] : ["openid", "profile"])),
            $"SocialFlow/SaaS/v1/{workspace}/{connection.Id}/credential");
        db.SocialConnections.Add(connection); await db.SaveChangesAsync(); return (post.Id, connection.Id);
    }
    private static string Manual(Guid id, string action) => $"/api/linkedin/posts/{id}/{action}";
    [Fact]
    public async Task WorkspaceLinkedIn_ConfirmationCannotCrossUserOrWorkspace_AndRoleIsRechecked()
    {
        var data = await LinkedInData(a.Id);
        await using (var db = Db(a.Id)) { db.WorkspaceMemberships.Add(WorkspaceMembership.Join(a.Id, bobId, WorkspaceRole.Editor)); await db.SaveChangesAsync(); }
        var response = await Send(alice, HttpMethod.Post, Manual(data.Post, "preview"), new { connectionId = data.Connection });
        var preview = (await response.Content.ReadFromJsonAsync<LinkedInPreview>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(bob, HttpMethod.Post, Manual(data.Post, "publish"), new { preview.Confirmation })).StatusCode);
        bob.DefaultRequestHeaders.Remove("X-Workspace-Id"); bob.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(bob, HttpMethod.Post, Manual(data.Post, "publish"), new { preview.Confirmation })).StatusCode);
        var bobPreview = (await (await Send(bob, HttpMethod.Post, Manual(data.Post, "preview"), new { connectionId = data.Connection })).Content.ReadFromJsonAsync<LinkedInPreview>())!;
        await using (var db = Db(a.Id)) { var member = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == a.Id && x.UserId == bobId); member.Change(WorkspaceRole.Viewer, MembershipStatus.Active, 1); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Post, Manual(data.Post, "publish"), new { bobPreview.Confirmation })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync(Manual(data.Post, "publish"), new { preview.Confirmation })).StatusCode);
        Assert.Equal(0, linkedInDispatch.Calls);
    }
    [Fact]
    public async Task WorkspaceLinkedIn_ChecksEncryptedGrantAndRejectsChangedPostAtClaim()
    {
        var data = await LinkedInData(a.Id, false);
        await using (var db = Db(a.Id)) { var c = await db.SocialConnections.SingleAsync(); c.PublishingPermissionGranted = true; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, Manual(data.Post, "preview"), new { connectionId = data.Connection })).StatusCode);
        // Confirm a valid encrypted grant, then change the post after preview.
        await using (var db = Db(a.Id)) {
            var c = await db.SocialConnections.SingleAsync();
            using var scope = factory.Services.CreateScope();
            c.ProtectedCredential = scope.ServiceProvider.GetRequiredService<ISocialSecretProtector>().Protect(JsonSerializer.Serialize(new SocialCredential("workspace-secret", null, c.ExpiresAtUtc, null, ["w_member_social"])), $"SocialFlow/SaaS/v1/{a.Id}/{c.Id}/credential");
            await db.SaveChangesAsync();
        }
        var preview = (await (await Send(alice, HttpMethod.Post, Manual(data.Post, "preview"), new { connectionId = data.Connection })).Content.ReadFromJsonAsync<LinkedInPreview>())!;
        await using (var db = Db(a.Id)) { var p = await db.SocialPosts.SingleAsync(); p.Cancel(); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, Manual(data.Post, "publish"), new { preview.Confirmation })).StatusCode);
        Assert.Equal(0, linkedInDispatch.Calls);
        await using var check = Db(a.Id); Assert.Empty(await check.PublicationOperations.ToListAsync());
    }
    [Theory]
    [InlineData(HttpStatusCode.Created, PublicationOperationState.Confirmed)]
    [InlineData(HttpStatusCode.Forbidden, PublicationOperationState.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, PublicationOperationState.Indeterminate)]
    public async Task WorkspaceLinkedIn_ReusesPublisherAndDurableOperationWithoutResending(HttpStatusCode status, PublicationOperationState expected)
    {
        var data = await LinkedInData(a.Id); linkedInDispatch.Status = status;
        var response = await Send(alice, HttpMethod.Post, Manual(data.Post, "preview"), new { connectionId = data.Connection });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<LinkedInPreview>())!;
        Assert.Equal(0, linkedInDispatch.Calls);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Send(alice, HttpMethod.Post, Manual(data.Post, "publish"), new { preview.Confirmation })));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, linkedInDispatch.Calls);
        var operation = (await alice.GetFromJsonAsync<LinkedInPublicationView>(Manual(data.Post, "publication")))!.Operation!;
        Assert.Equal(expected, operation.State);
        await using var db = Db(a.Id);
        Assert.Equal(operation.OperationId, (await db.PublicationOperations.SingleAsync()).OperationId);
        Assert.DoesNotContain("workspace-secret", await results[0].Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Manual(data.Post, "publication"))).StatusCode);
    }
    [Fact]
    public async Task WorkspaceLinkedIn_RejectsForeignMissingGrantAndUnscheduledPosts()
    {
        var own = await LinkedInData(a.Id, false); var foreign = await LinkedInData(b.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, Manual(own.Post, "preview"), new { connectionId = foreign.Connection })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, Manual(own.Post, "preview"), new { connectionId = own.Connection })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(alice, HttpMethod.Post, Manual(foreign.Post, "preview"), new { connectionId = own.Connection })).StatusCode);
        await using (var db = Db(a.Id)) { var p = SocialPost.Create("Approved", "text", SocialPlatform.LinkedIn); p.SubmitForReview(); p.Approve(); db.SocialPosts.Add(p); await db.SaveChangesAsync(); Assert.False((await alice.GetFromJsonAsync<LinkedInPublicationView>(Manual(p.Id, "publication")))!.CanPublish); }
        Assert.Equal(0, linkedInDispatch.Calls);
    }
    [Theory]
    [InlineData(WorkspaceRole.Owner, true)] [InlineData(WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Editor, true)] [InlineData(WorkspaceRole.Viewer, false)]
    public async Task WorkspaceLinkedIn_RolesAndChangedConnectionAreChecked(WorkspaceRole role, bool allowed)
    {
        var data = await LinkedInData(a.Id);
        await using (var db = Db(a.Id)) { db.WorkspaceMemberships.Add(WorkspaceMembership.Join(a.Id, bobId, role)); await db.SaveChangesAsync(); }
        bob.DefaultRequestHeaders.Remove("X-Workspace-Id"); bob.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        var response = await Send(bob, HttpMethod.Post, Manual(data.Post, "preview"), new { connectionId = data.Connection });
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (allowed) {
            var preview = (await response.Content.ReadFromJsonAsync<LinkedInPreview>())!;
            await using (var db = Db(a.Id)) { var connection = await db.SocialConnections.SingleAsync(); connection.PublishingPermissionGranted = false; connection.Revision = Guid.NewGuid(); await db.SaveChangesAsync(); }
            Assert.Equal(HttpStatusCode.Conflict, (await Send(bob, HttpMethod.Post, Manual(data.Post, "publish"), new { preview.Confirmation })).StatusCode);
        }
        Assert.Equal(0, linkedInDispatch.Calls);
        await using var check = Db(a.Id); Assert.Empty(await check.PublicationOperations.ToListAsync());
    }
}


