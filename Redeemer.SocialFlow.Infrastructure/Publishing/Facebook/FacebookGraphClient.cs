using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;

public sealed class FacebookGraphClient(HttpClient http, IOptions<FacebookOptions> options, TimeProvider clock, ILogger<FacebookGraphClient>? logger = null)
{
    public string Proof(string accessToken) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(options.Value.AppSecret), Encoding.UTF8.GetBytes(accessToken))).ToLowerInvariant();
    public static string Endpoint(string path) => "https://graph.facebook.com/" + FacebookOptions.Version + "/" + path;
    public async Task<FacebookConnection> ExchangeAsync(string code, CancellationToken token)
    {
        var stage = "configuration";
        try
        {
            if (!options.Value.Configured || string.IsNullOrWhiteSpace(code) || code.Length > 4096) throw new FacebookAuthorizationException();
            var now = clock.GetUtcNow();
            stage = "code_exchange";
            using var body = new FormUrlEncodedContent(new Dictionary<string,string> {
                ["client_id"] = options.Value.AppId, ["client_secret"] = options.Value.AppSecret,
                ["redirect_uri"] = FacebookOptions.Callback, ["code"] = code });
            using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint("oauth/access_token")) { Content = body };
            using var json = await SendAsync(message, token, "code_exchange");
            var access = ReadAccessToken(json.RootElement, stage);
            // This intermediate token is only exchanged, never persisted or used for Page access.
            // Its expiry is optional; if supplied, still reject an invalid or expired grant.
            if (json.RootElement.TryGetProperty("expires_in", out _) || json.RootElement.TryGetProperty("expires", out _)) ReadExpiry(json.RootElement, stage);
            stage = "token_extension";
            using var extension = new HttpRequestMessage(HttpMethod.Post, Endpoint("oauth/access_token")) {
                Content = new FormUrlEncodedContent(new Dictionary<string,string> { ["grant_type"] = "fb_exchange_token",
                    ["client_id"] = options.Value.AppId, ["client_secret"] = options.Value.AppSecret, ["fb_exchange_token"] = access }) };
            using var extended = await SendAsync(extension, token, "token_extension");
            access = ReadAccessToken(extended.RootElement, stage);
            var seconds = ReadExpiry(extended.RootElement, stage);
            stage = "permissions";
            var scopes = await PermissionsAsync(access, token);
            stage = "pages";
            var pages = await PagesAsync(access, token);
            // Conservative expiry of the user grant also bounds use of its Page tokens.
            return new() { AppId = options.Value.AppId, UserToken = access, ExpiresAt = now.AddSeconds(seconds), Scopes = scopes, Pages = pages };
        }
        catch (Exception error)
        {
            logger?.LogWarning("Facebook OAuth failed: Stage={Stage}; Category={Category}", stage, FailureCategory(error, token));
            throw;
        }
    }
    private string ReadAccessToken(JsonElement response, string stage)
    {
        if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("access_token", out var value)
            && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!;
        logger?.LogWarning("Facebook OAuth invalid response: Stage={Stage}; Field=access_token; Reason=missing_or_invalid", stage);
        throw new JsonException("Missing or invalid access_token.");
    }
    private long ReadExpiry(JsonElement response, string stage)
    {
        // Facebook's OAuth SDK handles both names as durations in seconds, not Unix timestamps.
        long? duration = null;
        foreach (var field in new[] { "expires_in", "expires" })
        {
            if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty(field, out var value)) continue;
            long seconds = 0;
            var parsed = value.ValueKind == JsonValueKind.Number ? value.TryGetInt64(out seconds)
                : value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 16 } text
                  && text.All(char.IsAsciiDigit) && long.TryParse(text, System.Globalization.NumberStyles.None,
                      System.Globalization.CultureInfo.InvariantCulture, out seconds);
            if (!parsed || seconds <= 0 || seconds > 366L * 86400)
            {
                logger?.LogWarning("Facebook OAuth invalid response: Stage={Stage}; Field={Field}; Reason=invalid_duration", stage, field);
                throw new JsonException("Invalid OAuth expiration duration.");
            }
            // If both are present, never extend beyond either provider-supplied bound.
            duration = duration.HasValue ? Math.Min(duration.Value, seconds) : seconds;
        }
        if (duration.HasValue) return duration.Value;
        // Unknown expiration is not permission to persist an indefinitely usable credential.
        logger?.LogWarning("Facebook OAuth invalid response: Stage={Stage}; Field=expires_in/expires; Reason=expiration_not_provided", stage);
        throw new JsonException("OAuth expiration was not provided.");
    }
    public async Task<string[]> PermissionsAsync(string access, CancellationToken token)
    {
        using var json = await GetAsync("me/permissions", access, token);
        var scopes = json.RootElement.GetProperty("data").EnumerateArray()
            .Where(p => p.GetProperty("status").GetString() == "granted").Select(p => p.GetProperty("permission").GetString()!).ToArray();
        var missing = FacebookOptions.RequiredScopes.Except(scopes, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            // Names come only from our static allowlist, never from provider error text.
            logger?.LogWarning("Facebook OAuth failed: Stage=permissions; Category=missing_permissions; Missing={Missing}", string.Join(',', missing));
            throw new FacebookAuthorizationException();
        }
        return scopes;
    }
    public async Task<FacebookPageCredential[]> PagesAsync(string access, CancellationToken token)
    {
        var pages = new List<FacebookPageCredential>(); string? after = null;
        var cursors = new HashSet<string>();
        for (var count = 0; count < 50; count++)
        {
            var path = "me/accounts?fields=id,name,access_token,tasks&limit=100";
            if (after is not null) path += "&after=" + Uri.EscapeDataString(after);
            using var json = await GetAsync(path, access, token);
            foreach (var page in json.RootElement.GetProperty("data").EnumerateArray())
            {
                var id = page.GetProperty("id").GetString();
                var name = page.GetProperty("name").GetString();
                if (!FacebookOptions.NumericId(id) || string.IsNullOrWhiteSpace(name)) throw new FacebookAuthorizationException();
                if (!page.TryGetProperty("access_token", out var key) || string.IsNullOrWhiteSpace(key.GetString())) continue;
                var tasks = page.TryGetProperty("tasks", out var granted) ? granted.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
                var entry = new FacebookPageCredential { Id = id!, Name = name, AccessToken = key.GetString()!, Tasks = tasks };
                if (entry.CanCreate) pages.Add(entry);
            }
            if (!json.RootElement.TryGetProperty("paging", out var paging) || paging.ValueKind != JsonValueKind.Object || !paging.TryGetProperty("next", out _)) return pages.DistinctBy(x => x.Id).ToArray();
            // Never follow provider pagination URLs: they may contain tokens or a different host.
            after = paging.GetProperty("cursors").GetProperty("after").GetString();
            if (string.IsNullOrWhiteSpace(after) || after.Length > 4096 || !cursors.Add(after)) throw new FacebookAuthorizationException();
        }
        throw new FacebookAuthorizationException();
    }
    private async Task<JsonDocument> GetAsync(string path, string access, CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, Endpoint(path + (path.Contains('?') ? "&" : "?") + "appsecret_proof=" + Proof(access)));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return await SendAsync(message, token, path.StartsWith("me/permissions", StringComparison.Ordinal) ? "permissions" : "pages");
    }
    public static string FailureCategory(Exception error, CancellationToken token) => error switch
    {
        OperationCanceledException => token.IsCancellationRequested ? "cancelled" : "timeout",
        HttpRequestException => "network_or_tls",
        FacebookAuthorizationException => "authorization_or_configuration",
        JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException => "invalid_response",
        _ => "internal"
    };
    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken token, string stage)
    {
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, deadline.Token);
        JsonDocument json;
        try { json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(deadline.Token)); }
        catch (JsonException)
        {
            logger?.LogWarning("Facebook HTTP failure: Stage={Stage}; HttpStatus={HttpStatus}; Category=invalid_response", stage, (int)response.StatusCode);
            throw;
        }
        if (!response.IsSuccessStatusCode || (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out _)))
        {
            int? code = null, subcode = null;
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out var meta) && meta.ValueKind == JsonValueKind.Object)
            {
                if (meta.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var number)) code = number;
                if (meta.TryGetProperty("error_subcode", out var sc) && sc.ValueKind == JsonValueKind.Number && sc.TryGetInt32(out var subnumber)) subcode = subnumber;
            }
            // Never log provider messages, response bodies, request URLs, headers or exception objects.
            logger?.LogWarning("Facebook HTTP failure: Stage={Stage}; HttpStatus={HttpStatus}; MetaCode={MetaCode}; MetaSubcode={MetaSubcode}", stage, (int)response.StatusCode, code, subcode);
            json.Dispose();
            throw new FacebookAuthorizationException();
        }

        return json;
    }
}
