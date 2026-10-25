using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Infrastructure.Accounts;
using Xunit;
namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed partial class WorkspaceIsolationTests
{
    private async Task<Guid> Organization()
    {
        var response = await Send(alice, HttpMethod.Post, "/api/workspaces", new { name = "Collaboration" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!.Id;
        alice.DefaultRequestHeaders.Remove("X-Workspace-Id"); alice.DefaultRequestHeaders.Add("X-Workspace-Id", id.ToString());
        return id;
    }
    private async Task<string> Invite(Guid id, WorkspaceRole role = WorkspaceRole.Editor, string email = "bob@example.test")
    {
        var response = await Send(alice, HttpMethod.Post, $"/api/workspaces/{id}/team/invitations", new { email, role });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return mailbox.Links.Last().Split("code=")[1];
    }
    private static Task<HttpResponseMessage> Accept(HttpClient client, string code) => Send(client, HttpMethod.Post, "/api/invitations/accept", new { code });
    private Task<WorkspaceTeam?> Team(HttpClient client, Guid id) => client.GetFromJsonAsync<WorkspaceTeam>($"/api/workspaces/{id}/team");

    [Theory]
    [InlineData(WorkspaceRole.Owner)] [InlineData(WorkspaceRole.Admin)] [InlineData(WorkspaceRole.Editor)] [InlineData(WorkspaceRole.Viewer)]
    public async Task RolesAreEnforced_ServerSide_AndSelfApprovalIsAllowed(WorkspaceRole role)
    {
        var id = await Organization(); var code = await Invite(id, role);
        Assert.Equal(HttpStatusCode.OK, (await Accept(bob, code)).StatusCode);
        bob.DefaultRequestHeaders.Remove("X-Workspace-Id"); bob.DefaultRequestHeaders.Add("X-Workspace-Id", id.ToString());
        Assert.Equal(role, (await Team(bob, id))!.MyRole);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/posts")).StatusCode);
        if (role == WorkspaceRole.Viewer)
        {
            var post = await Create(alice, "Owner draft");
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Post, "/api/posts", new { title = "Forbidden", platform = 1 })).StatusCode);
            foreach (var action in new[] { "submit-for-review", "approve", "reject", "schedule", "cancel" })
                Assert.Equal(HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Post, $"/api/posts/{post.Id}/{action}", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Put, $"/api/posts/{post.Id}", new { title = "Forbidden" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Delete, $"/api/posts/{post.Id}")).StatusCode);
        }
        else
        {
            var post = await Create(bob, "My own draft");
            Assert.Equal(HttpStatusCode.OK, (await Send(bob, HttpMethod.Post, $"/api/posts/{post.Id}/submit-for-review", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(bob, HttpMethod.Post, $"/api/posts/{post.Id}/approve", new { })).StatusCode);
            Assert.Equal(3, (int)(await bob.GetFromJsonAsync<SocialPostDto>($"/api/posts/{post.Id}"))!.Status);
        }
        if (role is WorkspaceRole.Editor or WorkspaceRole.Viewer)
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Post, $"/api/workspaces/{id}/team/invitations", new { email = "third@example.test", role = 1 })).StatusCode);
    }

    [Fact]
    public async Task InvitationsRequireRecipient_Expire_Revoke_AndCannotBeReplayedOrDuplicated()
    {
        var id = await Organization(); var code = await Invite(id);
        Assert.Equal(HttpStatusCode.BadRequest, (await Accept(alice, code)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, $"/api/workspaces/{id}/team/invitations", new { email = "BOB@example.test", role = 2 })).StatusCode);
        var pending = Assert.Single((await Team(alice, id))!.Invitations);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(alice, HttpMethod.Post, $"/api/workspaces/{id}/team/invitations/{pending.Id}/revoke", new { pending.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Accept(bob, code)).StatusCode);
        var expired = await Invite(id);
        await using (var db = Db(id))
        {
            var row = await db.WorkspaceInvitations.SingleAsync(x => x.Status == InvitationStatus.Pending);
            Assert.DoesNotContain(expired, row.TokenHash); Assert.Equal(64, row.TokenHash.Length);
            row.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Accept(bob, expired)).StatusCode);
        var valid = await Invite(id);
        Assert.Equal(HttpStatusCode.OK, (await Accept(bob, valid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Accept(bob, valid)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, $"/api/workspaces/{id}/team/invitations", new { email = "bob@example.test", role = 2 })).StatusCode);
    }

    [Fact]
    public async Task AdminCannotManageOwners_StaleChangesConflict_AndRevocationTakesEffect()
    {
        var id = await Organization(); Assert.Equal(HttpStatusCode.OK, (await Accept(bob, await Invite(id, WorkspaceRole.Admin))).StatusCode);
        var team = (await Team(alice, id))!; var owner = team.Members.Single(x => x.UserId == aliceId); var admin = team.Members.Single(x => x.UserId == bobId);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Put, $"/api/workspaces/{id}/team/members/{bobId}", new { role = 1, admin.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Post, $"/api/workspaces/{id}/team/members/{aliceId}/remove", new { owner.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(alice, HttpMethod.Post, $"/api/workspaces/{id}/team/leave", new { owner.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(alice, HttpMethod.Put, $"/api/workspaces/{id}/team/members/{bobId}", new { role = 4, admin.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Put, $"/api/workspaces/{id}/team/members/{bobId}", new { role = 2, admin.Revision })).StatusCode);
        var viewer = (await Team(alice, id))!.Members.Single(x => x.UserId == bobId);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(bob, HttpMethod.Post, $"/api/workspaces/{id}/team/leave", new { viewer.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/workspaces/{id}/team")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/api/workspaces/{b.Id}/team")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/api/workspaces/{Workspace.HistoricalId}/team")).StatusCode);
    }

    [Fact]
    public async Task NewRecipientMustConfirmBeforeAccepting_AndPersonalSpacesRejectTeams()
    {
        var id = await Organization(); var code = await Invite(id, email: "new@example.test");
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Accept(client, code)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { Email = "new@example.test", UserName = "new@example.test" };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, HttpMethod.Post, "/api/auth/login", new { email = user.Email, password = Password })).StatusCode);
        Assert.True((await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user))).Succeeded);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, "/api/auth/login", new { email = user.Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Accept(client, code)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/api/workspaces/{a.Id}/team")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/workspaces/{id}/team/invitations", new { email = "csrf@example.test", role = 2 })).StatusCode);
    }

    [Fact]
    public async Task ForeignTeamMutationsAndInvitationIdsAreRejected_AndRemovedIssuerCannotGrantAccess()
    {
        var id = await Organization(); var code = await Invite(id);
        var pending = Assert.Single((await Team(alice, id))!.Invitations);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Put, $"/api/workspaces/{id}/team/members/{aliceId}", new { role = 4, revision = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(bob, HttpMethod.Post, $"/api/workspaces/{id}/team/invitations/{pending.Id}/revoke", new { pending.Revision })).StatusCode);
        var other = await Organization();
        Assert.Equal(HttpStatusCode.NotFound, (await Send(alice, HttpMethod.Post, $"/api/workspaces/{other}/team/invitations/{pending.Id}/revoke", new { pending.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Accept(bob, new string('x', 43))).StatusCode);
        await using (var db = Db(id))
        {
            db.WorkspaceMemberships.Add(WorkspaceMembership.Join(id, "replacement-owner")); await db.SaveChangesAsync();
            var issuer = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == id && x.UserId == aliceId);
            issuer.Change(WorkspaceRole.Viewer, MembershipStatus.Active, 2); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Accept(bob, code)).StatusCode);
    }

    [Fact]
    public async Task PersonalOwnerCanApproveOwnPublication_AndConcurrentInvitationAcceptanceCreatesOneMembership()
    {
        var post = await Create(alice, "Personal self approval");
        Assert.Equal(HttpStatusCode.OK, (await Send(alice, HttpMethod.Post, $"/api/posts/{post.Id}/submit-for-review", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(alice, HttpMethod.Post, $"/api/posts/{post.Id}/approve", new { })).StatusCode);
        var id = await Organization(); var code = await Invite(id);
        // Separate HTTP requests exercise transaction serialization and one-use tokens.
        var results = await Task.WhenAll(Accept(bob, code), Accept(bob, code));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.BadRequest);
        await using var db = Db(id);
        Assert.Single(await db.WorkspaceMemberships.Where(x => x.WorkspaceId == id && x.UserId == bobId).ToListAsync());
    }
}
