using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Infrastructure.Social;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Api;

public sealed partial class WorkspaceIsolationTests
{
    [Theory]
    [InlineData(WorkspaceRole.Owner,0)] [InlineData(WorkspaceRole.Owner,1)] [InlineData(WorkspaceRole.Owner,3)]
    [InlineData(WorkspaceRole.Admin,0)] [InlineData(WorkspaceRole.Admin,1)] [InlineData(WorkspaceRole.Admin,3)]
    [InlineData(WorkspaceRole.Owner,-1)]
    [InlineData(WorkspaceRole.Owner,-2)]
    [InlineData(WorkspaceRole.Owner,-3)]
    public async Task FacebookCallbackExposesDiagnosticOverHttp_ForInitiatorOnly(WorkspaceRole role,int pageCount)
    {
        var workspace=await Organization();
        await using(var db=Db(workspace)) {
            db.WorkspaceMemberships.Add(WorkspaceMembership.Join(workspace,bobId,WorkspaceRole.Owner));
            await db.SaveChangesAsync();
            var member=await db.WorkspaceMemberships.SingleAsync(x=>x.WorkspaceId==workspace && x.UserId==aliceId);
            member.Change(role,MembershipStatus.Active,2); await db.SaveChangesAsync();
        }
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Authentication:PublicOrigin"]="https://localhost:65474",["SocialConnections:Providers:Facebook:Enabled"]="true",
            ["SocialConnections:Providers:Facebook:ClientId"]="test-app",["SocialConnections:Providers:Facebook:ClientSecret"]="SECRET-app",
            ["SocialConnections:Providers:Facebook:ApiVersion"]="v26.0",["SocialConnections:Providers:Facebook:Scopes"]="pages_show_list"
        }).Build();
        await using var host=factory.WithWebHostBuilder(builder=>builder.ConfigureServices(services=> {
            services.RemoveAll<IWorkspaceOAuthProvider>();
            services.AddScoped<IWorkspaceOAuthProvider>(_=>new FacebookWorkspaceOAuthProvider(new(new DiscoveryMetaHandler(pageCount)),config,TimeProvider.System));
        }));
        HttpClient ClientForTest()=>host.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost:65474"),AllowAutoRedirect=false});
        using var initiator=ClientForTest(); using var another=ClientForTest();
        Assert.Equal(HttpStatusCode.NoContent,(await Send(initiator,HttpMethod.Post,"/api/auth/login",new{email="alice@example.test",password=Password})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await Send(another,HttpMethod.Post,"/api/auth/login",new{email="bob@example.test",password=Password})).StatusCode);
        var start=await Send(initiator,HttpMethod.Post,Social(workspace)+"/start",new{platform=1});
        var url=(await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizationUrl").GetString()!;
        var state=QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
        var callback=await initiator.GetAsync("/api/social-oauth/1/callback?state="+state+"&code=fake-code");
        Assert.Equal(HttpStatusCode.Redirect,callback.StatusCode);
        var query=QueryHelpers.ParseQuery("?"+callback.Headers.Location!.OriginalString.Split('?')[1]);
        Assert.Equal("1",query["platform"]); Assert.Equal(workspace.ToString(),query["workspace"]);
        var session=Guid.Parse(query["session"]!);
        var endpoint=Social(workspace)+$"/sessions/{session}/diagnostics";
        var response=await initiator.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal("application/json",response.Content.Headers.ContentType!.MediaType);
        var body=await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRET",body); Assert.DoesNotContain("access_token",body); Assert.DoesNotContain("test-app",body);
        var diagnostic=JsonSerializer.Deserialize<SocialDiscoveryDiagnostic>(body,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var retained = pageCount == -3 ? 1 : Math.Max(0,pageCount);
        var received = pageCount == -2 ? 0 : pageCount <= 0 ? 1 : pageCount;
        Assert.Equal(received,diagnostic.PagesReceived);
        Assert.Equal(retained,diagnostic.PagesRetained);
        Assert.Equal(pageCount==-2?"graph_error":pageCount==-3?"pages_without_publishing":pageCount<=0?"pages_filtered":"pages_without_publishing",diagnostic.Outcome);
        Assert.Equal(received,diagnostic.Pages!.Count);
        Assert.Equal("USER",diagnostic.Facebook!.TokenType); Assert.True(diagnostic.Facebook.AppIdMatches);
        Assert.True(diagnostic.Facebook.PagesShowList); Assert.False(diagnostic.Facebook.PagesManagePosts);
        Assert.Equal("array",diagnostic.Facebook.Granular!.Shape);
        Assert.Equal(new[]{"810001"},diagnostic.Facebook.Granular.Permissions.Single(x=>x.Permission=="pages_show_list").TargetIds);
        Assert.Equal("empty_array",diagnostic.Facebook.Granular.Permissions.Single(x=>x.Permission=="pages_read_engagement").TargetsStatus);
        Assert.Equal("missing",diagnostic.Facebook.Granular.Permissions.Single(x=>x.Permission=="pages_manage_posts").TargetsStatus);
        Assert.DoesNotContain("PRIVATE-RESOURCE",body);
        var listResponse=Assert.Single(diagnostic.Facebook.Responses!,x=>x.Stage=="pages");
        Assert.Equal(200,listResponse.HttpStatus);
        Assert.Equal(pageCount==-2,listResponse.GraphError);
        if(pageCount>0) Assert.Equal("100",diagnostic.Pages[0].ExternalPageId);
        if(pageCount==-1) Assert.Equal(1,diagnostic.Exclusions["missing_page_token"]);
        if(pageCount==-2) {
            Assert.False(diagnostic.Complete); Assert.Equal(190,listResponse.GraphCode);
            Assert.Equal("empty_array",listResponse.DataShape);
            Assert.Empty((await initiator.GetFromJsonAsync<SocialCandidateDto[]>(Social(workspace)+$"/sessions/{session}"))!);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(initiator,HttpMethod.Post,Social(workspace)+$"/sessions/{session}/select",new{externalAccountIds=new[]{"100"}})).StatusCode);
        } else {
            var candidates=await initiator.GetFromJsonAsync<SocialCandidateDto[]>(Social(workspace)+$"/sessions/{session}");
            Assert.Equal(retained,candidates!.Length);
            if (pageCount == -3)
            {
                Assert.False(Assert.Single(candidates).PublishingPermissionGranted);
                Assert.Equal("retained_publishing_unverified", Assert.Single(diagnostic.Pages).Outcome);
                Assert.Equal(HttpStatusCode.NoContent, (await Send(initiator,HttpMethod.Post,Social(workspace)+$"/sessions/{session}/select",new{externalAccountIds=new[]{"810001"}})).StatusCode);
                var saved = Assert.Single((await initiator.GetFromJsonAsync<SocialConnectionDto[]>(Social(workspace)))!);
                Assert.False(saved.PublishingPermissionGranted);
                await using var stored = Db(workspace);
                var row = await stored.SocialConnections.SingleAsync(x => x.Id == saved.Id);
                Assert.DoesNotContain("SECRET", row.ProtectedCredential);
            }
        }
        Assert.Equal(HttpStatusCode.BadRequest,(await another.GetAsync(endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await initiator.GetAsync(Social(a.Id)+$"/sessions/{session}/diagnostics")).StatusCode);
    }
    private sealed class DiscoveryMetaHandler(int count):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            string body;
            if(count==-2 && path.EndsWith("accounts")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content=new StringContent("""{"data":[],"error":{"code":190,"error_subcode":463,"message":"SECRET"}}""") });
            if(request.Method==HttpMethod.Post) body="""{"access_token":"SECRET-user-token"}""";
            else if(path.EndsWith("debug_token")) body=JsonSerializer.Serialize(new{data=new{is_valid=true,app_id="test-app",type=request.RequestUri.Query.Contains("user-token")?"USER":"PAGE",expires_at=0,scopes=new[]{"pages_show_list"},granular_scopes=new[]{
                new{scope="pages_show_list",target_ids=new[]{request.RequestUri.Query.Contains("user-token")?"810001":"999999"}},
                new{scope="pages_read_engagement",target_ids=Array.Empty<string>()},
                new{scope="unknown-secret-scope",target_ids=new[]{"PRIVATE-RESOURCE"}}
            }}});
            else if(path.EndsWith("permissions")) body="""{"data":[{"permission":"pages_show_list","status":"granted"}]}""";
            else if(path.EndsWith("accounts")) body=count==-3?"""{"data":[]}""":count<0?"""{"data":[{"id":"999","name":"Not returned to UI"}]}""":JsonSerializer.Serialize(new{data=Enumerable.Range(0,count).Select(i=>new{id=(100+i).ToString(),name="Test Page "+i,access_token="SECRET-page-token",tasks=Array.Empty<string>()})});
            else if(count==-3) body="""{"id":"810001","name":"Authorized Page","access_token":"SECRET-page-token"}""";
            else body=JsonSerializer.Serialize(new{id=path.Split('/').Last()});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
        }
    }
}
