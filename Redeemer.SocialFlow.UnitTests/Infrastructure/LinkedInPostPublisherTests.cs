using System.Net;
using System.Text.Json;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Infrastructure;

public sealed class LinkedInPostPublisherTests
{
    private const string Token = "private-test-token";
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static PostPublicationRequest Request(string destination = "urn:li:person:member-id", SocialPlatform platform = SocialPlatform.LinkedIn)
        => new("  Texte enregistré\nAvec accents, \"citations\" et lien https://example.org  ", platform, destination, Guid.NewGuid());

    [Theory]
    [InlineData("urn:li:share:123")]
    [InlineData("urn:li:ugcPost:456")]
    public async Task ConfirmedOnlyWithCreationAndId_SendsExactSnapshot(string id)
    {
        using var handler = new Handler(HttpStatusCode.Created, id);
        using var http = new HttpClient(handler);
        var credentials = new Credentials();
        var request = Request();
        var result = await new LinkedInPostPublisher(http, credentials, new Clock()).PublishAsync(request);
        Assert.Equal(PostPublicationOutcome.Confirmed, result.Outcome);
        Assert.Equal(id, result.ExternalId);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://api.linkedin.com/rest/posts", handler.Url);
        Assert.Equal("Bearer " + Token, handler.Authorization);
        Assert.Equal("202609", handler.Version);
        Assert.Equal("2.0.0", handler.Protocol);
        Assert.Equal("application/json", handler.ContentType);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal(request.Content, json.RootElement.GetProperty("commentary").GetString());
        Assert.Equal(request.Destination, json.RootElement.GetProperty("author").GetString());
        Assert.Equal("PUBLISHED", json.RootElement.GetProperty("lifecycleState").GetString());
        Assert.Equal("PUBLIC", json.RootElement.GetProperty("visibility").GetString());
        Assert.Equal(6, json.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("warnings", handler.Body);
        Assert.DoesNotContain("visualBrief", handler.Body);
        Assert.DoesNotContain(request.OperationId.ToString(), handler.Body);
        Assert.DoesNotContain(Token, result.ToString());
    }

    [Theory]
    [InlineData(400, PostPublicationOutcome.Failed)]
    [InlineData(401, PostPublicationOutcome.Failed)]
    [InlineData(403, PostPublicationOutcome.Failed)]
    [InlineData(422, PostPublicationOutcome.Failed)]
    [InlineData(429, PostPublicationOutcome.Failed)]
    [InlineData(408, PostPublicationOutcome.Indeterminate)]
    [InlineData(409, PostPublicationOutcome.Indeterminate)]
    [InlineData(500, PostPublicationOutcome.Indeterminate)]
    [InlineData(502, PostPublicationOutcome.Indeterminate)]
    [InlineData(503, PostPublicationOutcome.Indeterminate)]
    [InlineData(200, PostPublicationOutcome.Indeterminate)]
    [InlineData(202, PostPublicationOutcome.Indeterminate)]
    [InlineData(302, PostPublicationOutcome.Indeterminate)]
    public async Task HttpOutcomesNeverRetryOrLeakBodies(int status, PostPublicationOutcome expected)
    {
        using var handler = new Handler((HttpStatusCode)status, "urn:li:share:123");
        using var http = new HttpClient(handler);
        var result = await new LinkedInPostPublisher(http, new Credentials(), new Clock()).PublishAsync(Request());
        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.ExternalId);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain(Token, result.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(Token)]
    public async Task CreatedWithoutUsableIdIsIndeterminate(string? id)
    {
        using var handler = new Handler(HttpStatusCode.Created, id);
        using var http = new HttpClient(handler);
        var result = await new LinkedInPostPublisher(http, new Credentials(), new Clock()).PublishAsync(Request());
        Assert.Equal(PostPublicationOutcome.Indeterminate, result.Outcome);
        Assert.Null(result.ExternalId);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("expired")]
    [InlineData("wrong-member")]
    [InlineData("organization")]
    [InlineData("platform")]
    [InlineData("storage-error")]
    public async Task InvalidCredentialsOrTargetNeverDispatch(string scenario)
    {
        var credentials = new Credentials { Missing = scenario == "absent", Expired = scenario == "expired", Throw = scenario == "storage-error" };
        using var handler = new Handler(HttpStatusCode.Created, "urn:li:share:123");
        using var http = new HttpClient(handler);
        var destination = scenario == "wrong-member" ? "urn:li:person:another" : scenario == "organization" ? "urn:li:organization:member-id" : "urn:li:person:member-id";
        var result = await new LinkedInPostPublisher(http, credentials, new Clock()).PublishAsync(Request(destination,
            scenario == "platform" ? SocialPlatform.Facebook : SocialPlatform.LinkedIn));
        Assert.Equal(PostPublicationOutcome.Failed, result.Outcome);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("cancel")]
    public async Task PossibleDispatchExceptionIsIndeterminate(string failure)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler(HttpStatusCode.Created, null) { Failure = failure, Cancellation = cancellation };
        using var http = new HttpClient(handler);
        var result = await new LinkedInPostPublisher(http, new Credentials(), new Clock()).PublishAsync(Request(), cancellation.Token);
        Assert.Equal(PostPublicationOutcome.Indeterminate, result.Outcome);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain(Token, result.ToString());
    }

    [Fact]
    public async Task CancellationBeforeDispatchDoesNotSend()
    {
        using var handler = new Handler(HttpStatusCode.Created, null);
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LinkedInPostPublisher(http, new Credentials(), new Clock()).PublishAsync(Request(), cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    private sealed class Credentials : ILinkedInCredentialProvider
    {
        public bool Missing { get; init; }
        public bool Expired { get; init; }
        public bool Throw { get; init; }
        public Task<LinkedInMemberCredential?> GetAsync(CancellationToken cancellationToken)
        {
            if (Throw) throw new InvalidOperationException(Token);
            return Task.FromResult(Missing ? null : new LinkedInMemberCredential
            { AccessToken = Token, Subject = "member-id", ExpiresAt = Expired ? Now : Now.AddHours(1) });
        }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Handler(HttpStatusCode status, string? id) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body, Url, Authorization, Version, Protocol, ContentType;
        public HttpMethod? Method;
        public string? Failure { get; init; }
        public CancellationTokenSource? Cancellation { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure == "network") throw new HttpRequestException(Token);
            if (Failure == "timeout") throw new TaskCanceledException(Token);
            if (Failure == "cancel") { Cancellation!.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
            Method = request.Method; Url = request.RequestUri!.ToString(); Authorization = request.Headers.Authorization!.ToString();
            Version = request.Headers.GetValues("LinkedIn-Version").Single();
            Protocol = request.Headers.GetValues("X-Restli-Protocol-Version").Single();
            ContentType = request.Content!.Headers.ContentType!.MediaType;
            Body = await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(status) { Content = new StringContent(Token) };
            if (id is not null) response.Headers.TryAddWithoutValidation("x-restli-id", id);
            return response;
        }
    }
}
