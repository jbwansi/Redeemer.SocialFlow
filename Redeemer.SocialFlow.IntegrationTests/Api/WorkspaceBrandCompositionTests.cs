using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using SkiaSharp;
using Xunit;
namespace Redeemer.SocialFlow.IntegrationTests.Api;
public sealed partial class WorkspaceIsolationTests
{
    private static byte[] BrandImage(bool logo, SKColor color, bool embeddedSlogan = false)
    {
        using var bitmap = new SKBitmap(logo ? 120 : 600, logo ? 60 : 400);
        using var canvas = new SKCanvas(bitmap); canvas.Clear(logo ? SKColors.Transparent : color);
        if (logo) { using var paint = new SKPaint { Color = color }; canvas.DrawRect(30, 15, 60, 30, paint); }
        if (embeddedSlogan) { using var font = new SKFont(SKTypeface.Default, 5); using var textPaint = new SKPaint { Color = SKColors.Black }; canvas.DrawText("TRANSFORMER PAR LES VALEURS", 5, 55, SKTextAlign.Left, font, textPaint); }
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    private static async Task<HttpResponseMessage> BrandUpload(HttpClient client, string path, byte[] bytes, long? revision = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", "test.png");
        if (revision.HasValue) form.Add(new StringContent(revision.Value.ToString()), "revision");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await client.SendAsync(request);
    }
    private static async Task<LibraryIdentityDto> BrandIdentity(HttpClient client, string name, byte[]? logo)
    {
        var response = await Send(client, HttpMethod.Post, IdentityRoot + "/visual", new {
            identityId = name, name, style = "Flat", colors = new[] { "#DA2E29", "#FFFFFF" }, slogan = "TRANSFORMER PAR LES VALEURS" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var identity = (await response.Content.ReadFromJsonAsync<LibraryIdentityDto>())!;
        if (logo is not null)
        {
            var uploaded = await BrandUpload(client, IdentityRoot + $"/visual/{identity.Id}/logo", logo, identity.Revision);
            Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
            identity = (await uploaded.Content.ReadFromJsonAsync<LibraryIdentityDto>())!;
        }
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Post, IdentityRoot + $"/visual/{identity.Id}/approve", new { identity.Revision })).StatusCode);
        return identity;
    }
    [Theory]
    [InlineData("Compact", 800, 80)]
    [InlineData("Compact", 80, 800)]
    [InlineData("Classic", 800, 80)]
    [InlineData("Classic", 80, 800)]
    public async Task BrandComposition_LayoutsPreserveBlackSource(string layout, int logoWidth, int logoHeight)
    {
        using var mark = new SKBitmap(logoWidth, logoHeight);
        using var canvas = new SKCanvas(mark); canvas.Clear(SKColors.Red);
        using var image = SKImage.FromBitmap(mark); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var logoBytes = data.ToArray();
        var identity = await BrandIdentity(alice, "Layout", logoBytes);
        var post = await Create(alice, "Subject"); var root = $"/api/posts/{post.Id}/workspace-visuals";
        var bytes = BrandImage(false, SKColors.Black);
        var source = (await (await BrandUpload(alice, root + "/upload", bytes)).Content.ReadFromJsonAsync<VisualDto>())!;
        async Task<VisualDto> Compose(string mode)
        {
            var response = await Send(alice, HttpMethod.Post, root + $"/{source.Id}/compose", new { libraryId = identity.Id, identity.Revision, mode, layout, logoContainsSlogan = true });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<VisualDto>())!;
        }
        var result = await Compose("LogoAndSlogan"); var logoOnly = await Compose("Logo");
        Assert.Equal(logoOnly.Sha256, result.Sha256);
        Assert.Equal(layout, result.Generation!.Composition!.Layout);
        Assert.Equal(600, result.Width); Assert.Equal(layout == "Compact" ? 480 : 500, result.Height);
        using var bitmap = SKBitmap.Decode(await alice.GetByteArrayAsync(root + $"/{result.Id}"));
        for (var y = 0; y < 400; y++) for (var x = 0; x < 600; x++) Assert.Equal(SKColors.Black, bitmap.GetPixel(x, y));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(300, 400 + (result.Height - 400) / 2));
        Assert.Equal(bytes, await alice.GetByteArrayAsync(root + $"/{source.Id}"));
        Assert.Equal(logoBytes, await alice.GetByteArrayAsync(IdentityRoot + $"/visual/{identity.Id}/logos/{identity.LogoId}"));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, root + $"/{source.Id}/compose", new { libraryId = identity.Id, identity.Revision, mode = "Logo", layout = "Overlay" })).StatusCode);
        Assert.False(result.Selected); Assert.Null(draftGenerator.Received);
        var text = new VisualTextOptions("Title", "Un titre fidèle");
        var reviewResponse = await Send(alice, HttpMethod.Post, root + "/text-review", text);
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        var review = (await reviewResponse.Content.ReadFromJsonAsync<VisualTextReview>())!;
        var textResult = await Send(alice, HttpMethod.Post, root + $"/{source.Id}/compose", new {
            libraryId = identity.Id, identity.Revision, mode = "Logo", layout, text,
            textFingerprint = review.Fingerprint, textConfirmed = true });
        Assert.Equal(HttpStatusCode.Created, textResult.StatusCode);
        var withText = (await textResult.Content.ReadFromJsonAsync<VisualDto>())!;
        Assert.Equal(text.Title, withText.Generation!.Composition!.Text!.Title);
        Assert.Equal(layout, withText.Generation.Composition.Layout);
        Assert.False(withText.Selected);
    }
    [Fact]
    public async Task BrandComposition_PreservesOriginalsTransparencyAndDoesNotSelectOrDuplicateSlogan()
    {
        var sourceBytes = BrandImage(false, SKColors.Blue); var logoBytes = BrandImage(true, SKColors.Red, true);
        var identity = await BrandIdentity(alice, "A", logoBytes);
        var post = await Create(alice, "Subject"); var root = $"/api/posts/{post.Id}/workspace-visuals";
        var upload = await BrandUpload(alice, root + "/upload", sourceBytes);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var source = (await upload.Content.ReadFromJsonAsync<VisualDto>())!;
        async Task<VisualDto> Compose(string mode, bool? integrated = null)
        {
            var response = await Send(alice, HttpMethod.Post, root + $"/{source.Id}/compose", new { libraryId = identity.Id, identity.Revision, mode, logoContainsSlogan = integrated });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<VisualDto>())!;
        }
        var logo = await Compose("Logo"); var integrated = await Compose("LogoAndSlogan", true);
        var both = await Compose("LogoAndSlogan", false); var slogan = await Compose("Slogan");
        Assert.Equal(logo.Sha256, integrated.Sha256); Assert.NotEqual(logo.Sha256, both.Sha256);
        Assert.Equal("Slogan", slogan.Generation!.Composition!.Mode);
        Assert.Equal("TRANSFORMER PAR LES VALEURS", both.Generation!.EffectiveIdentity!.Slogan);
        Assert.Equal(source.Id, logo.Generation!.Composition!.SourceVisualId);
        Assert.Equal(source.Sha256, logo.Generation.Composition.SourceSha256);
        Assert.Equal(600, logo.Width); Assert.Equal(500, logo.Height);
        var resultBytes = await alice.GetByteArrayAsync(root + $"/{logo.Id}");
        using var result = SKBitmap.Decode(resultBytes);
        Assert.Equal(SKColors.Blue, result.GetPixel(10, 10));
        Assert.Equal(SKColors.White, result.GetPixel(241, 421)); // transparent logo corner
        Assert.Equal(SKColors.Red, result.GetPixel(300, 450));
        Assert.Equal(sourceBytes, await alice.GetByteArrayAsync(root + $"/{source.Id}"));
        Assert.Equal(logoBytes, await alice.GetByteArrayAsync(IdentityRoot + $"/visual/{identity.Id}/logos/{identity.LogoId}"));
        Assert.All((await alice.GetFromJsonAsync<VisualDto[]>(root))!, x => { Assert.False(x.Selected); Assert.Null(x.ValidatedAt); });
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, root + $"/{logo.Id}/compose", new { libraryId = identity.Id, identity.Revision, mode = "Logo" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, root + $"/{source.Id}/compose", new { libraryId = identity.Id, identity.Revision, mode = "LogoAndSlogan" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Delete, root + $"/{source.Id}")).StatusCode);
        Assert.Null(draftGenerator.Received);
    }
    [Fact]
    public async Task BrandComposition_RejectsForeignObsoleteAndMissingAssetsButAllowsSloganWithoutLogo()
    {
        var first = await BrandIdentity(alice, "A", null);
        var other = await BrandIdentity(bob, "B", BrandImage(true, SKColors.White));
        var otherPost = await Create(bob, "Other subject"); var otherRoot = $"/api/posts/{otherPost.Id}/workspace-visuals";
        var otherSource = (await (await BrandUpload(bob, otherRoot + "/upload", BrandImage(false, SKColors.Blue))).Content.ReadFromJsonAsync<VisualDto>())!;
        var otherResult = await Send(bob, HttpMethod.Post, otherRoot + $"/{otherSource.Id}/compose", new { libraryId = other.Id, other.Revision, mode = "Logo" });
        Assert.Equal(HttpStatusCode.Created, otherResult.StatusCode);
        var otherVisual = (await otherResult.Content.ReadFromJsonAsync<VisualDto>())!;
        Assert.Equal("B", otherVisual.Generation!.EffectiveIdentity!.Name);
        using var otherImage = SKBitmap.Decode(await bob.GetByteArrayAsync(otherRoot + $"/{otherVisual.Id}"));
        Assert.Equal(SKColors.White, otherImage.GetPixel(300, 450));
        Assert.Equal(SKColors.Black, otherImage.GetPixel(241, 421));
        var post = await Create(alice, "Subject"); var root = $"/api/posts/{post.Id}/workspace-visuals";
        var source = (await (await BrandUpload(alice, root + "/upload", BrandImage(false, SKColors.Blue))).Content.ReadFromJsonAsync<VisualDto>())!;
        var route = root + $"/{source.Id}/compose";
        Assert.Equal(HttpStatusCode.NotFound, (await Send(alice, HttpMethod.Post, route, new { libraryId = other.Id, other.Revision, mode = "Logo" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(bob, HttpMethod.Post, route, new { libraryId = other.Id, other.Revision, mode = "Logo" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, route, new { libraryId = first.Id, first.Revision, mode = "Logo" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(alice, HttpMethod.Post, route, new { libraryId = first.Id, first.Revision, mode = "Slogan", slogan = "BROWSER INJECTION" })).StatusCode);
        await Send(alice, HttpMethod.Put, IdentityRoot + $"/visual/{first.Id}", new { first.Revision, identity = new { identityId = "A", name = "A", style = "New", colors = new[] { "#FFFFFF" } } });
        Assert.Equal(HttpStatusCode.Conflict, (await Send(alice, HttpMethod.Post, route, new { libraryId = first.Id, first.Revision, mode = "Slogan" })).StatusCode);
        await using var db = Db(a.Id); Assert.False(db.Database.HasPendingModelChanges());
    }
    [Theory]
    [InlineData(WorkspaceRole.Editor, true)]
    [InlineData(WorkspaceRole.Viewer, false)]
    public async Task BrandComposition_UsesWorkspaceEditPermission(WorkspaceRole role, bool allowed)
    {
        var identity = await BrandIdentity(alice, "A", null);
        var post = await Create(alice, "Subject"); var root = $"/api/posts/{post.Id}/workspace-visuals";
        var source = (await (await BrandUpload(alice, root + "/upload", BrandImage(false, SKColors.Blue))).Content.ReadFromJsonAsync<VisualDto>())!;
        await using (var db = Db(a.Id)) { db.Add(WorkspaceMembership.Join(a.Id, bobId, role)); await db.SaveChangesAsync(); }
        bob.DefaultRequestHeaders.Remove("X-Workspace-Id"); bob.DefaultRequestHeaders.Add("X-Workspace-Id", a.Id.ToString());
        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden,
            (await Send(bob, HttpMethod.Post, root + $"/{source.Id}/compose", new { libraryId = identity.Id, identity.Revision, mode = "Slogan" })).StatusCode);
    }
}
