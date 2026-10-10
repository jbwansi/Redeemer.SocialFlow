using System.Security.Cryptography;
using System.Text.Json;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;

/// <summary>One attempt only. Call through the durable publication use case, never as a retry mechanism.</summary>
public sealed class LinkedInPostPublisher(HttpClient http, ILinkedInCredentialProvider credentials,
    TimeProvider clock, IVisualStorage? storage = null) : IPostPublisher
{
    public const string ApiVersion = "202609";

    public async Task<PostPublicationResult> PublishAsync(PostPublicationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Platform != SocialPlatform.LinkedIn) return PostPublicationResult.Failed();

        var dispatchPossible = false;
        try
        {
            var member = await credentials.GetAsync(cancellationToken);
            if (member is null || member.ExpiresAt <= clock.GetUtcNow() || string.IsNullOrWhiteSpace(member.AccessToken) ||
                string.IsNullOrWhiteSpace(member.Subject) ||
                member.Subject.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
                return PostPublicationResult.Failed();
            var author = "urn:li:person:" + member.Subject;
            if (!string.Equals(request.Destination, author, StringComparison.Ordinal)) return PostPublicationResult.Failed();

            string? image = null;
            if (request.Visual is not null)
            {
                image = await UploadImageAsync(request.Visual, author, member, cancellationToken);
                if (image is null) return PostPublicationResult.Failed();
            }
            using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/rest/posts");
            SetApiHeaders(message, member.AccessToken);
            var payload = new Dictionary<string, object>
            {
                ["author"] = author,
                ["commentary"] = request.Content,
                ["visibility"] = "PUBLIC",
                ["distribution"] = new { feedDistribution = "MAIN_FEED", targetEntities = Array.Empty<string>(), thirdPartyDistributionChannels = Array.Empty<string>() },
                ["lifecycleState"] = "PUBLISHED",
                ["isReshareDisabledByAuthor"] = false
            };
            if (image is not null) payload["content"] = new { media = new { id = image } };
            message.Content = JsonContent.Create(payload);

            cancellationToken.ThrowIfCancellationRequested();
            if (member.ExpiresAt <= clock.GetUtcNow()) return PostPublicationResult.Failed();
            dispatchPossible = true;
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            // Do not read bodies: neither provider errors nor echoed post content are needed.
            if (response.StatusCode == HttpStatusCode.Created && response.Headers.TryGetValues("x-restli-id", out var ids))
            {
                var values = ids.ToArray();
                if (values.Length == 1 && IsPostId(values[0])) return PostPublicationResult.Confirmed(values[0]);
            }
            // Only explicit rejection statuses attest non-publication. In particular 408/409
            // and all 5xx remain ambiguous; OperationId is not a provider idempotency key.
            return (int)response.StatusCode is 400 or 401 or 403 or 404 or 405 or 413 or 415 or 422 or 429
                ? PostPublicationResult.Failed() : PostPublicationResult.Indeterminate();
        }
        catch (OperationCanceledException) when (!dispatchPossible && cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Never propagate provider/credential exception messages containing sensitive data.
            return dispatchPossible ? PostPublicationResult.Indeterminate() : PostPublicationResult.Failed();
        }
    }


    private async Task<string?> UploadImageAsync(PublicationVisualSnapshot visual, string author,
        LinkedInMemberCredential member, CancellationToken token)
    {
        if (storage is null || visual.VisualId == Guid.Empty ||
            visual.ContentType is not ("image/png" or "image/jpeg") ||
            visual.ByteLength is <= 0 or > 20 * 1024 * 1024 ||
            visual.Width <= 0 || visual.Height <= 0 || (long)visual.Width * visual.Height >= 36_152_320 ||
            visual.Sha256.Length != 64 || !visual.Sha256.All(Uri.IsHexDigit))
            return null;
        var bytes = await storage.ReadAsync(visual.StorageKey, token);
        if (bytes.LongLength != visual.ByteLength ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(visual.Sha256)))
            return null;
        if (member.ExpiresAt <= clock.GetUtcNow()) return null;
        using var initialize = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/rest/images?action=initializeUpload");
        SetApiHeaders(initialize, member.AccessToken);
        initialize.Content = JsonContent.Create(new { initializeUploadRequest = new { owner = author } });
        using var initialized = await http.SendAsync(initialize, HttpCompletionOption.ResponseHeadersRead, token);
        if (initialized.StatusCode != HttpStatusCode.OK) return null;
        using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        readDeadline.CancelAfter(TimeSpan.FromSeconds(30));
        await initialized.Content.LoadIntoBufferAsync(64 * 1024, readDeadline.Token);
        using var json = JsonDocument.Parse(await initialized.Content.ReadAsByteArrayAsync(token));
        var value = json.RootElement.GetProperty("value");
        var urn = value.GetProperty("image").GetString();
        var url = value.GetProperty("uploadUrl").GetString();
        var expiry = value.GetProperty("uploadUrlExpiresAt").GetInt64();
        if (!IsImageId(urn) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host != "www.linkedin.com" || uri.Port != 443 ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith("/dms-uploads/", StringComparison.Ordinal) ||
            expiry <= clock.GetUtcNow().ToUnixTimeMilliseconds() || member.ExpiresAt <= clock.GetUtcNow())
            return null;
        // The existing HttpClient disables redirects, cookies, logging and retries.
        // Never forward bearer credentials to an arbitrary URL returned by the provider.
        using var upload = new HttpRequestMessage(HttpMethod.Put, uri);
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        upload.Content = new ByteArrayContent(bytes);
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue(visual.ContentType);
        using var uploaded = await http.SendAsync(upload, HttpCompletionOption.ResponseHeadersRead, token);
        // w_member_social is write-only for versioned Images API: no GET/polling or new OAuth scopes.
        return uploaded.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent ? urn : null;
    }

    private static void SetApiHeaders(HttpRequestMessage message, string token)
    {
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add("LinkedIn-Version", ApiVersion);
        message.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
    }

    private static bool IsImageId(string? value) => value is not null &&
        value.StartsWith("urn:li:image:", StringComparison.Ordinal) && value.Length is > 13 and <= 200 &&
        value["urn:li:image:".Length..].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool IsPostId(string value)
    {
        // Documented identifiers only; do not persist arbitrary response text as an ID.
        var prefix = value.StartsWith("urn:li:share:", StringComparison.Ordinal) ? "urn:li:share:"
            : value.StartsWith("urn:li:ugcPost:", StringComparison.Ordinal) ? "urn:li:ugcPost:" : null;
        return prefix is not null && value.Length > prefix.Length && value[prefix.Length..].All(char.IsAsciiDigit);
    }
}
