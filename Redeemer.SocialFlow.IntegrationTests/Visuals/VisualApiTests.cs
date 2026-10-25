using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Api.Development.Visuals;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using SkiaSharp;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Visuals;

public sealed class VisualApiTests : IAsyncLifetime
{
    private const string Secret = "test-only-administrative-secret-0123456789";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "visual-api-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly Generator _generator = new();

    private WebApplicationFactory<Program> Factory(string environment = "Development", bool enabled = true,
        string? secret = Secret, bool local = true, bool allowHttp = false) =>
        new Redeemer.SocialFlow.IntegrationTests.BusinessApiFactory().WithWebHostBuilder(builder => builder
            .UseEnvironment(environment)
            .UseSetting("LinkedIn:WorkerEnabled", "false")
            .UseSetting("LinkedIn:PublicationEnabled", "false")
            .UseSetting("Visuals:Enabled", enabled.ToString())
            .UseSetting("Visuals:Storage:RootPath", Path.Combine(_root, "files"))
            .UseSetting("ConnectionStrings:SocialFlow", $"Data Source={Path.Combine(_root,"db.sqlite")};Pooling=False")
            .ConfigureServices(services =>
            {
                services.RemoveAll<LocalVisualAccess>();
                services.AddSingleton(new LocalVisualAccess(enabled, allowHttp, secret));
                services.RemoveAll<IVisualGenerator>();
                services.AddSingleton<IVisualGenerator>(_generator);
                services.AddSingleton<IStartupFilter>(new AddressFilter(local));
            }));

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _factory = Factory();
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>().Database.MigrateAsync();
        _client = Client(_factory);
        _client.DefaultRequestHeaders.Add("X-SocialFlow-Visual-Admin", Secret);
    }
    private static HttpClient Client(WebApplicationFactory<Program> factory, string scheme = "https") =>
        factory.CreateClient(new() { BaseAddress = new Uri(scheme + "://localhost"), AllowAutoRedirect = false });
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        Directory.Delete(_root,true);
    }
    private async Task<Guid> Post()
    {
        var response = await _client.PostAsJsonAsync("/api/posts", new CreatePostRequest("Title","Content",SocialPlatform.LinkedIn));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SocialPostDto>())!.Id;
    }
    private static string Route(Guid post) => $"/api/posts/{post}/visuals";
    private static MultipartFormDataContent UploadBody()
    {
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(Image());
        image.Headers.ContentType = new("text/plain"); // Deliberately false; the decoder determines MIME.
        form.Add(image,"file","../../untrusted-name.jpg");
        return form;
    }

    [Fact]
    public async Task ImportListPreviewSelectDeleteAndGenerateWorkThroughProtectedRoutes()
    {
        var post = await Post();
        using var body = UploadBody();
        using var upload = await _client.PostAsync(Route(post)+"/upload", body);
        Assert.Equal(HttpStatusCode.Created,upload.StatusCode);
        var visual = (await upload.Content.ReadFromJsonAsync<VisualDto>())!;
        Assert.Equal("image/png",visual.ContentType);
        using var preview = await _client.GetAsync(upload.Headers.Location);
        Assert.Equal(HttpStatusCode.OK,preview.StatusCode);
        Assert.Equal(Image(),await preview.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png",preview.Content.Headers.ContentType!.MediaType);
        Assert.True(preview.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff",preview.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Single((await _client.GetFromJsonAsync<VisualDto[]>(Route(post)))!);
        using var select = await _client.PostAsync(Route(post)+$"/{visual.Id}/select",null);
        Assert.True((await select.Content.ReadFromJsonAsync<VisualDto>())!.Selected);
        using var deniedDelete = await _client.DeleteAsync(Route(post)+$"/{visual.Id}");
        Assert.Equal(HttpStatusCode.Conflict,deniedDelete.StatusCode);
        using var generation = await _client.PostAsJsonAsync(Route(post)+"/generate",
            new { IdentityId="brand",Name="Brand",Style="Simple",Colors=new[]{"#123456"},VisualBrief="Garden" });
        Assert.Equal(HttpStatusCode.Created,generation.StatusCode);
        var candidate = (await generation.Content.ReadFromJsonAsync<VisualDto>())!;
        Assert.Equal(1,_generator.Calls);
        using var deletion = await _client.DeleteAsync(Route(post)+$"/{candidate.Id}");
        Assert.Equal(HttpStatusCode.NoContent,deletion.StatusCode);
        using var deleted = await _client.GetAsync(Route(post)+$"/{candidate.Id}");
        Assert.Equal(HttpStatusCode.NotFound,deleted.StatusCode);
        Assert.True(File.Exists(Path.Combine(_root,"files",candidate.Id.ToString("N"))));
        using var staticFile = await _client.GetAsync("/files/"+visual.Id.ToString("N"));
        Assert.Equal(HttpStatusCode.NotFound,staticFile.StatusCode);
    }

    [Fact]
    public async Task MissingFileAndCorruptContentAreRejectedWithoutSaving()
    {
        var post = await Post();
        using var empty = new MultipartFormDataContent();
        using var missing = await _client.PostAsync(Route(post) + "/upload", empty);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var invalid = new MultipartFormDataContent();
        invalid.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "fake.png");
        using var corrupt = await _client.PostAsync(Route(post) + "/upload", invalid);
        Assert.Equal(HttpStatusCode.BadRequest, corrupt.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<VisualDto[]>(Route(post)))!);
        Assert.Equal(0, _generator.Calls);
    }

    private static IEnumerable<HttpRequestMessage> Requests(Guid post)
    {
        var route=Route(post); var id=Guid.NewGuid();
        yield return new(HttpMethod.Get,"/api/visual-identities");
        yield return new(HttpMethod.Post,"/api/visual-identities");
        yield return new(HttpMethod.Put,"/api/visual-identities/"+id);
        yield return new(HttpMethod.Delete,"/api/visual-identities/"+id);
        yield return new(HttpMethod.Post,"/api/visual-identities/"+id+"/default");
        yield return new(HttpMethod.Post,"/api/visual-identities/"+id+"/logo");
        yield return new(HttpMethod.Get,"/api/visual-identities/"+id+"/logos/"+id);
        yield return new(HttpMethod.Get,route);
        yield return new(HttpMethod.Get,route+"/"+id);
        yield return new(HttpMethod.Post,route+"/upload");
        yield return new(HttpMethod.Post,route+"/generate");
        yield return new(HttpMethod.Post,route+$"/{id}/select");
        yield return new(HttpMethod.Delete,route+"/"+id);
    }

    [Theory]
    [InlineData("Production",true)]
    [InlineData("Staging",true)]
    [InlineData("Development",false)]
    public async Task RoutesAreAbsentOutsideDevelopmentOrWithoutOptIn(string environment,bool enabled)
    {
        await using var factory=Factory(environment,enabled);
        using var client=Client(factory);
        client.DefaultRequestHeaders.Add("X-SocialFlow-Visual-Admin",Secret);
        foreach(var request in Requests(Guid.NewGuid()))
        {
            using(request) using(var response=await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-secret")]
    public async Task EveryRouteRejectsMissingOrWrongHeaderBeforeBinding(string? header)
    {
        using var client=Client(_factory);
        if(header is not null) client.DefaultRequestHeaders.Add("X-SocialFlow-Visual-Admin",header);
        foreach(var request in Requests(Guid.NewGuid()))
        {
            using(request) using(var response=await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
        }
        Assert.Equal(0,_generator.Calls);
    }

    [Fact]
    public async Task QueryStringSecretIsNotAuthentication()
    {
        using var client=Client(_factory);
        using var response=await client.GetAsync(Route(Guid.NewGuid())+"?X-SocialFlow-Visual-Admin="+Secret);
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    [Fact]
    public async Task AbsentServerSecretFailsClosed()
    {
        await using var factory=Factory(secret:null);
        using var client=Client(factory);
        using var response=await client.GetAsync(Route(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
    }

    [Theory]
    [InlineData("remote")]
    [InlineData("host")]
    [InlineData("forwarded")]
    [InlineData("http")]
    public async Task NonLocalOrInsecureRequestsAreDenied(string scenario)
    {
        await using var factory=Factory(local:scenario!="remote");
        using var client=Client(factory,scenario=="http"?"http":"https");
        client.DefaultRequestHeaders.Add("X-SocialFlow-Visual-Admin",Secret);
        if(scenario=="host") client.DefaultRequestHeaders.Host="example.com";
        if(scenario=="forwarded") client.DefaultRequestHeaders.Add("X-Forwarded-For","203.0.113.10");
        foreach (var request in Requests(Guid.NewGuid()))
        {
            using (request) using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task HttpRequiresExplicitLoopbackOptIn()
    {
        await using var factory=Factory(allowHttp:true);
        using var client=Client(factory,"http");
        client.DefaultRequestHeaders.Add("X-SocialFlow-Visual-Admin",Secret);
        using var response=await client.GetAsync(Route(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound,response.StatusCode); // Authorized, post does not exist.
    }

    [Fact]
    public async Task GeneratorFailureIsSanitized()
    {
        var post=await Post();
        _generator.Fail=true;
        using var response=await _client.PostAsJsonAsync(Route(post)+"/generate",
            new {IdentityId="brand",Name="Brand",Style="Simple",Colors=new[]{"#123456"},VisualBrief="Garden"});
        Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode);
        Assert.DoesNotContain("secret",await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void SecretMustBeLongAndDistinctFromBothOpenAiKeys()
    {
        Assert.False(new LocalVisualAccess(true,false,"short").Configured);
        Assert.False(new LocalVisualAccess(true,false,Secret,Secret).Configured);
        Assert.False(new LocalVisualAccess(true,false,Secret,null,Secret).Configured);
        var access=new LocalVisualAccess(true,false,Secret,"different");
        Assert.True(access.Matches(Secret));
        Assert.False(access.Matches(Secret+"x"));
    }

    [Fact]
    public async Task IdentityLibraryRoutesUploadLogoAndRecordEffectiveGenerationWithoutExposingStorage()
    {
        var list = (await _client.GetFromJsonAsync<LibraryIdentityDto[]>("/api/visual-identities"))!;
        var identity = Assert.Single(list);
        using var body = UploadBody(); body.Add(new StringContent(identity.Revision.ToString()), "revision");
        using var upload = await _client.PostAsync($"/api/visual-identities/{identity.Id}/logo", body);
        upload.EnsureSuccessStatusCode();
        Assert.DoesNotContain("storageKey", await upload.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        identity = (await upload.Content.ReadFromJsonAsync<LibraryIdentityDto>())!;
        using var image = await _client.GetAsync($"/api/visual-identities/{identity.Id}/logos/{identity.LogoId}");
        image.EnsureSuccessStatusCode(); Assert.True(image.Headers.CacheControl!.NoStore);
        Assert.Equal(Image(), await image.Content.ReadAsByteArrayAsync());
        var post = await Post();
        using var generated = await _client.PostAsJsonAsync(Route(post)+"/generate", new {
            identity.IdentityId, Name="Temporary", Style="Flat", Colors=new[]{"#123456"}, VisualBrief="Garden",
            LibraryId=identity.Id, identity.Revision, Instructions="Temporary instruction" });
        generated.EnsureSuccessStatusCode();
        var visual = (await generated.Content.ReadFromJsonAsync<VisualDto>())!;
        Assert.Equal(identity.Id, visual.Generation!.EffectiveIdentity!.LibraryId);
        Assert.Equal(identity.LogoId, visual.Generation.EffectiveIdentity.LogoId);
        Assert.Equal("Temporary", visual.Generation.EffectiveIdentity.Name);
        using var deleted = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,$"/api/visual-identities/{identity.Id}") {
            Content=JsonContent.Create(new { identity.Revision }) });
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        using var stale = await _client.PostAsJsonAsync(Route(post)+"/generate", new {
            identity.IdentityId, identity.Name, identity.Style, identity.Colors, VisualBrief="Garden",
            LibraryId=identity.Id, Revision=0 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal(1,_generator.Calls);
    }

    private static byte[] Image()
    {
        using var bitmap=new SKBitmap(16,12); bitmap.Erase(SKColors.CornflowerBlue);
        using var image=SKImage.FromBitmap(bitmap); using var data=image.Encode(SKEncodedImageFormat.Png,90);
        return data.ToArray();
    }
    private sealed class Generator:IVisualGenerator
    {
        public int Calls; public bool Fail;
        public Task<GeneratedVisual> GenerateAsync(GenerateVisualRequest request,CancellationToken cancellationToken=default)
        {
            Calls++;
            if(Fail) throw new Exception("secret-provider-payload");
            var bytes=Image();
            return Task.FromResult(new GeneratedVisual(bytes,new("Fake","test",request.Identity.Id,request.Platform,
                request.VisualBrief,"image/png",16,12,bytes.Length,"medium",DateTimeOffset.UtcNow)));
        }
    }
    private sealed class AddressFilter(bool local):IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)=>app=>
        {
            app.Use(async(context,proceed)=>{context.Connection.RemoteIpAddress=local?IPAddress.Loopback:IPAddress.Parse("203.0.113.10");await proceed();});
            next(app);
        };
    }
}
