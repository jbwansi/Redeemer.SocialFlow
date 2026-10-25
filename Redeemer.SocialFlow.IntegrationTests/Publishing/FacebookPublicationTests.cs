using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Redeemer.SocialFlow.Api.Development.Facebook;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Api.Development.Visuals;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
using SkiaSharp;
using Xunit;
namespace Redeemer.SocialFlow.IntegrationTests.Publishing;

public sealed class FacebookPublicationTests : IAsyncLifetime
{
    private const string Secret = "test-administrative-secret-for-facebook-only";
    private readonly string root = Path.Combine(Path.GetTempPath(), "facebook-test-" + Guid.NewGuid().ToString("N"));
    private readonly Clock clock = new();
    private readonly Handler handler = new();
    private readonly CapturedLogs logs = new();
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;
    private FacebookConnection connection = null!;
    private WebApplicationFactory<Program> Factory(string environment = "Development", bool enabled = true, bool local = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment(environment)
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs))
            .UseSetting("ConnectionStrings:SocialFlow", $"Data Source={Path.Combine(root,"test.db")};Pooling=False")
            .UseSetting("Facebook:Enabled", enabled.ToString()).UseSetting("Facebook:PublicationEnabled", "true")
            .UseSetting("Facebook:WorkerEnabled", "false").UseSetting("Facebook:AppId", "42").UseSetting("Facebook:AppSecret", "test-app-secret")
            .UseSetting("Facebook:StorageDirectory", Path.Combine(root,"credentials"))
            .UseSetting("LinkedIn:PublicationEnabled", "true").UseSetting("LinkedIn:WorkerEnabled", "false")
            .UseSetting("Visuals:Storage:RootPath", Path.Combine(root,"images"))
            .ConfigureServices(services => {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.RemoveAll<FacebookAdminFilter>();
                services.AddScoped<FacebookAdminFilter>(p => new(new LocalVisualAccess(enabled,false,Secret), p.GetRequiredService<IHostEnvironment>()));
                services.AddSingleton<IStartupFilter>(new Address(local));
                services.AddHttpClient<FacebookGraphClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
                services.AddHttpClient<FacebookPostPublisher>().ConfigurePrimaryHttpMessageHandler(() => handler);
                services.PostConfigure<FacebookOptions>(o => { o.WorkerEnabled = true; o.LoginConfigId = null; o.OAuthResourceDiagnosticsEnabled = false; }); // Isolate local configuration; exercise the scan without starting its timer.
            }));
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost:65474"), AllowAutoRedirect = false, HandleCookies = false });
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root); factory = Factory(); client = Client(factory); client.DefaultRequestHeaders.Add("X-SocialFlow-Visual-Admin",Secret);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().Database.MigrateAsync();
        connection = new() { AppId="42", UserToken="user-secret", ExpiresAt=clock.GetUtcNow().AddDays(30),
            Scopes=FacebookOptions.RequiredScopes, SelectedPageId="123", Pages=[new() { Id="123",Name="Page One",AccessToken="page-secret",Tasks=["CREATE_CONTENT"] }, new() { Id="456",Name="Page Two",AccessToken="other-secret",Tasks=["CREATE_CONTENT"] }] };
        await factory.Services.GetRequiredService<IFacebookConnectionStore>().SaveAsync(connection);
    }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); Directory.Delete(root,true); }
    private async Task<(Guid Post, Guid? Visual)> Seed(bool image)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>();
        var post=SocialPost.Create("Facebook title","Frozen Facebook content",SocialPlatform.Facebook, new Clock(clock.GetUtcNow().AddDays(-1)));
        db.SocialPosts.Add(post); await db.SaveChangesAsync(); Guid? visualId=null;
        if(image) {
            using var bitmap=new SKBitmap(16,12); bitmap.Erase(SKColors.Red); using var picture=SKImage.FromBitmap(bitmap); using var data=picture.Encode(SKEncodedImageFormat.Png,90); var bytes=data.ToArray();
            var visual=PostVisual.Create(post,VisualOrigin.Uploaded,"image/png",16,12,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)),null,clock.GetUtcNow());
            visual.Complete(); db.PostVisuals.Add(visual); await db.SaveChangesAsync(); post.SelectVisual(visual,clock.GetUtcNow()); visualId=visual.Id;
            await scope.ServiceProvider.GetRequiredService<IVisualStorage>().WriteAsync(visual.StorageKey,bytes);
        }
        post.SubmitForReview();post.Approve();post.Schedule(clock.GetUtcNow());await db.SaveChangesAsync();return(post.Id,visualId);
    }
    private async Task Tick() { await using var scope=factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<FacebookPublicationService>().ProcessDueAsync(default); }
    private static string Route(Guid id,string action)=>$"/api/dev/facebook/posts/{id}/{action}";
    private async Task<PublicationOperation> Operation(Guid post)
    { await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().PublicationOperations.AsNoTracking().SingleAsync(x=>x.SocialPostId==post); }
    [Theory] [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)] [InlineData(true,true)]
    public async Task ManualAndScheduledTextAndImageUseDurableSnapshotExactlyOnce(bool image,bool automatic)
    {
        var item=await Seed(image);var preview=(await client.GetFromJsonAsync<LinkedInPreview>(Route(item.Post,"preview")))!;
        if(automatic) await Tick(); else (await client.PostAsJsonAsync(Route(item.Post,"publish"),new {preview.Confirmation})).EnsureSuccessStatusCode();
        await Tick(); (await client.PostAsJsonAsync(Route(item.Post,"publish"),new {preview.Confirmation})).EnsureSuccessStatusCode();
        var operation=await Operation(item.Post);Assert.Equal(PublicationOperationState.Confirmed,operation.State);Assert.Equal("123_789",operation.ExternalId);
        Assert.Equal("Facebook",operation.Provider);Assert.Equal("123",operation.Destination);Assert.Equal(item.Visual,operation.Visual?.VisualId);
        Assert.Equal(1,handler.Publications);Assert.Contains(image?"/photos":"/feed",handler.LastPath);
        Assert.Contains("Frozen",handler.Body);Assert.Equal("page-secret",handler.Bearer);
        Assert.DoesNotContain(root,handler.Body); if(image) { Assert.Contains("source",handler.Body);Assert.DoesNotContain(operation.Visual!.StorageKey,handler.Body); }
    }
    [Theory] [InlineData(403,3)] [InlineData(500,1)] [InlineData(400,1)]
    public async Task FailedOrUncertainPhotoIsNeverRetriedOrReplacedByText(int status,int expected)
    {
        handler.Status=status;var item=await Seed(true);await Tick();await Tick();
        Assert.Equal((PublicationOperationState)expected,(await Operation(item.Post)).State);Assert.Equal(1,handler.Publications);Assert.EndsWith("/photos",handler.LastPath);
    }
    [Fact] public async Task LostAcknowledgementAndConcurrentScansCannotDuplicate()
    {
        handler.ThrowAfterDispatch=true;var item=await Seed(true);
        await Task.WhenAll(Task.Run(Tick),Task.Run(Tick));await Tick();
        Assert.Equal(1,handler.Publications);Assert.Equal(PublicationOperationState.Indeterminate,(await Operation(item.Post)).State);
    }
    [Fact] public async Task CorruptPrivateFileNeverLeavesStorage()
    {
        var item=await Seed(true);await File.WriteAllBytesAsync(Path.Combine(root,"images",item.Visual!.Value.ToString("N")),[1,2,3]);await Tick();
        Assert.Equal(0,handler.Publications);Assert.Equal(PublicationOperationState.Failed,(await Operation(item.Post)).State);
    }
    [Fact] public async Task RevokedPermissionPreventsPublishing()
    {
        handler.PermissionDenied=true;var item=await Seed(false);await Tick();
        Assert.Equal(0,handler.Publications);Assert.Equal(PublicationOperationState.Failed,(await Operation(item.Post)).State);
    }
    [Fact] public async Task ExpiredConnectionIsNotDispatched()
    {
        var item=await Seed(false);clock.Now=connection.ExpiresAt!.Value.AddMinutes(1);await Tick();
        Assert.Equal(0,handler.Publications);await using var scope=factory.Services.CreateAsyncScope();Assert.Empty(await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().PublicationOperations.ToListAsync());
        using var runtime=JsonDocument.Parse(await client.GetStringAsync("/api/dev/facebook/runtime"));Assert.Equal("expired",runtime.RootElement.GetProperty("state").GetString());
    }
    [Fact] public async Task PageSwitchInvalidatesConfirmationAndDisconnectRemovesProtectedCredentials()
    {
        var item=await Seed(false);var preview=(await client.GetFromJsonAsync<LinkedInPreview>(Route(item.Post,"preview")))!;
        var selection=await client.PostAsJsonAsync("/api/dev/facebook/page",new { ConnectionId=connection.Id,PageId="456" });selection.EnsureSuccessStatusCode();
        var stale=await client.PostAsJsonAsync(Route(item.Post,"publish"),new {preview.Confirmation});Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);Assert.Equal(0,handler.Publications);
        var invalid=await client.PostAsJsonAsync("/api/dev/facebook/page",new { ConnectionId=connection.Id,PageId="999" });Assert.Equal(HttpStatusCode.Conflict,invalid.StatusCode);
        var disk=await File.ReadAllTextAsync(Path.Combine(root,"credentials","connection.protected"));Assert.DoesNotContain("secret",disk);
        var runtime=await client.GetStringAsync("/api/dev/facebook/runtime");Assert.DoesNotContain("secret",runtime);Assert.DoesNotContain("AccessToken",runtime);
        (await client.PostAsJsonAsync("/api/dev/facebook/disconnect",new {ConnectionId=connection.Id})).EnsureSuccessStatusCode();Assert.Null(await factory.Services.GetRequiredService<IFacebookConnectionStore>().ReadAsync());
    }
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    public async Task OAuthTicketStateCookieExchangeAndReplayProtection(bool missingExpiry, bool invalidDebug, bool zeroExpiry, bool business)
    {
        handler.MissingExpiry = missingExpiry; handler.InvalidDebug = invalidDebug;
        handler.ZeroExpiry = zeroExpiry;
        if (business) factory.Services.GetRequiredService<IOptions<FacebookOptions>>().Value.LoginConfigId = "987654";
        using var start=await client.PostAsJsonAsync("/api/dev/facebook/connect",new {});start.EnsureSuccessStatusCode();
        using var startJson=JsonDocument.Parse(await start.Content.ReadAsStringAsync());var url=startJson.RootElement.GetProperty("authorizeUrl").GetString()!;
        using var publicClient=Client(factory);using var authorize=await publicClient.GetAsync(url);Assert.Equal(HttpStatusCode.Redirect,authorize.StatusCode);
        var redirect=authorize.Headers.Location!;Assert.Equal("www.facebook.com",redirect.Host);var query=QueryHelpers.ParseQuery(redirect.Query);
        if (business) { Assert.Equal("987654", query["config_id"]); Assert.False(query.ContainsKey("scope")); }
        else { Assert.False(query.ContainsKey("config_id")); Assert.Equal(string.Join(',',FacebookOptions.RequiredScopes),query["scope"]); }
        Assert.Equal("code", query["response_type"]);
        var cookie=authorize.Headers.GetValues("Set-Cookie").Single();Assert.Contains("secure",cookie,StringComparison.OrdinalIgnoreCase);Assert.Contains("httponly",cookie,StringComparison.OrdinalIgnoreCase);
        publicClient.DefaultRequestHeaders.Add("Cookie",cookie.Split(';')[0]);
        var callback="/api/dev/facebook/callback?state="+query["state"]+"&code=test-code";
        using var response=await publicClient.GetAsync(callback);
        Assert.Equal(invalidDebug ? HttpStatusCode.BadGateway : HttpStatusCode.Redirect,response.StatusCode);
        var stored=await factory.Services.GetRequiredService<IFacebookConnectionStore>().ReadAsync();
        if (invalidDebug)
        {
            Assert.Contains(logs.Messages, text => text.Contains("Stage=debug_token; Category=invalid_response; ExceptionType=JsonException"));
            Assert.Contains(logs.Messages, text => text.Contains("Stage=meta_oauth; Category=invalid_response; ExceptionType=JsonException"));
            Assert.Equal(connection.Id, stored!.Id); // failed validation cannot replace the protected connection
            Assert.DoesNotContain("user-secret", await response.Content.ReadAsStringAsync());
        }
        else
        {
            Assert.Contains(logs.Messages, text => text.Contains("OAuthCompleted=true; PagesCount=2"));
            Assert.Contains(logs.Messages, text => text.Contains("ConnectionPersisted=true; PagesCount=2"));
            Assert.DoesNotContain("code",response.Headers.Location!.ToString()); Assert.Null(stored!.SelectedPageId);
            if (zeroExpiry)
            {
                Assert.Null(stored.ExpiresAt);
                var restartedStore = new ProtectedFacebookConnectionStore(Options.Create(new FacebookOptions { AppId = "42", StorageDirectory = Path.Combine(root, "credentials") }));
                Assert.Null((await restartedStore.ReadAsync())!.ExpiresAt);
            }
            else Assert.Equal(clock.GetUtcNow().AddSeconds(missingExpiry ? 3600 : 5184000), stored.ExpiresAt);
        }
        Assert.Equal(2,handler.Exchanges);
        Assert.Equal(missingExpiry ? 1 : 0, handler.DebugCalls);
        Assert.Equal(0, handler.Publications);
        foreach (var secret in new[] { "test-app-secret", "user-secret", "page-secret", "test-code", "input_token" })
            Assert.DoesNotContain(logs.Messages, text => text.Contains(secret, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, text => text.StartsWith("System.Net.Http.HttpClient.FacebookGraphClient", StringComparison.Ordinal));
        using var replay=await publicClient.GetAsync(callback);Assert.Equal(HttpStatusCode.Unauthorized,replay.StatusCode);Assert.Equal(2,handler.Exchanges);
        using var reused=await publicClient.GetAsync(url);Assert.Equal(HttpStatusCode.Unauthorized,reused.StatusCode);
    }
    [Fact] public async Task CallbackWithoutBrowserBindingAndDirectAuthorizeAreRejected()
    {
        using var anonymous=Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/dev/facebook/authorize")).StatusCode);
        var flow=factory.Services.GetRequiredService<FacebookFlows>().States.Create();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/dev/facebook/callback?state="+flow.State+"&code=x")).StatusCode);Assert.Equal(0,handler.Exchanges);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task RefusedOrExpiredOAuthStateCannotBeReused(bool expired)
    {
        var flow=factory.Services.GetRequiredService<FacebookFlows>().States.Create();
        using var browser=Client(factory);browser.DefaultRequestHeaders.Add("Cookie","__Host-SocialFlow.Facebook.State="+flow.Browser);
        if(expired) clock.Now=clock.Now.AddMinutes(11);
        var path="/api/dev/facebook/callback?state="+flow.State+"&error=access_denied";
        Assert.Equal(expired?HttpStatusCode.Unauthorized:HttpStatusCode.BadRequest,(await browser.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await browser.GetAsync(path)).StatusCode);Assert.Equal(0,handler.Exchanges);
    }
    [Fact]
    public async Task UnknownExpiryPersistsDataDeadlineAndRuntimeFailsClosedWhenRevalidationFails()
    {
        var unknown = new FacebookConnection { AppId = connection.AppId, UserToken = connection.UserToken,
            ExpiresAt = null, DataAccessExpiresAt = clock.GetUtcNow().AddMinutes(30), Scopes = connection.Scopes,
            Pages = connection.Pages, SelectedPageId = connection.SelectedPageId };
        await factory.Services.GetRequiredService<IFacebookConnectionStore>().SaveAsync(unknown);
        var restarted = new ProtectedFacebookConnectionStore(Options.Create(new FacebookOptions { AppId = "42", StorageDirectory = Path.Combine(root, "credentials") }));
        var read = await restarted.ReadAsync();
        Assert.Null(read!.ExpiresAt);
        Assert.Equal(unknown.DataAccessExpiresAt, read.DataAccessExpiresAt);
        handler.ZeroExpiry = true;
        using (var runtime = JsonDocument.Parse(await client.GetStringAsync("/api/dev/facebook/runtime")))
        {
            Assert.True(runtime.RootElement.GetProperty("connected").GetBoolean());
            Assert.True(runtime.RootElement.GetProperty("revalidationRequired").GetBoolean());
            Assert.False(runtime.RootElement.GetProperty("expirationKnown").GetBoolean());
        }
        handler.InvalidDebug = true;
        using (var runtime = JsonDocument.Parse(await client.GetStringAsync("/api/dev/facebook/runtime")))
            Assert.False(runtime.RootElement.GetProperty("connected").GetBoolean());
        var item = await Seed(false);
        await Tick();
        Assert.Equal(0, handler.Publications);
        handler.InvalidDebug = false;
        clock.Now = unknown.DataAccessExpiresAt.Value;
        using var expired = JsonDocument.Parse(await client.GetStringAsync("/api/dev/facebook/runtime"));
        Assert.Equal("expired", expired.RootElement.GetProperty("state").GetString());
    }
    [Theory]
    [InlineData(true, "no-pages", 0)]
    [InlineData(false, "select-page", 2)]
    public async Task RuntimeDistinguishesPersistedAccountFromPageReadiness(bool empty, string state, int count)
    {
        var saved = new FacebookConnection { AppId = connection.AppId, UserToken = connection.UserToken,
            ExpiresAt = connection.ExpiresAt, Scopes = connection.Scopes, Pages = empty ? [] : connection.Pages };
        await factory.Services.GetRequiredService<IFacebookConnectionStore>().SaveAsync(saved);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/dev/facebook/runtime"));
        Assert.Equal(state, json.RootElement.GetProperty("state").GetString());
        Assert.False(json.RootElement.GetProperty("connected").GetBoolean());
        Assert.Equal(saved.Id, json.RootElement.GetProperty("connectionId").GetGuid());
        Assert.Equal(count, json.RootElement.GetProperty("pages").GetArrayLength());
        Assert.Contains(logs.Messages, text => text.Contains("Stage=runtime; ConnectionPersisted=True; PagesCount=" + count));
    }
    private static IEnumerable<HttpRequestMessage> SensitiveRoutes()
    {
        yield return new(HttpMethod.Get,"/api/dev/facebook/runtime");
        foreach(var path in new[]{"connect","page","disconnect"}) yield return new(HttpMethod.Post,"/api/dev/facebook/"+path);
        var id=Guid.NewGuid();yield return new(HttpMethod.Get,Route(id,"publication"));yield return new(HttpMethod.Get,Route(id,"preview"));yield return new(HttpMethod.Post,Route(id,"publish"));
    }
    [Theory] [InlineData("Production",true)] [InlineData("Staging",true)] [InlineData("Development",false)]
    public async Task DisabledOutsideExplicitDevelopment(string environment,bool enabled)
    {
        await using var alternate=Factory(environment,enabled);using var other=Client(alternate);
        foreach(var request in SensitiveRoutes()) { using(request) using(var response=await other.SendAsync(request)) Assert.Equal(HttpStatusCode.NotFound,response.StatusCode); }
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync("/api/dev/facebook/authorize")).StatusCode);
    }
    [Fact] public async Task AllAdminRoutesRejectMissingSecretBeforeBinding()
    {
        using var anonymous=Client(factory);foreach(var request in SensitiveRoutes()) { using(request) using(var response=await anonymous.SendAsync(request)) Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode); }
    }
    [Theory] [InlineData("host")] [InlineData("forwarded")] [InlineData("remote")]
    public async Task NonLocalAdminAndOAuthRequestsAreDenied(string scenario)
    {
        await using var alternate=Factory(local:scenario!="remote");using var other=Client(alternate);other.DefaultRequestHeaders.Add("X-SocialFlow-Visual-Admin",Secret);
        if(scenario=="host") other.DefaultRequestHeaders.Host="example.com";if(scenario=="forwarded") other.DefaultRequestHeaders.Add("X-Forwarded-For","203.0.113.2");
        foreach(var request in SensitiveRoutes()) { using(request) using(var response=await other.SendAsync(request)) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); }
        Assert.Equal(HttpStatusCode.Forbidden,(await other.GetAsync("/api/dev/facebook/authorize")).StatusCode);
    }
    private sealed class Clock(DateTimeOffset? now=null):TimeProvider { public DateTimeOffset Now=now??new DateTimeOffset(2030,1,1,0,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class Address(bool local):IStartupFilter { public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)=>app=> { app.Use((ctx,run)=> {ctx.Connection.RemoteIpAddress=IPAddress.Parse(local?"127.0.0.1":"203.0.113.2");return run();});next(app); }; }
    private sealed class Handler:HttpMessageHandler
    {
        public bool MissingExpiry, InvalidDebug, ZeroExpiry;
        public int DebugCalls;
        public int Publications,Exchanges;public int Status=200;public bool PermissionDenied,ThrowAfterDispatch;public string LastPath="",Body="",Bearer="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Equal("graph.facebook.com",request.RequestUri!.Host);Assert.Equal("https",request.RequestUri.Scheme);Assert.StartsWith("/v26.0/",request.RequestUri.AbsolutePath);
            var path=request.RequestUri.AbsolutePath;
            if(path.EndsWith("/oauth/access_token")) { Exchanges++;return Json(200, MissingExpiry && Exchanges == 2 ? "{\"access_token\":\"user-secret\"}" : "{\"access_token\":\"user-secret\",\"expires_in\":5184000}"); }
            if(path.EndsWith("/debug_token")) { DebugCalls++; return Json(200, JsonSerializer.Serialize(new { data = new { type = "USER", is_valid = !InvalidDebug, app_id = "42", expires_at = ZeroExpiry ? 0L : 1893459600L } })); }
            if(path.EndsWith("/permissions")) return PermissionDenied?Json(400,"{\"error\":{\"code\":190}}") : Json(200,JsonSerializer.Serialize(new {data=FacebookOptions.RequiredScopes.Select(p=>new {permission=p,status="granted"})}));
            if(path.EndsWith("/accounts")) return Json(200,"{\"data\":[{\"id\":\"123\",\"name\":\"Page One\",\"access_token\":\"page-secret\",\"tasks\":[\"CREATE_CONTENT\"]},{\"id\":\"456\",\"name\":\"Page Two\",\"access_token\":\"other-secret\",\"tasks\":[\"CREATE_CONTENT\"]}]}");
            Assert.Equal(HttpMethod.Post,request.Method);Interlocked.Increment(ref Publications);LastPath=path;Body=await request.Content!.ReadAsStringAsync(token);Bearer=request.Headers.Authorization!.Parameter!;
            if(ThrowAfterDispatch) throw new HttpRequestException("simulated private provider response");
            return Json(Status,Status==200?"{\"id\":\"123_789\",\"post_id\":\"123_789\"}":"{\"error\":{\"code\":2,\"is_transient\":true}}");
        }
        private static HttpResponseMessage Json(int status,string value)=>new((HttpStatusCode)status){Content=new StringContent(value,System.Text.Encoding.UTF8,"application/json")};
    }
    private sealed class CapturedLogs : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(categoryName, Messages);
        public void Dispose() { }
        private sealed class Capture(string category, System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(category + ": " + formatter(state, exception) + exception);
        }
    }
}
