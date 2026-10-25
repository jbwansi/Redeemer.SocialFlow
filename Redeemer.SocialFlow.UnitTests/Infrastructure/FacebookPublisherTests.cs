using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Publishing;
using Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
using Xunit;
namespace Redeemer.SocialFlow.UnitTests.Infrastructure;
public sealed class FacebookPublisherTests
{
    private static readonly byte[] Bytes=[137,80,78,71,1,2,3];
    private static IOptions<FacebookOptions> Options()=>Microsoft.Extensions.Options.Options.Create(new FacebookOptions { AppId="42",AppSecret="app-secret" });
    private static PublicationVisualSnapshot Visual()=>new(Guid.NewGuid(),Guid.NewGuid().ToString("N"),"image/png",Bytes.Length,Convert.ToHexString(SHA256.HashData(Bytes)),16,12,DateTimeOffset.UtcNow);
    private static PostPublicationRequest Request(bool image)=>new("Exact text",SocialPlatform.Facebook,"123",Guid.NewGuid(),image?Visual():null);
    private static FacebookPostPublisher Publisher(HttpClient http,Store? store=null)=>new(http,store??new Store(),new FacebookGraphClient(http,Options(),TimeProvider.System),Options(),TimeProvider.System,new Storage());
    [Fact] public async Task SendsExactBytesAndTextInOneRequestWithoutPrivatePath()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var request=Request(true);
        var result=await Publisher(http).PublishAsync(request);
        Assert.Equal(PostPublicationOutcome.Confirmed,result.Outcome);Assert.Equal(Bytes,handler.Upload);Assert.Equal("Exact text",handler.Message);
        Assert.Equal(1,handler.Writes);Assert.DoesNotContain(request.Visual!.StorageKey,handler.Body);Assert.DoesNotContain("page-secret",result.ToString());
    }
    [Theory]
    [InlineData(200,"{}",3)]
    [InlineData(200,"{\"id\":\"123_789\"}",3)]
    [InlineData(200,"{\"post_id\":\"999_789\"}",3)]
    [InlineData(400,"{\"error\":{\"code\":190}}",2)]
    [InlineData(400,"{\"error\":{\"code\":100,\"is_transient\":true}}",3)]
    [InlineData(500,"{}",3)]
    public async Task RequiresConfirmedPhotoPostIdAndClassifiesErrorsConservatively(int status,string json,int outcome)
    {
        using var handler=new Handler {Status=status,Result=json};using var http=new HttpClient(handler);
        var result=await Publisher(http).PublishAsync(Request(true));
        Assert.Equal((PostPublicationOutcome)outcome,result.Outcome);Assert.Equal(1,handler.Writes);
    }
    [Fact] public async Task NoPageTaskOrExpiredGrantPreventsExternalWrite()
    {
        using var handler=new Handler {Task="ANALYZE"};using var http=new HttpClient(handler);
        Assert.Equal(PostPublicationOutcome.Failed,(await Publisher(http).PublishAsync(Request(false))).Outcome);Assert.Equal(0,handler.Writes);
        var expired=new Store {Expiry=DateTimeOffset.UtcNow.AddMinutes(-1)};
        Assert.Equal(PostPublicationOutcome.Failed,(await Publisher(http,expired).PublishAsync(Request(false))).Outcome);Assert.Equal(0,handler.Writes);
    }
    [Fact] public async Task RouterDoesNotDispatchToWrongPlatform()
    {
        var linkedin=new FakePublisher();var facebook=new FakePublisher();var router=new PlatformPostPublisher(linkedin,facebook);
        await router.PublishAsync(Request(false));Assert.Equal(0,linkedin.Calls);Assert.Equal(1,facebook.Calls);
        await router.PublishAsync(new("text",SocialPlatform.LinkedIn,"urn:li:person:1",Guid.NewGuid()));Assert.Equal(1,linkedin.Calls);
        Assert.Equal(PostPublicationOutcome.Failed,(await router.PublishAsync(new("text",SocialPlatform.Instagram,"1",Guid.NewGuid()))).Outcome);
    }
    [Fact] public async Task OAuthExchangesGrantAndRejectsMissingPermissions()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var graph=new FacebookGraphClient(http,Options(),TimeProvider.System);
        var grant=await graph.ExchangeAsync("code",default);Assert.Equal(2,handler.Exchanges);Assert.Null(grant.SelectedPageId);Assert.Single(grant.Pages);Assert.True(grant.ExpiresAt>DateTimeOffset.UtcNow.AddDays(1));
        handler.MissingPermission=true;await Assert.ThrowsAsync<FacebookAuthorizationException>(()=>graph.ExchangeAsync("code",default));
    }
    [Fact] public async Task OAuthAcceptsIntermediateTokenWithoutExpiryButUsesFinalExpiry()
    {
        using var handler=new Handler { FirstToken="{\"access_token\":\"user-secret\"}" };
        using var http=new HttpClient(handler);
        var before=DateTimeOffset.UtcNow;
        var connection=await new FacebookGraphClient(http,Options(),TimeProvider.System).ExchangeAsync("code",default);
        Assert.Equal(2,handler.Exchanges);
        Assert.InRange(connection.ExpiresAt!.Value,before.AddSeconds(5184000),DateTimeOffset.UtcNow.AddSeconds(5184000));
    }
    [Theory]
    [InlineData("{}",null,1)]
    [InlineData("{\"access_token\":null}",null,1)]
    [InlineData("{\"access_token\":\"user-secret\",\"expires_in\":0}",null,1)]
    [InlineData(null,"{\"access_token\":\"user-secret\"}",2)]
    [InlineData(null,"{\"access_token\":\"user-secret\",\"expires_in\":0}",2)]
    public async Task OAuthRejectsMissingTokensAndUnknownFinalExpiry(string? first,string? final,int exchanges)
    {
        using var handler=new Handler { FirstToken=first, FinalToken=final };using var http=new HttpClient(handler);
        await Assert.ThrowsAsync<JsonException>(()=>new FacebookGraphClient(http,Options(),TimeProvider.System).ExchangeAsync("code",default));
        Assert.Equal(exchanges,handler.Exchanges);Assert.Equal(0,handler.PageReads);
    }
    [Theory]
    [InlineData("\"expires\":3600",3600)]
    [InlineData("\"expires_in\":3600",3600)]
    [InlineData("\"expires\":\"3600\"",3600)]
    [InlineData("\"expires_in\":\"3600\"",3600)]
    [InlineData("\"expires\":3600,\"expires_in\":1800",1800)]
    public async Task OAuthReadsExpirationAliasesAsDurations(string fields,long seconds)
    {
        using var handler=new Handler { FinalToken="{\"access_token\":\"user-secret\","+fields+"}" };
        using var http=new HttpClient(handler);var before=DateTimeOffset.UtcNow;
        var grant=await new FacebookGraphClient(http,Options(),TimeProvider.System).ExchangeAsync("code",default);
        Assert.InRange(grant.ExpiresAt!.Value,before.AddSeconds(seconds),DateTimeOffset.UtcNow.AddSeconds(seconds));
        Assert.Equal(2,handler.Exchanges);
    }
    [Theory]
    [InlineData("\"expires\":null")]
    [InlineData("\"expires\":0")]
    [InlineData("\"expires\":-1")]
    [InlineData("\"expires\":1.5")]
    [InlineData("\"expires\":true")]
    [InlineData("\"expires\":\"invalid\"")]
    [InlineData("\"expires\":\" 3600\"")]
    [InlineData("\"expires\":999999999999999999999999")]
    [InlineData("\"expires\":3600,\"expires_in\":null")]
    public async Task OAuthRejectsMalformedExpirationEvenWhenAliasIsValid(string fields)
    {
        using var handler=new Handler { FinalToken="{\"access_token\":\"user-secret\","+fields+"}" };
        using var http=new HttpClient(handler);
        await Assert.ThrowsAsync<JsonException>(()=>new FacebookGraphClient(http,Options(),TimeProvider.System).ExchangeAsync("code",default));
        Assert.Equal(0,handler.PageReads);
    }
    [Fact] public async Task PaginationNeverFollowsAnUntrustedNextUrl()
    {
        using var handler=new Handler {Paginate=true};using var http=new HttpClient(handler);
        var pages=await new FacebookGraphClient(http,Options(),TimeProvider.System).PagesAsync("user-secret",default);
        Assert.Single(pages);Assert.Equal(2,handler.PageReads);Assert.All(handler.Hosts,h=>Assert.Equal("graph.facebook.com",h));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownExpiryRequiresFreshVerificationForEveryPublication(bool failVerification)
    {
        using var handler = new Handler { DebugFailure = failVerification };
        using var http = new HttpClient(handler);
        var store = new Store { Expiry = null };
        var result = await Publisher(http, store).PublishAsync(Request(false));
        Assert.Equal(failVerification ? PostPublicationOutcome.Failed : PostPublicationOutcome.Confirmed, result.Outcome);
        Assert.Equal(failVerification ? 0 : 1, handler.Writes);
        handler.DebugFailure = true;
        Assert.Equal(PostPublicationOutcome.Failed, (await Publisher(http, store).PublishAsync(Request(false))).Outcome);
        Assert.Equal(failVerification ? 0 : 1, handler.Writes);
    }
    [Theory]
    [InlineData("{\"is_valid\":false,\"app_id\":\"42\",\"type\":\"USER\",\"expires_at\":0}")]
    [InlineData("{\"is_valid\":true,\"app_id\":\"43\",\"type\":\"USER\",\"expires_at\":0}")]
    [InlineData("{\"is_valid\":true,\"app_id\":\"42\",\"type\":\"PAGE\",\"expires_at\":0}")]
    [InlineData("{\"is_valid\":true,\"app_id\":\"42\",\"expires_at\":0}")]
    [InlineData("{\"is_valid\":true,\"app_id\":\"42\",\"type\":\"USER\"}")]
    [InlineData("{\"is_valid\":true,\"app_id\":\"42\",\"type\":\"USER\",\"expires_at\":1}")]
    [InlineData("{\"is_valid\":true,\"app_id\":\"42\",\"type\":\"USER\",\"expires_at\":0,\"data_access_expires_at\":1}")]
    public async Task UnverifiableGrantNeverSendsPublication(string metadata)
    {
        using var handler = new Handler { DebugResponse = "{\"data\":" + metadata + "}" };
        using var http = new HttpClient(handler);
        Assert.Equal(PostPublicationOutcome.Failed, (await Publisher(http, new Store { Expiry = null }).PublishAsync(Request(false))).Outcome);
        Assert.Equal(0, handler.Writes);
    }
    private sealed class Store:IFacebookConnectionStore
    {
        public DateTimeOffset? Expiry=DateTimeOffset.UtcNow.AddDays(2);
        public Task<FacebookConnection?> ReadAsync(CancellationToken token=default)=>Task.FromResult<FacebookConnection?>(new() {AppId="42",UserToken="user-secret",ExpiresAt=Expiry,Scopes=FacebookOptions.RequiredScopes,SelectedPageId="123",Pages=[new() {Id="123",Name="Page",AccessToken="page-secret",Tasks=["CREATE_CONTENT"]}]});
        public Task SaveAsync(FacebookConnection c,CancellationToken token=default)=>throw new NotSupportedException();
        public Task SelectAsync(Guid c,string p,CancellationToken token=default)=>throw new NotSupportedException();
        public Task DisconnectAsync(Guid c,CancellationToken token=default)=>throw new NotSupportedException();
    }
    private sealed class Storage:IVisualStorage
    {
        public Task<byte[]> ReadAsync(string key,CancellationToken token=default)=>Task.FromResult(Bytes.ToArray());
        public Task WriteAsync(string key,ReadOnlyMemory<byte> bytes,CancellationToken token=default)=>throw new NotSupportedException();
        public Task DeleteAsync(string key,CancellationToken token=default)=>throw new NotSupportedException();
    }
    private sealed class FakePublisher:IPostPublisher { public int Calls; public Task<PostPublicationResult> PublishAsync(PostPublicationRequest r,CancellationToken cancellationToken=default) {Calls++;return Task.FromResult(PostPublicationResult.Confirmed("1"));} }
    private sealed class Handler:HttpMessageHandler
    {
        public string? FirstToken,FinalToken,DebugResponse; public bool DebugFailure;
        public int Writes,Exchanges,PageReads,Status=200;public string Result="{\"id\":\"123_789\",\"post_id\":\"123_789\"}",Task="CREATE_CONTENT",Body="",Message="";
        public byte[]? Upload;public bool MissingPermission,Paginate;public List<string> Hosts=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token)
        {
            Hosts.Add(r.RequestUri!.Host);var path=r.RequestUri.AbsolutePath;
            if(path.EndsWith("/oauth/access_token")) {Exchanges++;return Json(200,(Exchanges==1?FirstToken:FinalToken)??"{\"access_token\":\"user-secret\",\"expires_in\":5184000}");}
            if(path.EndsWith("/debug_token")) { if (DebugFailure) throw new HttpRequestException("private-network"); return Json(200, DebugResponse ?? (Exchanges > 0 ? "{\"data\":{\"is_valid\":true,\"app_id\":\"42\",\"type\":\"USER\"}}" : "{\"data\":{\"is_valid\":true,\"app_id\":\"42\",\"type\":\"USER\",\"expires_at\":0}}")); }
            if(path.EndsWith("/permissions")) return Json(200,JsonSerializer.Serialize(new {data=(MissingPermission?new[]{"pages_show_list"}:FacebookOptions.RequiredScopes).Select(p=>new {permission=p,status="granted"})}));
            if(path.EndsWith("/accounts")) {
                PageReads++;return Json(200,JsonSerializer.Serialize(new {data=new[]{new{id="123",name="Page",access_token="page-secret",tasks=new[]{Task}}},paging=Paginate&&PageReads==1?(object)new {next="https://attacker.example/steal",cursors=new{after="cursor"}}:null}));
            }
            Writes++;Body=await r.Content!.ReadAsStringAsync(token);
            if(r.Content is MultipartFormDataContent form) foreach(var part in form) {
                var name=part.Headers.ContentDisposition!.Name!.Trim('"');
                if(name=="source") Upload=await part.ReadAsByteArrayAsync(token);
                if(name=="message") Message=await part.ReadAsStringAsync(token);
            }
            return Json(Status,Result);
        }
        private static HttpResponseMessage Json(int status,string text)=>new((HttpStatusCode)status){Content=new StringContent(text)};
    }
}
