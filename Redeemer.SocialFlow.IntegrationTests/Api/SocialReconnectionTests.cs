using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Social;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed class SocialReconnectionTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "social-reconnect-" + Guid.NewGuid() + ".db");
    private DbContextOptions<SocialFlowDbContext> options = null!;
    private readonly Workspace first = Workspace.Create("First", WorkspaceType.Organization, "actor");
    private readonly Workspace second = Workspace.Create("Second", WorkspaceType.Organization, "actor");
    private SocialConnections service = null!;
    public async Task InitializeAsync()
    {
        options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite($"Data Source={path};Pooling=False;Default Timeout=15").Options;
        await using var db = new SocialFlowDbContext(options);
        await db.Database.MigrateAsync();
        db.Workspaces.AddRange(first, second);
        db.WorkspaceMemberships.AddRange(WorkspaceMembership.Join(first.Id,"actor"),WorkspaceMembership.Join(second.Id,"actor"));
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["SocialConnections:Encryption:ActiveKeyId"]="test", ["SocialConnections:Encryption:Keys:test"]=Convert.ToBase64String(new byte[32])
        }).Build();
        service = new(options,new Actor(),Enum.GetValues<SocialPlatform>().Select(p => new Provider(p)),new SocialSecretProtector(config),TimeProvider.System);
    }
    public Task DisposeAsync() { File.Delete(path); return Task.CompletedTask; }
    private async Task<Guid> Discover(Guid workspace, SocialPlatform platform)
    {
        var url = await service.StartAsync(workspace,platform,default);
        var state = QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
        return (await service.CallbackAsync(platform,state,"fake",false,default)).SessionId;
    }
    private async Task Connect(Guid workspace, SocialPlatform platform) =>
        await service.SelectAsync(workspace,await Discover(workspace,platform),["stable-one","stable-two"],default);

    [Theory]
    [InlineData(SocialPlatform.Facebook)] [InlineData(SocialPlatform.LinkedIn)] [InlineData(SocialPlatform.Instagram)]
    public async Task ReconnectionReusesRevokedIdentity_PreservesDestinations_AndSameNamesRemainDistinct(SocialPlatform platform)
    {
        await Connect(first.Id,platform);
        var original = await service.ListAsync(first.Id,default);
        Assert.Equal(2,original.Count); Assert.Equal(original[0].DisplayName,original[1].DisplayName);
        await using var db = new SocialFlowDbContext(options,new ExplicitWorkspaceContext(first.Id));
        var post = SocialPost.Create("Draft","Content",platform); db.SocialPosts.Add(post); await db.SaveChangesAsync();
        db.PostSocialDestinations.Add(new(){WorkspaceId=first.Id,SocialPostId=post.Id,SocialConnectionId=original[0].Id}); await db.SaveChangesAsync();
        await service.DisconnectAsync(first.Id,original[0].Id,original[0].Revision,default);
        for(var i=0;i<3;i++) await Connect(first.Id,platform);
        var current = await service.ListAsync(first.Id,default);
        Assert.Equal(original.Select(x=>x.Id).Order(),current.Select(x=>x.Id).Order());
        Assert.All(current,x=>Assert.Equal(SocialConnectionStatus.Connected,x.ConnectionStatus));
        Assert.NotEqual(original[0].Revision,current.Single(x=>x.Id==original[0].Id).Revision);
        Assert.Equal(original[0].Id,(await db.PostSocialDestinations.SingleAsync()).SocialConnectionId);
        await Connect(second.Id,platform);
        var foreign = await service.ListAsync(second.Id,default);
        Assert.Equal(2,foreign.Count); Assert.Empty(foreign.Select(x=>x.Id).Intersect(current.Select(x=>x.Id)));
    }

    [Theory]
    [InlineData(SocialPlatform.Facebook)] [InlineData(SocialPlatform.LinkedIn)] [InlineData(SocialPlatform.Instagram)]
    public async Task ConcurrentIndependentSelectionsUpdateOneIdentity_AndSameSessionCannotReplay(SocialPlatform platform)
    {
        var one=await Discover(first.Id,platform); var two=await Discover(first.Id,platform);
        using var barrier = new Barrier(2);
        Task Select(Guid session) => Task.Run(async()=> { barrier.SignalAndWait(); await service.SelectAsync(first.Id,session,["stable-one"],default); });
        await Task.WhenAll(Select(one),Select(two));
        Assert.Single(await service.ListAsync(first.Id,default));
        var shared=await Discover(first.Id,platform);
        var outcomes=await Task.WhenAll(Task.Run(()=>Record.ExceptionAsync(()=>service.SelectAsync(first.Id,shared,["stable-one"],default))),Task.Run(()=>Record.ExceptionAsync(()=>service.SelectAsync(first.Id,shared,["stable-one"],default))));
        Assert.Single(outcomes,x=>x is null); Assert.Single(outcomes,x=>x is SocialConnectionException);
        Assert.Single(await service.ListAsync(first.Id,default));
    }
    private sealed class Actor : ICollaborationActor { public string UserId=>"actor"; public string Email=>"actor@example.test"; }
    private sealed class Provider(SocialPlatform platform) : IWorkspaceOAuthProvider
    {
        public SocialPlatform Platform=>platform; public bool Available=>true; public bool SupportsPkce=>false;
        public string AuthorizationUrl(string state,string? challenge)=>"https://provider.example.test/?state="+state;
        public Task<IReadOnlyList<SocialCandidate>> DiscoverAsync(string code,string? verifier,CancellationToken ct)=>Task.FromResult<IReadOnlyList<SocialCandidate>>(
            new[]{"stable-one","stable-two"}.Select(id=>new SocialCandidate(id,"Same display name","Account",new("fake-token",null,DateTimeOffset.UtcNow.AddHours(1),null,[]),false)).ToArray());
        public Task<SocialCredential> ValidateAsync(string id,string type,SocialCredential credential,CancellationToken ct)=>Task.FromResult(credential);
    }
}
