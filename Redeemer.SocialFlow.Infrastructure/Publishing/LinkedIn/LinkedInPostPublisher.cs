using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;

/// <summary>One attempt only. Call through the durable publication use case, never as a retry mechanism.</summary>
public sealed class LinkedInPostPublisher(HttpClient http, ILinkedInCredentialProvider credentials,
    TimeProvider clock) : IPostPublisher
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

            using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/rest/posts");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
            message.Headers.Add("LinkedIn-Version", ApiVersion);
            message.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
            message.Content = JsonContent.Create(new
            {
                author,
                commentary = request.Content,
                visibility = "PUBLIC",
                distribution = new { feedDistribution = "MAIN_FEED", targetEntities = Array.Empty<string>(), thirdPartyDistributionChannels = Array.Empty<string>() },
                lifecycleState = "PUBLISHED",
                isReshareDisabledByAuthor = false
            });
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

    private static bool IsPostId(string value)
    {
        // Documented identifiers only; do not persist arbitrary response text as an ID.
        var prefix = value.StartsWith("urn:li:share:", StringComparison.Ordinal) ? "urn:li:share:"
            : value.StartsWith("urn:li:ugcPost:", StringComparison.Ordinal) ? "urn:li:ugcPost:" : null;
        return prefix is not null && value.Length > prefix.Length && value[prefix.Length..].All(char.IsAsciiDigit);
    }
}
