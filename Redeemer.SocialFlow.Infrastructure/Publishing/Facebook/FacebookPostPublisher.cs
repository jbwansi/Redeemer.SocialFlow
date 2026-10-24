using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Enums;
namespace Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;

public sealed class FacebookPostPublisher(HttpClient http, IFacebookConnectionStore connections,
    FacebookGraphClient graph, IOptions<FacebookOptions> options, TimeProvider clock, IVisualStorage storage) : IPostPublisher
{
    public async Task<PostPublicationResult> PublishAsync(PostPublicationRequest request, CancellationToken cancellationToken = default)
    {
        var dispatched = false;
        try
        {
            if (request.Platform != SocialPlatform.Facebook || !FacebookOptions.NumericId(request.Destination) || !options.Value.Configured)
                return PostPublicationResult.Failed();
            var connection = await connections.ReadAsync(cancellationToken);
            if (connection is null || connection.AppId != options.Value.AppId || connection.ExpiresAt <= clock.GetUtcNow() ||
                FacebookOptions.RequiredScopes.Except(connection.Scopes).Any() || !connection.Pages.Any(p => p.Id == request.Destination && p.CanCreate))
                return PostPublicationResult.Failed();
            // Recheck the current grant and Page task before every external publication.
            await graph.PermissionsAsync(connection.UserToken, cancellationToken);
            var page = (await graph.PagesAsync(connection.UserToken, cancellationToken)).SingleOrDefault(p => p.Id == request.Destination);
            if (page is null) return PostPublicationResult.Failed();
            using var message = new HttpRequestMessage(HttpMethod.Post, FacebookGraphClient.Endpoint(page.Id + (request.Visual is null ? "/feed" : "/photos")));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", page.AccessToken);
            if (request.Visual is { } visual)
            {
                if (visual.ContentType is not ("image/png" or "image/jpeg") || visual.ByteLength is <= 0 or > 20 * 1024 * 1024 ||
                    visual.Sha256.Length != 64 || !visual.Sha256.All(Uri.IsHexDigit)) return PostPublicationResult.Failed();
                var bytes = await storage.ReadAsync(visual.StorageKey, cancellationToken);
                if (bytes.LongLength != visual.ByteLength || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(visual.Sha256)))
                    return PostPublicationResult.Failed();
                var body = new MultipartFormDataContent(); message.Content = body;
                var image = new ByteArrayContent(bytes); image.Headers.ContentType = new MediaTypeHeaderValue(visual.ContentType);
                body.Add(image, "source", visual.ContentType == "image/png" ? "visual.png" : "visual.jpg");
                body.Add(new StringContent(request.Content), "message");
                body.Add(new StringContent("true"), "published");
                body.Add(new StringContent(graph.Proof(page.AccessToken)), "appsecret_proof");
            }
            else message.Content = new FormUrlEncodedContent(new Dictionary<string,string> {
                ["message"] = request.Content, ["appsecret_proof"] = graph.Proof(page.AccessToken) });
            cancellationToken.ThrowIfCancellationRequested();
            if (connection.ExpiresAt <= clock.GetUtcNow()) return PostPublicationResult.Failed();
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
            var name = request.Visual is null ? "id" : "post_id";
            if (!json.RootElement.TryGetProperty(name, out var field)) return PostPublicationResult.Indeterminate();
            var id = field.GetString();
            return id is { Length: > 0 and <= 200 } && id.StartsWith(page.Id + "_", StringComparison.Ordinal) && FacebookOptions.NumericId(id[(page.Id.Length + 1)..])
                ? PostPublicationResult.Confirmed(id) : PostPublicationResult.Indeterminate();
        }
        catch (Exception) { return dispatched ? PostPublicationResult.Indeterminate() : PostPublicationResult.Failed(); }
    }
}
