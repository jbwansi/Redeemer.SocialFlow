using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Enums;
namespace Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;

public sealed class FacebookPostPublisher : IPostPublisher
{
    private readonly HttpClient http;
    private readonly IFacebookPublicationCredentialProvider credentials;
    private readonly TimeProvider clock;
    private readonly IVisualStorage? storage;
    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public FacebookPostPublisher(HttpClient http, IFacebookConnectionStore connections, FacebookGraphClient graph,
        IOptions<FacebookOptions> options, TimeProvider clock, IVisualStorage storage)
        : this(http, new LocalFacebookPublicationCredentials(connections, graph, options, clock), clock, storage) { }
    public FacebookPostPublisher(HttpClient http, IFacebookPublicationCredentialProvider credentials, TimeProvider clock, IVisualStorage? storage = null)
    { this.http = http; this.credentials = credentials; this.clock = clock; this.storage = storage; }
    public async Task<PostPublicationResult> PublishAsync(PostPublicationRequest request, CancellationToken cancellationToken = default)
    {
        var dispatched = false;
        try
        {
            if (request.Platform != SocialPlatform.Facebook || !FacebookOptions.NumericId(request.Destination))
                return PostPublicationResult.Failed();
            var page = await credentials.GetAsync(request.Destination, cancellationToken);
            if (page is null || page.PageId != request.Destination || string.IsNullOrWhiteSpace(page.AccessToken) ||
                string.IsNullOrWhiteSpace(page.AppSecret) || !System.Text.RegularExpressions.Regex.IsMatch(page.ApiVersion, "^v[0-9]+\\.0$") ||
                page.ExpiresAt <= clock.GetUtcNow()) return PostPublicationResult.Failed();
            using var message = new HttpRequestMessage(HttpMethod.Post, "https://graph.facebook.com/" + page.ApiVersion + "/" + page.PageId + (request.Visual is null ? "/feed" : "/photos"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", page.AccessToken);
            if (request.Visual is { } visual)
            {
                if (storage is null || visual.ContentType is not ("image/png" or "image/jpeg") || visual.ByteLength is <= 0 or > 20 * 1024 * 1024 ||
                    visual.Sha256.Length != 64 || !visual.Sha256.All(Uri.IsHexDigit)) return PostPublicationResult.Failed();
                var bytes = await storage.ReadAsync(visual.StorageKey, cancellationToken);
                if (bytes.LongLength != visual.ByteLength || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(visual.Sha256)))
                    return PostPublicationResult.Failed();
                var body = new MultipartFormDataContent(); message.Content = body;
                var image = new ByteArrayContent(bytes); image.Headers.ContentType = new MediaTypeHeaderValue(visual.ContentType);
                body.Add(image, "source", visual.ContentType == "image/png" ? "visual.png" : "visual.jpg");
                body.Add(new StringContent(request.Content), "message");
                body.Add(new StringContent("true"), "published");
                body.Add(new StringContent(page.Proof), "appsecret_proof");
            }
            else message.Content = new FormUrlEncodedContent(new Dictionary<string,string> {
                ["message"] = request.Content, ["appsecret_proof"] = page.Proof });
            cancellationToken.ThrowIfCancellationRequested();
            if (page.ExpiresAt <= clock.GetUtcNow()) return PostPublicationResult.Failed();
            // A /photos request uploads AND publishes atomically from this client's perspective.
            // Any lost acknowledgement must therefore be treated as potentially published.
            dispatched = true;
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 400)
                {
                    using var rejectionDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    rejectionDeadline.CancelAfter(TimeSpan.FromSeconds(30));
                    await response.Content.LoadIntoBufferAsync(64 * 1024, rejectionDeadline.Token);
                    using var rejection = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(rejectionDeadline.Token));
                    if (rejection.RootElement.TryGetProperty("error", out var error) &&
                        (!error.TryGetProperty("is_transient", out var transient) || transient.ValueKind == JsonValueKind.False) &&
                        error.TryGetProperty("code", out var code) && code.TryGetInt32(out var number) && number is 10 or 100 or 190 or 200 or 2500)
                        return PostPublicationResult.Failed();
                    return PostPublicationResult.Indeterminate();
                }
                return (int)response.StatusCode is 401 or 403 or 404 or 405 or 413 or 415 or 422 or 429
                    ? PostPublicationResult.Failed() : PostPublicationResult.Indeterminate();
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            await response.Content.LoadIntoBufferAsync(64 * 1024, deadline.Token);
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(deadline.Token));
            if (json.RootElement.TryGetProperty("error", out _)) return PostPublicationResult.Indeterminate();
            var name = request.Visual is null ? "id" : "post_id";
            if (!json.RootElement.TryGetProperty(name, out var field)) return PostPublicationResult.Indeterminate();
            var id = field.GetString();
            return id is { Length: > 0 and <= 200 } && id.StartsWith(page.PageId + "_", StringComparison.Ordinal) && FacebookOptions.NumericId(id[(page.PageId.Length + 1)..])
                ? PostPublicationResult.Confirmed(id) : PostPublicationResult.Indeterminate();
        }
        catch (Exception) { return dispatched ? PostPublicationResult.Indeterminate() : PostPublicationResult.Failed(); }
    }
}
