using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Infrastructure;

public sealed class LinkedInImagePublisherTests
{
    private static readonly byte[] Bytes = [137, 80, 78, 71, 1, 2, 3, 4];
    private static PublicationVisualSnapshot Snapshot() => new(Guid.NewGuid(), Guid.NewGuid().ToString("N"),
        "image/png", Bytes.Length, Convert.ToHexString(SHA256.HashData(Bytes)), 16, 12, DateTimeOffset.UtcNow);
    private static PostPublicationRequest Request(PublicationVisualSnapshot? visual) =>
        new("Exact text", SocialPlatform.LinkedIn, "urn:li:person:member", Guid.NewGuid(), visual);

    [Fact]
    public async Task UploadsExactOriginalBytesThenCreatesImagePost()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var storage = new Storage();
        var result = await new LinkedInPostPublisher(http, new Credentials(), TimeProvider.System, storage).PublishAsync(Request(Snapshot()));
        Assert.Equal(PostPublicationOutcome.Confirmed, result.Outcome);
        Assert.Equal(new[] { "POST /rest/images?action=initializeUpload", "PUT /dms-uploads/image/upload?signature=private", "POST /rest/posts" }, handler.Paths);
        Assert.Equal(Bytes, handler.Upload);
        Assert.Equal("image/png", handler.UploadMime);
        Assert.Equal(1, storage.Reads);
        using var body = JsonDocument.Parse(handler.Post!);
        Assert.Equal("Exact text", body.RootElement.GetProperty("commentary").GetString());
        Assert.Equal("urn:li:image:IMAGE123", body.RootElement.GetProperty("content").GetProperty("media").GetProperty("id").GetString());
        Assert.DoesNotContain("StorageKey", handler.Post);
        Assert.DoesNotContain("secret-token", result.ToString());
    }

    [Theory]
    [InlineData("init", 403, PostPublicationOutcome.Failed, 1)]
    [InlineData("init", 500, PostPublicationOutcome.Failed, 1)]
    [InlineData("upload", 400, PostPublicationOutcome.Failed, 2)]
    [InlineData("upload", 503, PostPublicationOutcome.Failed, 2)]
    [InlineData("upload", 302, PostPublicationOutcome.Failed, 2)]
    [InlineData("post", 403, PostPublicationOutcome.Failed, 3)]
    [InlineData("post", 500, PostPublicationOutcome.Indeterminate, 3)]
    [InlineData("post", 409, PostPublicationOutcome.Indeterminate, 3)]
    public async Task FailureNeverFallsBackOrRetries(string stage, int code, PostPublicationOutcome outcome, int calls)
    {
        using var handler = new Handler { FailStage = stage, Code = code };
        using var http = new HttpClient(handler);
        var result = await new LinkedInPostPublisher(http, new Credentials(), TimeProvider.System, new Storage()).PublishAsync(Request(Snapshot()));
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(calls, handler.Paths.Count);
        if (stage != "post") Assert.Null(handler.Post);
    }

    [Theory]
    [InlineData("https://attacker.example/dms-uploads/a")]
    [InlineData("http://www.linkedin.com/dms-uploads/a")]
    [InlineData("https://www.linkedin.com:444/dms-uploads/a")]
    [InlineData("https://www.linkedin.com.evil.test/dms-uploads/a")]
    [InlineData("https://user@www.linkedin.com/dms-uploads/a")]
    [InlineData("https://www.linkedin.com/rest/posts")]
    public async Task RejectsUntrustedUploadDestinationBeforeSendingTokenOrImage(string url)
    {
        using var handler = new Handler { UploadUrl = url };
        using var http = new HttpClient(handler);
        var result = await new LinkedInPostPublisher(http, new Credentials(), TimeProvider.System, new Storage()).PublishAsync(Request(Snapshot()));
        Assert.Equal(PostPublicationOutcome.Failed, result.Outcome);
        Assert.Single(handler.Paths);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("missing")]
    [InlineData("mime")]
    [InlineData("length")]
    public async Task UnusablePrivateImagePreventsAllHttpCalls(string reason)
    {
        var visual = Snapshot();
        if (reason == "hash") visual = visual with { Sha256 = new string('0', 64) };
        if (reason == "mime") visual = visual with { ContentType = "image/svg+xml" };
        if (reason == "length") visual = visual with { ByteLength = 123 };
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var result = await new LinkedInPostPublisher(http, new Credentials(), TimeProvider.System,
            new Storage { Fail = reason == "missing" }).PublishAsync(Request(visual));
        Assert.Equal(PostPublicationOutcome.Failed, result.Outcome);
        Assert.Empty(handler.Paths);
    }

    [Theory]
    [InlineData("upload", PostPublicationOutcome.Failed)]
    [InlineData("post", PostPublicationOutcome.Indeterminate)]
    public async Task NetworkUncertaintyIsClassifiedAtThePostBoundary(string stage, PostPublicationOutcome outcome)
    {
        using var handler = new Handler { ThrowStage = stage };
        using var http = new HttpClient(handler);
        var result = await new LinkedInPostPublisher(http, new Credentials(), TimeProvider.System, new Storage()).PublishAsync(Request(Snapshot()));
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(stage == "post" ? 3 : 2, handler.Paths.Count);
    }

    [Fact]
    public async Task TextOnlyDoesNotReadStorageOrInitializeUpload()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var storage = new Storage { Fail = true };
        var result = await new LinkedInPostPublisher(http, new Credentials(), TimeProvider.System, storage).PublishAsync(Request(null));
        Assert.Equal(PostPublicationOutcome.Confirmed, result.Outcome);
        Assert.Single(handler.Paths);
        Assert.Equal(0, storage.Reads);
        using var body = JsonDocument.Parse(handler.Post!);
        Assert.False(body.RootElement.TryGetProperty("content", out _));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("urn")]
    [InlineData("json")]
    [InlineData("oversized")]
    public async Task InvalidInitializationNeverUploadsOrPublishes(string failure)
    {
        using var handler = new Handler { InvalidInitialization = failure };
        using var http = new HttpClient(handler);
        var result = await new LinkedInPostPublisher(http, new Credentials(), TimeProvider.System, new Storage()).PublishAsync(Request(Snapshot()));
        Assert.Equal(PostPublicationOutcome.Failed, result.Outcome);
        Assert.Single(handler.Paths);
        Assert.Null(handler.Upload);
    }

    private sealed class Storage : IVisualStorage
    {
        public int Reads;
        public bool Fail;
        public Task<byte[]> ReadAsync(string key, CancellationToken token = default)
        { Reads++; if (Fail) throw new VisualStorageException(); return Task.FromResult(Bytes.ToArray()); }
        public Task WriteAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken token = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class Credentials : ILinkedInCredentialProvider
    {
        public Task<LinkedInMemberCredential?> GetAsync(CancellationToken token) => Task.FromResult<LinkedInMemberCredential?>(
            new() { Subject = "member", AccessToken = "secret-token", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
    }
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Paths = [];
        public string? Post;
        public byte[]? Upload;
        public string? UploadMime;
        public string? FailStage, ThrowStage, InvalidInitialization;
        public int Code;
        public string UploadUrl = "https://www.linkedin.com/dms-uploads/image/upload?signature=private";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.PathAndQuery;
            Paths.Add(request.Method + " " + path);
            Assert.Equal("Bearer secret-token", request.Headers.Authorization!.ToString());
            var stage = path.StartsWith("/rest/images") ? "init" : request.Method == HttpMethod.Put ? "upload" : "post";
            if (stage == "upload") { Upload = await request.Content!.ReadAsByteArrayAsync(token); UploadMime = request.Content.Headers.ContentType!.MediaType; }
            if (stage == "post") Post = await request.Content!.ReadAsStringAsync(token);
            if (stage == ThrowStage) throw new HttpRequestException("sensitive-token");
            if (stage == FailStage) return new((HttpStatusCode)Code);
            if (stage == "init")
            {
                if (InvalidInitialization == "json") return new(HttpStatusCode.OK) { Content = new StringContent("invalid") };
                if (InvalidInitialization == "oversized") return new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 65537)) };
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("urn:li:person:member", body.RootElement.GetProperty("initializeUploadRequest").GetProperty("owner").GetString());
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = new {
                    image = InvalidInitialization == "urn" ? "urn:li:share:123" : "urn:li:image:IMAGE123", uploadUrl = UploadUrl, uploadUrlExpiresAt = InvalidInitialization == "expired" ? 1 : DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() } }) };
            }
            var result = new HttpResponseMessage(HttpStatusCode.Created);
            if (stage == "post") result.Headers.Add("x-restli-id", "urn:li:share:123");
            return result;
        }
    }
}
