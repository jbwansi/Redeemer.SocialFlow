using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Infrastructure.Social;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests;

public sealed class FacebookDiscoveryDiagnosticsTests
{
    [Theory]
    [InlineData(null,"missing","missing",0)]
    [InlineData("null","invalid","invalid",0)]
    [InlineData("[]","array","missing",0)]
    [InlineData("[],\"granular_scopes\":[]","ambiguous","ambiguous",0)]
    [InlineData("[{}]","array","ambiguous",0)]
    [InlineData("[{\"scope\":\"pages_show_list\"}]","array","missing",0)]
    [InlineData("[{\"scope\":\"pages_show_list\",\"target_ids\":[]}]","array","empty_array",0)]
    [InlineData("[{\"scope\":\"pages_show_list\",\"target_ids\":[\"810001\",810001,\"810002\"]}]","array","array",2)]
    [InlineData("[{\"scope\":\"pages_show_list\",\"target_ids\":[\"SECRET\"]}]","array","invalid",0)]
    [InlineData("[{\"scope\":\"pages_show_list\",\"target_ids\":[1e3]}]","array","invalid",0)]
    [InlineData("[{\"scope\":\"pages_show_list\",\"target_ids\":null}]","array","invalid",0)]
    [InlineData("[{\"scope\":\"pages_show_list\",\"target_ids\":[],\"target_ids\":[\"810001\"]}]","array","ambiguous",0)]
    [InlineData("[{\"scope\":\"pages_show_list\"},{\"scope\":\"pages_show_list\"}]","array","ambiguous",0)]
    [InlineData("[{\"scope\":\"SECRET\",\"target_ids\":[\"810001\"]}]","array","missing",0)]
    public async Task GranularTargetsAreSafeObservationsAndNeverChangeDiscovery(string? granular,string shape,string status,int count)
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["SocialConnections:Providers:Facebook:ClientId"]="app-id",["SocialConnections:Providers:Facebook:ClientSecret"]="SECRET",
            ["SocialConnections:Providers:Facebook:ApiVersion"]="v26.0"
        }).Build();
        using var provider=new FacebookWorkspaceOAuthProvider(new(new Handler(request=> {
            var path=request.RequestUri!.AbsolutePath;
            if(request.Method==HttpMethod.Post) return Ok("""{"access_token":"SECRET-token"}""");
            if(path.EndsWith("debug_token")) return Ok("{\"data\":{\"is_valid\":true,\"app_id\":\"app-id\",\"type\":\"USER\",\"expires_at\":0" +
                (granular is null?"":",\"granular_scopes\":"+granular)+"}}");
            if(path.EndsWith("permissions")) return Ok("""{"data":[{"permission":"pages_show_list","status":"granted"}]}""");
            return Ok("""{"data":[]}""");
        })),config,TimeProvider.System);
        var result=await provider.DiscoverWithDiagnosticsAsync("SECRET-code",default);
        Assert.True(result.Succeeded); Assert.Equal("no_pages",result.Diagnostic.Outcome);
        var observed=result.Diagnostic.Facebook!.Granular!;
        Assert.Equal(shape,observed.Shape); Assert.Equal(3,observed.Permissions.Count);
        var list=Assert.Single(observed.Permissions,x=>x.Permission=="pages_show_list");
        Assert.Equal(status,list.TargetsStatus); Assert.Equal(count,list.TargetIds.Count);
        if(count>0) Assert.Equal(new[]{"810001","810002"},list.TargetIds);
        Assert.DoesNotContain("SECRET",JsonSerializer.Serialize(result.Diagnostic));
    }
    [Theory]
    [InlineData("USER", true)]
    [InlineData("SYSTEM_USER", true)]
    [InlineData("USER", false)]
    [InlineData("SECRET unexpected type", true)]
    public async Task EffectiveConfigurationAndTokenInspectionAreSafe(string type, bool matchingApp)
    {
        const string prefix="SocialConnections:Providers:Facebook:";
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Authentication:PublicOrigin"]="https://localhost:65474", [prefix+"ClientId"]="1145889561364859",
            [prefix+"ClientSecret"]="SECRET", [prefix+"LoginConfigId"]="2140120956587865",
            [prefix+"ApiVersion"]="v26.0", [prefix+"UseLegacyClientCredentials"]="true",
            ["Facebook:AppId"]="999", ["Facebook:LoginConfigId"]="888"
        }).Build();
        using var provider=new FacebookWorkspaceOAuthProvider(new(new Handler(request=> {
            var path=request.RequestUri!.AbsolutePath;
            if(request.Method==HttpMethod.Post) return Ok("""{"access_token":"SECRET-token"}""");
            if(path.EndsWith("debug_token")) return Ok(JsonSerializer.Serialize(new{data=new{is_valid=true,app_id=matchingApp?"1145889561364859":"999",type,expires_at=0}}));
            if(path.EndsWith("permissions")) return Ok("""{"data":[{"permission":"pages_show_list","status":"granted"},{"permission":"pages_manage_posts","status":"declined"}]}""");
            return Ok("""{"data":[]}""");
        })),config,TimeProvider.System);
        var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(provider.AuthorizationUrl("state",null)).Query);
        Assert.Equal("1145889561364859",query["client_id"]); Assert.Equal("2140120956587865",query["config_id"]);
        Assert.Equal("https://localhost:65474/api/social-oauth/1/callback",query["redirect_uri"]); Assert.False(query.ContainsKey("scope"));
        var result=await provider.DiscoverWithDiagnosticsAsync("SECRET-code",default);
        var evidence=result.Diagnostic.Facebook!;
        Assert.Equal("1145889561364859",evidence.ClientId); Assert.Equal("2140120956587865",evidence.LoginConfigId);
        Assert.Equal(type.StartsWith("SECRET")?"unknown":type,evidence.TokenType); Assert.Equal(matchingApp,evidence.AppIdMatches);
        Assert.Equal(type=="USER" && matchingApp,result.Succeeded);
        if(result.Succeeded) { Assert.True(evidence.PagesShowList); Assert.False(evidence.PagesReadEngagement); Assert.False(evidence.PagesManagePosts); }
        else { Assert.Null(evidence.PagesShowList); Assert.Null(evidence.Granular); Assert.Equal(0,result.Diagnostic.Requests); }
        Assert.DoesNotContain("SECRET",JsonSerializer.Serialize(result.Diagnostic));
    }
    [Theory]
    [InlineData("empty", "no_pages", 0, 0, true)]
    [InlineData("graph-empty", "graph_error", 0, 0, false)]
    [InlineData("graph-only", "graph_error", 0, 0, false)]
    [InlineData("graph-400", "http_error", 0, 0, false)]
    [InlineData("missing-permission", "missing_discovery_permission", 0, 0, false)]
    [InlineData("filtered", "pages_filtered", 2, 0, true)]
    [InlineData("no-publish", "pages_without_publishing", 1, 1, true)]
    [InlineData("http", "http_error", 0, 0, false)]
    [InlineData("http-malformed", "http_error", 0, 0, false)]
    [InlineData("invalid-json", "invalid_response", 0, 0, false)]
    [InlineData("invalid-shape", "invalid_response", 0, 0, false)]
    [InlineData("pagination", "pages_available", 3, 2, true)]
    [InlineData("cursor-missing", "invalid_response", 1, 1, false)]
    [InlineData("cursor-repeat", "invalid_response", 2, 1, false)]
    [InlineData("limit", "pagination_limit", 5, 1, false)]
    [InlineData("network", "network_error", 0, 0, false)]
    public async Task ReportsSafeCountsAndFailureReasons_WithoutLeakingResponses(string scenario,string outcome,int received,int retained,bool success)
    {
        var calls=0;
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["SocialConnections:Providers:Facebook:ClientId"]="app-id",["SocialConnections:Providers:Facebook:ClientSecret"]="app-secret",
            ["SocialConnections:Providers:Facebook:ApiVersion"]="v26.0"
        }).Build();
        using var provider=new FacebookWorkspaceOAuthProvider(new(new Handler(request=> {
            Assert.Equal("graph.facebook.com",request.RequestUri!.Host);
            var path=request.RequestUri.AbsolutePath;
            if(request.Method==HttpMethod.Post) return Ok("""{"access_token":"user-token"}""");
            if(path.EndsWith("debug_token")) return Ok(JsonSerializer.Serialize(new { data=new {
                is_valid=true, app_id="app-id", type=request.RequestUri.Query.Contains("user-token")?"USER":"PAGE", expires_at=0,
                scopes=scenario=="no-publish"?new[]{"pages_show_list"}:new[]{"pages_show_list","pages_read_engagement","pages_manage_posts"}
            }}));
            if(path.EndsWith("permissions")) return Ok(JsonSerializer.Serialize(new {data=(scenario=="missing-permission"?Array.Empty<string>():new[]{"pages_show_list","pages_read_engagement","pages_manage_posts"}).Select(permission=>new{permission,status="granted"})}));
            if(!path.EndsWith("accounts")) return Ok(JsonSerializer.Serialize(new{id=path.Split('/').Last()}));
            calls++;
            if(scenario.StartsWith("graph-")) return new(scenario=="graph-400" ? HttpStatusCode.BadRequest : HttpStatusCode.OK) {
                Content=new StringContent(scenario=="graph-only" ? """{"error":{"code":190,"error_subcode":463,"message":"SECRET"}}""" : """{"data":[],"error":{"code":190,"error_subcode":463,"message":"SECRET"}}""") };
            if(scenario=="network") throw new HttpRequestException("SECRET raw network message");
            if(scenario=="http") return new(HttpStatusCode.Forbidden){Content=new StringContent("SECRET raw body")};
            if(scenario=="http-malformed") return new(HttpStatusCode.BadRequest){Content=new StringContent("""{"error":{"code":"SECRET invalid error code"}}""")};
            if(scenario=="invalid-json") return Ok("SECRET non-json response");
            if(scenario=="invalid-shape") return Ok("""{"data":{},"secret":"RAW BODY"}""");
            if(scenario=="empty") return Ok("""{"data":[]}""");
            if(scenario=="filtered") return Ok("""{"data":[{"id":"1234","name":"SECRET display name"},{"access_token":"page-token"}]}""");
            var id=scenario=="pagination" && calls==2?"two":"one";
            var page=new {id,name="SECRET display name",access_token="page-token",tasks=new[]{"CREATE_CONTENT"}};
            if(scenario=="pagination" && calls==2) {
                Assert.Contains("after=cursor-1",request.RequestUri.Query);
                return Ok(JsonSerializer.Serialize(new{data=new[]{page,page}}));
            }
            if(scenario=="cursor-missing") return Ok(JsonSerializer.Serialize(new{data=new[]{page},paging=new{next="https://untrusted.example/SECRET"}}));
            if(scenario is "pagination" or "cursor-repeat" or "limit") return Ok(JsonSerializer.Serialize(new{data=new[]{page},paging=new{next="https://untrusted.example/SECRET",cursors=new{after=scenario=="limit"?"cursor-"+calls:"cursor-1"}}}));
            return Ok(JsonSerializer.Serialize(new{data=new[]{page}}));
        })),config,TimeProvider.System);
        var result=await provider.DiscoverWithDiagnosticsAsync("SECRET code",default);
        Assert.Equal(outcome,result.Diagnostic.Outcome); Assert.Equal(success,result.Succeeded);
        Assert.Equal(received,result.Diagnostic.PagesReceived); Assert.Equal(retained,result.Diagnostic.PagesRetained);
        Assert.Equal(calls,result.Diagnostic.Requests); Assert.Equal(success,result.Diagnostic.Complete);
        if(!success) Assert.Empty(result.Candidates);
        var evidence=Assert.IsType<Redeemer.SocialFlow.Application.Accounts.FacebookDiscoveryEvidence>(result.Diagnostic.Facebook);
        Assert.Equal("USER",evidence.TokenType); Assert.True(evidence.AppIdMatches);
        Assert.Equal(scenario!="missing-permission",evidence.PagesShowList);
        Assert.Equal(scenario!="missing-permission",evidence.PagesReadEngagement);
        Assert.Equal(scenario!="missing-permission",evidence.PagesManagePosts);
        if(scenario=="empty" || scenario.StartsWith("graph-")) {
            var response=Assert.Single(evidence.Responses!,x=>x.Stage=="pages");
            Assert.Equal(scenario=="graph-400"?400:200,response.HttpStatus);
            Assert.Equal(scenario=="graph-only"?"missing":"empty_array",response.DataShape);
            Assert.Equal(scenario!="empty",response.GraphError);
            if(response.GraphError) { Assert.Equal(190,response.GraphCode); Assert.Equal(463,response.GraphSubcode); }
        }
        if(scenario=="no-publish") { Assert.False(Assert.Single(result.Candidates).PublishingPermissionGranted); Assert.Equal(1,result.Diagnostic.PagesWithoutPublishing); }
        if(scenario=="filtered") {
            Assert.Equal(1,result.Diagnostic.Exclusions["missing_page_token"]); Assert.Equal(1,result.Diagnostic.Exclusions["missing_page_id"]);
            Assert.Equal("1234",result.Diagnostic.Pages![0].ExternalPageId);
            Assert.Equal("missing_page_token",result.Diagnostic.Pages[0].Outcome);
            Assert.Null(result.Diagnostic.Pages[1].ExternalPageId);
        }
        if(scenario=="pagination") Assert.Equal(1,result.Diagnostic.Exclusions["duplicate_page_id"]);
        if(scenario=="http") Assert.Equal(403,result.Diagnostic.HttpStatus);
        if(scenario=="http-malformed") Assert.Equal(400,result.Diagnostic.HttpStatus);
        var safe=JsonSerializer.Serialize(result.Diagnostic);
        foreach(var forbidden in new[]{"SECRET","page-token","user-token","app-secret","RAW BODY","untrusted.example"}) Assert.DoesNotContain(forbidden,safe);
    }
    private static HttpResponseMessage Ok(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body)};
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(respond(request)); }
}
