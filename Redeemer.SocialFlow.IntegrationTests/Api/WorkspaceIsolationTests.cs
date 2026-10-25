using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Application.Knowledge;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Accounts;
using Redeemer.SocialFlow.Infrastructure.Knowledge;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Publishing;
using Redeemer.SocialFlow.Infrastructure.Visuals;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed partial class WorkspaceIsolationTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "socialflow-tenants-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> factory = null!;
    private DbContextOptions<SocialFlowDbContext> options = null!;
    private HttpClient alice = null!, bob = null!;
    private WorkspaceDto a = null!, b = null!;
    private string aliceId = "", bobId = "";
    private readonly NoMail mailbox = new();
    private const string Password = "Password-For-Test-123!";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        var business = $"Data Source={Path.Combine(root, "business.db")};Pooling=False";
        options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(business).Options;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .UseSetting("ConnectionStrings:SocialFlow", business)
            .UseSetting("ConnectionStrings:Accounts", $"Data Source={Path.Combine(root, "accounts.db")};Pooling=False")
            .UseSetting("Visuals:Enabled", "true").UseSetting("Facebook:Enabled", "true")
            .UseSetting("LocalAdministration:Enabled", "false")
            .ConfigureServices(services =>
            {
                services.RemoveAll<IDataProtectionProvider>(); services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.RemoveAll<IAccountEmailSender>(); services.AddSingleton<IAccountEmailSender>(mailbox);
            }));
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userA = new ApplicationUser { UserName = "alice@example.test", Email = "alice@example.test", EmailConfirmed = true };
        var userB = new ApplicationUser { UserName = "bob@example.test", Email = "bob@example.test", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(userA, Password)).Succeeded); Assert.True((await users.CreateAsync(userB, Password)).Succeeded);
        aliceId = userA.Id; bobId = userB.Id;
        alice = Client(); bob = Client();
        Assert.Equal(HttpStatusCode.NoContent, (await Send(alice, HttpMethod.Post, "/api/auth/login", new { email = userA.Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(bob, HttpMethod.Post, "/api/auth/login", new { email = userB.Email, password = Password })).StatusCode);
        a = (await (await Send(alice, HttpMethod.Post, "/api/workspaces/personal", new { })).Content.ReadFromJsonAsync<WorkspaceDto>())!;
        b = (await (await Send(bob, HttpMethod.Post, "/api/workspaces/personal", new { })).Content.ReadFromJsonAsync<WorkspaceDto>())!;
        alice.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString()); bob.DefaultRequestHeaders.Add("X-Workspace-Id", b.Id.ToString());
    }
    private HttpClient Client() => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost:65474"), AllowAutoRedirect = false });
    private SocialFlowDbContext Db(Guid id) => new(options, new ExplicitWorkspaceContext(id));
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private static async Task<SocialPostDto> Create(HttpClient client, string title)
    {
        var response = await Send(client, HttpMethod.Post, "/api/posts", new { title, content = "Private content", platform = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SocialPostDto>())!;
    }

    [Fact]
    public async Task Lists_DirectAccess_Mutations_AndTrashAreIsolated()
    {
        var own = await Create(alice, "Alice private"); var foreign = await Create(bob, "Bob private");
        Assert.Equal(own.Id, Assert.Single((await alice.GetFromJsonAsync<SocialPostDto[]>("/api/posts"))!).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/posts/{foreign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(alice, HttpMethod.Put, $"/api/posts/{foreign.Id}", new { title = "Attack", content = "attack" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(alice, HttpMethod.Delete, $"/api/posts/{foreign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(bob, HttpMethod.Delete, $"/api/posts/{foreign.Id}")).StatusCode);
        Assert.Empty((await alice.GetFromJsonAsync<TrashedSocialPostDto[]>("/api/posts/trash"))!);
        Assert.Equal(foreign.Id, Assert.Single((await bob.GetFromJsonAsync<TrashedSocialPostDto[]>("/api/posts/trash"))!).Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(alice, HttpMethod.Delete, $"/api/posts/{own.Id}")).StatusCode);
        Assert.Equal(own.Id, Assert.Single((await alice.GetFromJsonAsync<TrashedSocialPostDto[]>("/api/posts/trash"))!).Id);
    }

    [Fact]
    public async Task MembershipIsRechecked_AndBrowserWorkspaceIdCannotGrantAccess()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Send(alice, HttpMethod.Post, $"/api/workspaces/{b.Id}/select", new { })).StatusCode);
        alice.DefaultRequestHeaders.Remove("X-Workspace-Id");
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/posts")).StatusCode);
        alice.DefaultRequestHeaders.Add("X-Workspace-Id", b.Id.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/posts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/workspaces/{b.Id}")).StatusCode);
        await using var db = Db(a.Id);
        db.WorkspaceMemberships.Add(WorkspaceMembership.Join(a.Id, bobId)); await db.SaveChangesAsync();
        bob.DefaultRequestHeaders.Remove("X-Workspace-Id"); bob.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/posts")).StatusCode);
        var membership = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == a.Id && x.UserId == bobId);
        membership.Change(WorkspaceRole.Editor, MembershipStatus.Inactive, 2); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/posts")).StatusCode);
    }

    [Fact]
    public async Task PersonalWorkspaceIsIdempotent_OrganizationGetsOwner_AndHistoricalIsUnassigned()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IWorkspaceDirectory>();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => directory.EnsurePersonalAsync(aliceId)));
        Assert.All(results, result => Assert.Equal(a.Id, result.Id));
        var created = await Send(alice, HttpMethod.Post, "/api/workspaces", new { name = "Alice Studio" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var organization = (await created.Content.ReadFromJsonAsync<WorkspaceDto>())!;
        Assert.Equal(WorkspaceRole.Owner, organization.Role); Assert.Equal(WorkspaceType.Organization, organization.Type);
        Assert.Equal(2, (await directory.ListAsync(aliceId)).Count); Assert.Single(await directory.ListAsync(bobId));
        await using var db = Db(a.Id);
        Assert.Empty(await db.WorkspaceMemberships.Where(x => x.WorkspaceId == Workspace.HistoricalId).ToListAsync());
        Assert.Equal(WorkspaceStatus.Suspended, (await db.Workspaces.SingleAsync(x => x.Id == Workspace.HistoricalId)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/workspaces", new { name = "No CSRF" })).StatusCode);
    }

    [Fact]
    public async Task ConfirmationProvisionsPersonalWorkspace_AndConcurrentRetriesKeepOneOwner()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "pending@example.test", Email = "pending@example.test" };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        var directory = scope.ServiceProvider.GetRequiredService<IWorkspaceDirectory>();
        Assert.Empty(await directory.ListAsync(user.Id));
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var encoded = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));
        Assert.True(await scope.ServiceProvider.GetRequiredService<IAccountService>().ConfirmAsync(user.Email, encoded));
        var personal = Assert.Single(await directory.ListAsync(user.Id));
        Assert.Equal(WorkspaceRole.Owner, personal.Role);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => directory.EnsurePersonalAsync(user.Id))));
        Assert.All(results, result => Assert.Equal(personal.Id, result.Id));
        var second = new ApplicationUser { UserName = "concurrent@example.test", Email = "concurrent@example.test", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(second, Password)).Succeeded);
        var firstCreations = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => directory.EnsurePersonalAsync(second.Id))));
        Assert.Single(firstCreations.Select(x => x.Id).Distinct());
        await using var db = Db(personal.Id);
        Assert.Single(await db.WorkspaceMemberships.Where(x => x.WorkspaceId == personal.Id).ToListAsync());
    }

    [Fact]
    public async Task HistoricalAssignmentRequiresExplicitConfirmedUser_AndCannotReassign()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var pending = new ApplicationUser { UserName = "owner-pending@example.test", Email = "owner-pending@example.test" };
        Assert.True((await users.CreateAsync(pending, Password)).Succeeded);
        await using var db = Db(Workspace.HistoricalId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Redeemer.SocialFlow.Api.Development.HistoricalWorkspaceOwner.AssignAsync(pending.Id, db, users));
        await Redeemer.SocialFlow.Api.Development.HistoricalWorkspaceOwner.AssignAsync(aliceId, db, users);
        await Redeemer.SocialFlow.Api.Development.HistoricalWorkspaceOwner.AssignAsync(aliceId, db, users);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Redeemer.SocialFlow.Api.Development.HistoricalWorkspaceOwner.AssignAsync(bobId, db, users));
        var directory = scope.ServiceProvider.GetRequiredService<IWorkspaceDirectory>();
        Assert.NotNull(await directory.GetAsync(aliceId, Workspace.HistoricalId));
        Assert.Null(await directory.GetAsync(bobId, Workspace.HistoricalId));
        Assert.Equal(aliceId, (await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == Workspace.HistoricalId)).UserId);
    }

    private async Task<(SocialPost Post, KnowledgeDocument Document, PostVisual Visual, StoredVisualIdentity Identity, IdentityLogo Logo)> Seed(Guid id)
    {
        await using var db = Db(id);
        var post = SocialPost.Create("Private", "Content", SocialPlatform.Facebook);
        var document = KnowledgeDocument.Create("Private knowledge", "theme", ["theme"], [KnowledgeUsage.Generation], SourceType.Other, AuthorityLevel.Low, "fr");
        document.ChangeStatus(KnowledgeDocumentStatus.Approved);
        var identity = StoredVisualIdentity.Create("BRAND", "Private brand", "Flat", ["#123456"], null, null, DateTimeOffset.UtcNow);
        identity.SetDefault(true);
        db.AddRange(post, document, identity); await db.SaveChangesAsync();
        var visual = PostVisual.Create(post, VisualOrigin.Uploaded, "image/png", 10, 10, 100, new string('a', 64), null, DateTimeOffset.UtcNow); visual.Complete();
        var logo = IdentityLogo.Reserve(identity.Id, "image/png", 10, 10, 100, new string('b', 64)); logo.Complete();
        db.AddRange(visual, logo, KnowledgeChunk.Create(document.Id, "Confidential knowledge theme", 0),
            AiGeneration.Create(post.Id, "Subject", "Goal", "Audience", post.Platform, "Fake", "Fake", []));
        await db.SaveChangesAsync();
        identity.SetLogo(logo.Id); post.SelectVisual(visual, DateTimeOffset.UtcNow); await db.SaveChangesAsync();
        return (post, document, visual, identity, logo);
    }

    [Fact]
    public async Task KnowledgeSearch_Visuals_Identities_Logos_AndAuditAreScoped()
    {
        var own = await Seed(a.Id); var foreign = await Seed(b.Id);
        await using var db = Db(a.Id);
        var found = await new SqliteKnowledgePassageSearch(db).SearchForGenerationAsync(new("knowledge", 10));
        Assert.Equal(own.Document.Id, Assert.Single(found).KnowledgeDocumentId);
        Assert.Equal(own.Visual.Id, (await db.PostVisuals.SingleAsync()).Id);
        Assert.Equal(own.Identity.Id, (await db.VisualIdentities.SingleAsync()).Id);
        Assert.Equal(own.Logo.Id, (await db.IdentityLogos.SingleAsync()).Id);
        Assert.Equal(own.Post.Id, (await db.AiGenerations.SingleAsync()).SocialPostId);
        Assert.Null(await db.PostVisuals.SingleOrDefaultAsync(x => x.Id == foreign.Visual.Id));
        var repository = new SqliteVisualIdentityRepository(options, new ExplicitWorkspaceContext(a.Id));
        Assert.Equal(own.Identity.Id, Assert.Single(await repository.ListAsync(default)).Id);
        Assert.Null(await db.KnowledgeDocuments.SingleOrDefaultAsync(x => x.Id == foreign.Document.Id));
        foreach (var path in new[] { $"/api/posts/{foreign.Post.Id}/visuals/{foreign.Visual.Id}", $"/api/visual-identities/{foreign.Identity.Id}/logos/{foreign.Logo.Id}", "/api/dev/linkedin/status", "/api/dev/facebook/runtime", "/api/dev/knowledge/documents" })
            Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task CompositeForeignKeysRejectCrossWorkspaceReferences()
    {
        var foreign = await Seed(b.Id); var own = await Seed(a.Id);
        await using (var db = Db(a.Id))
        {
            db.KnowledgeChunks.Add(KnowledgeChunk.Create(foreign.Document.Id, "Attack", 0));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = Db(a.Id))
        {
            db.AiGenerations.Add(AiGeneration.Create(foreign.Post.Id, "S", "O", "A", SocialPlatform.Facebook, "Fake", "Fake", []));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = Db(a.Id))
        {
            var identity = await db.VisualIdentities.SingleAsync(); identity.SetLogo(foreign.Logo.Id);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = Db(a.Id))
        {
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SocialPosts SET SelectedVisualId={foreign.Visual.Id} WHERE Id={own.Post.Id}"));
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SocialPosts SET WorkspaceId={b.Id} WHERE Id={own.Post.Id}"));
        }
    }

    [Fact]
    public async Task UnboundContextsAndForeignTrackedWritesFailClosed()
    {
        var foreign = await Seed(b.Id);
        await using (var unbound = new SocialFlowDbContext(options))
        {
            Assert.Empty(await unbound.SocialPosts.ToListAsync());
            Assert.Empty(await unbound.PostVisuals.ToListAsync());
            unbound.SocialPosts.Add(SocialPost.Create("No workspace", null, SocialPlatform.Facebook));
            await Assert.ThrowsAsync<InvalidOperationException>(() => unbound.SaveChangesAsync());
        }
        await using var db = Db(a.Id);
        db.Attach(foreign.Post); foreign.Post.Update("Intrusion", "Attack", null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        var store = new SqlitePublicationOperationStore(options, workspace: new ExplicitWorkspaceContext(a.Id));
        await Assert.ThrowsAsync<PostNotFoundException>(() => store.TryClaimAsync(new(foreign.Post.Id, Guid.NewGuid(), "Fake", "Fake")));
    }

    [Fact]
    public async Task LastActiveOwnerCannotBeDeletedOrDisabled_EvenThroughSql()
    {
        await using var db = Db(a.Id);
        var member = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == a.Id);
        Assert.Throws<Redeemer.SocialFlow.Domain.Exceptions.DomainException>(() => member.Change(WorkspaceRole.Editor, MembershipStatus.Active, 1));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM WorkspaceMemberships WHERE WorkspaceId={a.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE WorkspaceMemberships SET Status=2 WHERE WorkspaceId={a.Id}"));
        db.WorkspaceMemberships.Add(WorkspaceMembership.Join(a.Id, bobId)); await db.SaveChangesAsync();
        member.Change(WorkspaceRole.Editor, MembershipStatus.Active, 2); await db.SaveChangesAsync();
        Assert.Equal(1, await db.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == a.Id && x.Role == WorkspaceRole.Owner && x.Status == MembershipStatus.Active));
    }

    public async Task DisposeAsync() { alice.Dispose(); bob.Dispose(); await factory.DisposeAsync(); Directory.Delete(root, true); }
    private sealed class NoMail : IAccountEmailSender
    {
        public List<string> Links { get; } = [];
        public Task SendAsync(string email, string purpose, string link, CancellationToken token)
        { Links.Add(link); return Task.CompletedTask; }
    }
}
