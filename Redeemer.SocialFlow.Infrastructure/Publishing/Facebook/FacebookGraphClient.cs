using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;

public sealed class FacebookGraphClient(HttpClient http, IOptions<FacebookOptions> options, TimeProvider clock, ILogger<FacebookGraphClient>? logger = null, IHostEnvironment? environment = null)
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
            DateTimeOffset? expiresAt;
            DateTimeOffset? dataAccessExpiresAt = null;
            var hasRelativeExpiry = extended.RootElement.TryGetProperty("expires_in", out _) || extended.RootElement.TryGetProperty("expires", out _);
            if (hasRelativeExpiry)
            {
                expiresAt = now.AddSeconds(ReadExpiry(extended.RootElement, stage));
                logger?.LogInformation("Facebook OAuth diagnostics: Stage=token_extension; DebugTokenStatus=not_requested; Reason=relative_expiry_present");
            }
            else
            {
                stage = "debug_token";
                var verified = await VerifyUserTokenAsync(access, token);
                expiresAt = verified.ExpiresAt;
                dataAccessExpiresAt = verified.DataAccessExpiresAt;
            }
            stage = "permissions";
            var scopes = await PermissionsAsync(access, token);
            if (hasRelativeExpiry)
                foreach (var permission in FacebookOptions.RequiredScopes)
                    LogPermissionResources("permissions", permission, scopes.Contains(permission, StringComparer.Ordinal), "me_permissions", null, null);
            stage = "pages";
            logger?.LogInformation("Facebook accounts request: Stage=pages; TokenSource=final_oauth_exchange; GraphVersion={GraphVersion}; Resource=me/accounts; RequestedLimit=100", FacebookOptions.Version);
            var pages = await PagesAsync(access, token);
            stage = "connection_validation";
            if (expiresAt <= clock.GetUtcNow() || dataAccessExpiresAt <= clock.GetUtcNow()) throw InvalidDebug("expires_at", "expired_before_storage", stage);
            // Conservative expiry of the user grant also bounds use of its Page tokens.
            if (environment?.IsDevelopment() == true && options.Value.OAuthResourceDiagnosticsEnabled)
                await DiagnoseResourcesAsync(access, token);
            return new() { AppId = options.Value.AppId, UserToken = access, ExpiresAt = expiresAt, DataAccessExpiresAt = dataAccessExpiresAt, Scopes = scopes, Pages = pages };
        }
        catch (Exception error)
        {
            logger?.LogWarning("Facebook OAuth failed: Stage={Stage}; Category={Category}; ExceptionType={ExceptionType}", stage, FailureCategory(error, token), error.GetType().Name);
            throw;
        }
    }
    public sealed record VerifiedUserToken(DateTimeOffset? ExpiresAt, DateTimeOffset? DataAccessExpiresAt)
    {
        public bool HasElapsedDeadline(DateTimeOffset now) => ExpiresAt <= now || DataAccessExpiresAt <= now;
    }
    public async Task<VerifiedUserToken> VerifyUserTokenAsync(string access, CancellationToken token)
    {
        try { return await InspectUserTokenAsync(access, token); }
        catch (Exception error)
        {
            logger?.LogWarning("Facebook OAuth revalidation failed: Stage=debug_token; Category={Category}; ExceptionType={ExceptionType}", FailureCategory(error, token), error.GetType().Name);
            throw;
        }
    }
    private async Task<VerifiedUserToken> InspectUserTokenAsync(string access, CancellationToken token)
    {
        // Meta's GET contract requires input_token in the query. Never log this URI;
        // the registered HttpClient removes all HTTP loggers and disables redirects.
        // Authenticate with an app access token in the header, not with a secret in the URL.
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint("debug_token?input_token=" + Uri.EscapeDataString(access)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AppId + "|" + options.Value.AppSecret);
        using var json = await SendAsync(request, token, "debug_token");
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().Count(property => property.Name == "data") != 1 ||
            data.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw InvalidDebug("data", "missing_or_ambiguous");
        // Derived labels only: never log identifiers, timestamps, or provider-supplied strings.
        var now = clock.GetUtcNow();
        var expiryState = TimestampShape(data, "expires_at");
        if (expiryState == "positive_integer")
        {
            var instant = DateTimeOffset.FromUnixTimeSeconds(data.GetProperty("expires_at").GetInt64());
            expiryState = instant <= now ? "expired" : instant > now.AddDays(366) ? "beyond_policy" : "valid";
        }
        var typeState = !data.TryGetProperty("type", out var observedType) ? "missing"
            : observedType.ValueKind != JsonValueKind.String ? "unexpected_type"
            : observedType.GetString() switch { "USER" => "USER", "PAGE" => "PAGE", _ => "other" };
        var validityState = !data.TryGetProperty("is_valid", out var observedValidity) ? "missing"
            : observedValidity.ValueKind == JsonValueKind.True ? "true"
            : observedValidity.ValueKind == JsonValueKind.False ? "false" : "unexpected_type";
        var observedApp = data.TryGetProperty("app_id", out var appValue)
            ? appValue.ValueKind == JsonValueKind.String ? appValue.GetString() : appValue.ValueKind == JsonValueKind.Number ? appValue.GetRawText() : null : null;
        var appState = !data.TryGetProperty("app_id", out _) ? "missing" : !FacebookOptions.NumericId(observedApp) ? "invalid_format"
            : string.Equals(observedApp, options.Value.AppId, StringComparison.Ordinal) ? "match" : "mismatch";
        logger?.LogInformation("Facebook OAuth metadata: Stage=debug_token; ExpiryState={ExpiryState}; TokenType={TokenType}; Validity={Validity}; AppMatch={AppMatch}",
            expiryState, typeState, validityState, appState);
        if (data.TryGetProperty("error", out _)) throw InvalidDebug("error", "contradictory_metadata");
        LogResourceGrantShape(data);
        if (!data.TryGetProperty("is_valid", out var valid) || valid.ValueKind != JsonValueKind.True)
            throw InvalidDebug("is_valid", "not_valid");
        if (!data.TryGetProperty("app_id", out var app)) throw InvalidDebug("app_id", "missing_or_mismatched");
        // App IDs can be strings or JSON integers; never round through floating point.
        var appId = app.ValueKind == JsonValueKind.String ? app.GetString() : app.ValueKind == JsonValueKind.Number ? app.GetRawText() : null;
        if (!FacebookOptions.NumericId(appId) || !string.Equals(appId, options.Value.AppId, StringComparison.Ordinal))
            throw InvalidDebug("app_id", "missing_or_mismatched");
        if (!data.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "USER")
            throw InvalidDebug("type", "unexpected_token_type");
        DateTimeOffset? expiry = TimestampShape(data, "expires_at") == "zero" ? null : ReadVerifiedTimestamp(data, "expires_at");
        // Preserve the existing maximum accepted lifetime; it is a rejection bound, not a fallback duration.
        if (expiry <= now) throw InvalidDebug("expires_at", "expired");
        if (expiry > now.AddDays(366)) throw InvalidDebug("expires_at", "beyond_policy");
        if (data.TryGetProperty("issued_at", out var issued) &&
            (issued.ValueKind != JsonValueKind.Number || !issued.TryGetInt64(out var issuedAt) || issuedAt < 0 || issuedAt > now.ToUnixTimeSeconds()))
            throw InvalidDebug("issued_at", "invalid_timestamp");
        // When Meta supplies a data-access deadline, never retain Page access beyond it.
        DateTimeOffset? dataExpiry = null;
        if (data.TryGetProperty("data_access_expires_at", out _))
        {
            dataExpiry = ReadVerifiedTimestamp(data, "data_access_expires_at");
            if (dataExpiry <= now) throw InvalidDebug("data_access_expires_at", "expired");
        }
        return new(expiry, dataExpiry);
    }
    private async Task DiagnoseResourcesAsync(string access, CancellationToken token)
    {
        // One bounded observation per successful exchange; no authorization decisions.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint("debug_token?input_token=" + Uri.EscapeDataString(access)));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AppId + "|" + options.Value.AppSecret);
            using var json = await SendAsync(request, deadline.Token, "debug_token", logDiagnostics: false);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count(p => p.Name == "data") != 1 || data.TryGetProperty("error", out _) ||
                data.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal).Any(g => g.Count() != 1))
                throw new JsonException();
            LogResourceGrantShape(data, diagnosticOnly: true);
            LogResourceComparisons(data);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var status = error is OperationCanceledException ? "timeout" : error is JsonException ? "invalid_response" : "request_failed";
            foreach (var permission in FacebookOptions.RequiredScopes)
            {
                LogOptionalDiagnostic(permission, null, null, null, status);
                LogExpectedPageComparison(permission, null);
            }
            LogCommonTargetComparison(null);
        }
    }
    private void LogResourceComparisons(JsonElement data)
    {
        // Local observation only. Never retain or log these sets or provider values.
        var targets = FacebookOptions.RequiredScopes.Select(permission => ReadExactTargets(data, permission)).ToArray();
        var expected = options.Value.OAuthResourceDiagnosticsExpectedPageId;
        for (var index = 0; index < targets.Length; index++)
            LogExpectedPageComparison(FacebookOptions.RequiredScopes[index],
                IsExactIdentifier(expected) && targets[index] is { } ids ? ids.Contains(expected!) : null);

        bool? common = null;
        if (targets.All(ids => ids is not null))
        {
            var intersection = new HashSet<string>(targets[0]!, StringComparer.Ordinal);
            foreach (var ids in targets.Skip(1)) intersection.IntersectWith(ids!);
            common = intersection.Count > 0;
        }
        LogCommonTargetComparison(common);
    }
    private static bool IsExactIdentifier(string? value) => !string.IsNullOrEmpty(value) && value.All(char.IsAsciiDigit);
    private static HashSet<string>? ReadExactTargets(JsonElement data, string permission)
    {
        if (!data.TryGetProperty("granular_scopes", out var granular) || granular.ValueKind != JsonValueKind.Array) return null;
        HashSet<string>? result = null;
        foreach (var entry in granular.EnumerateArray())
        {
            // Duplicate properties or scope entries are ambiguous, never last-value-wins.
            if (entry.ValueKind != JsonValueKind.Object ||
                entry.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal).Any(g => g.Count() != 1) ||
                !entry.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.String) return null;
            if (!string.Equals(scope.GetString(), permission, StringComparison.Ordinal)) continue;
            if (result is not null || !entry.TryGetProperty("target_ids", out var ids) || ids.ValueKind != JsonValueKind.Array) return null;
            result = new(StringComparer.Ordinal);
            foreach (var id in ids.EnumerateArray())
            {
                // JSON strings are decoded as strings; JSON integers retain their exact lexical digits.
                // No Trim, numeric parsing, case folding, exponent expansion, or rounding.
                var exact = id.ValueKind == JsonValueKind.String ? id.GetString()
                    : id.ValueKind == JsonValueKind.Number ? id.GetRawText() : null;
                if (!IsExactIdentifier(exact)) return null;
                result.Add(exact!);
            }
        }
        return result;
    }
    private static string ComparisonLabel(bool? value) => value is null ? "unknown" : value.Value ? "true" : "false";
    private void LogExpectedPageComparison(string permission, bool? matched) =>
        logger?.LogInformation("Facebook optional resource comparison: Permission={Permission}; ExpectedPageMatched={ExpectedPageMatched}",
            permission, ComparisonLabel(matched));
    private void LogCommonTargetComparison(bool? present) =>
        logger?.LogInformation("Facebook optional resource comparison: CommonTargetPresent={CommonTargetPresent}", ComparisonLabel(present));
    private void LogOptionalDiagnostic(string permission, bool? granted, bool? granularPresent, int? targetCount, string status)
    {
        static string Label(bool? value) => value is null ? "unknown" : value.Value ? "true" : "false";
        logger?.LogInformation("Facebook optional permission resources: Permission={Permission}; Granted={Granted}; GranularScopePresent={GranularScopePresent}; TargetIdsCount={TargetIdsCount}; DiagnosticStatus={DiagnosticStatus}",
            permission, Label(granted), Label(granularPresent), targetCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown", status);
    }
    private void LogResourceGrantShape(JsonElement data, bool diagnosticOnly = false)
    {
        // Observations only: absence of target_ids does not prove absence of granted assets.
        var present = data.TryGetProperty("granular_scopes", out var granular);
        var shape = !present ? "missing" : granular.ValueKind == JsonValueKind.Array ? "array" : "unexpected_type";
        foreach (var permission in FacebookOptions.RequiredScopes)
        {
            bool? granted = null;
            if (data.TryGetProperty("scopes", out var scopes) && scopes.ValueKind == JsonValueKind.Array &&
                scopes.EnumerateArray().All(scope => scope.ValueKind == JsonValueKind.String))
                granted = scopes.EnumerateArray().Any(scope => scope.GetString() == permission);
            var entries = 0; var targets = 0; var missingTargets = 0; var malformed = 0;
            if (present && granular.ValueKind == JsonValueKind.Array)
                foreach (var entry in granular.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.String) { malformed++; continue; }
                    if (scope.GetString() != permission) continue;
                    entries++;
                    if (!entry.TryGetProperty("target_ids", out var ids)) { missingTargets++; continue; }
                    if (ids.ValueKind != JsonValueKind.Array) { malformed++; continue; }
                    targets += ids.GetArrayLength();
                }
            if (!diagnosticOnly) logger?.LogInformation("Facebook resource grants: Stage=debug_token; GranularScopesShape={GranularScopesShape}; Permission={Permission}; MatchingEntries={MatchingEntries}; TargetEntriesCount={TargetEntriesCount}; MissingTargetsCount={MissingTargetsCount}; MalformedEntriesCount={MalformedEntriesCount}", shape, permission, entries, targets, missingTargets, malformed);
            bool? granularPresent = shape == "array" && malformed == 0 ? entries > 0 : null;
            int? targetCount = granularPresent == true && missingTargets == 0 && malformed == 0 ? targets : null;
            if (diagnosticOnly) LogOptionalDiagnostic(permission, granted, granularPresent, targetCount, "completed");
            else LogPermissionResources("debug_token", permission, granted, "debug_token_scopes", granularPresent, targetCount);
        }
    }
    private void LogPermissionResources(string stage, string permission, bool? granted, string source, bool? granularPresent, int? targetCount)
    {
        // Fixed labels and counts only. Unknown never means false or an empty target list.
        static string Label(bool? value) => value is null ? "unknown" : value.Value ? "true" : "false";
        logger?.LogInformation("Facebook permission resources: Stage={Stage}; Permission={Permission}; Granted={Granted}; GrantedSource={GrantedSource}; GranularScopePresent={GranularScopePresent}; TargetIdsCount={TargetIdsCount}",
            stage, permission, Label(granted), source, Label(granularPresent), targetCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown");
    }
    private DateTimeOffset ReadVerifiedTimestamp(JsonElement data, string field)
    {
        var shape = TimestampShape(data, field);
        if (shape != "positive_integer") throw InvalidDebug(field, shape);
        return DateTimeOffset.FromUnixTimeSeconds(data.GetProperty(field).GetInt64());
    }
    private static string TimestampShape(JsonElement data, string field)
    {
        if (!data.TryGetProperty(field, out var value)) return "missing";
        if (value.ValueKind != JsonValueKind.Number) return "unexpected_type";
        if (!value.TryGetInt64(out var seconds)) return "not_int64";
        if (seconds == 0) return "zero";
        if (seconds < 0) return "negative";
        return seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds() ? "out_of_range" : "positive_integer";
    }
    private JsonException InvalidDebug(string field, string reason, string stage = "debug_token")
    {
        // All arguments are fixed local labels. No provider field value or exception payload.
        logger?.LogWarning("Facebook OAuth invalid response: Stage={Stage}; Field={Field}; Reason={Reason}", stage, field, reason);
        return new JsonException("Facebook token expiration could not be verified.");
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
        logger?.LogInformation("Facebook permissions: Stage=permissions; HasPagesShowList={HasPagesShowList}; HasPagesManagePosts={HasPagesManagePosts}; HasPagesReadEngagement={HasPagesReadEngagement}",
            scopes.Contains("pages_show_list", StringComparer.Ordinal), scopes.Contains("pages_manage_posts", StringComparer.Ordinal), scopes.Contains("pages_read_engagement", StringComparer.Ordinal));
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
        try { return await FetchPagesAsync(access, token); }
        catch (Exception error)
        {
            logger?.LogWarning("Facebook Pages: Stage=pages; PagesFetchStatus=failed; Category={Category}; ExceptionType={ExceptionType}", FailureCategory(error, token), error.GetType().Name);
            throw;
        }
    }
    private async Task<FacebookPageCredential[]> FetchPagesAsync(string access, CancellationToken token)
    {
        var pages = new List<FacebookPageCredential>(); string? after = null;
        var returned = 0; var missingToken = 0; var unsupportedTask = 0;
        var cursors = new HashSet<string>();
        for (var count = 0; count < 50; count++)
        {
            var path = "me/accounts?fields=id,name,access_token,tasks&limit=100";
            if (after is not null) path += "&after=" + Uri.EscapeDataString(after);
            using var json = await GetAsync(path, access, token);
            foreach (var page in json.RootElement.GetProperty("data").EnumerateArray())
            {
                returned++;
                var id = page.GetProperty("id").GetString();
                var name = page.GetProperty("name").GetString();
                if (!FacebookOptions.NumericId(id) || string.IsNullOrWhiteSpace(name)) throw new FacebookAuthorizationException();
                if (!page.TryGetProperty("access_token", out var key) || string.IsNullOrWhiteSpace(key.GetString())) { missingToken++; continue; }
                var tasks = page.TryGetProperty("tasks", out var granted) ? granted.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
                var entry = new FacebookPageCredential { Id = id!, Name = name, AccessToken = key.GetString()!, Tasks = tasks };
                if (entry.CanCreate) pages.Add(entry); else unsupportedTask++;
            }
            if (!json.RootElement.TryGetProperty("paging", out var paging) || paging.ValueKind != JsonValueKind.Object || !paging.TryGetProperty("next", out _))
            {
                var result = pages.DistinctBy(x => x.Id).ToArray();
                logger?.LogInformation("Facebook Pages: Stage=pages; PagesFetchStatus=completed; PagesCount={PagesCount}; ReturnedPagesCount={ReturnedPagesCount}; MissingPageTokenCount={MissingPageTokenCount}; UnsupportedTaskCount={UnsupportedTaskCount}; BatchesCount={BatchesCount}", result.Length, returned, missingToken, unsupportedTask, count + 1);
                return result;
            }
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
    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken token, string stage, bool logDiagnostics = true)
    {
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, deadline.Token);
        JsonDocument json;
        try { json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(deadline.Token)); }
        catch (JsonException)
        {
            if (logDiagnostics) logger?.LogWarning("Facebook HTTP failure: Stage={Stage}; HttpStatus={HttpStatus}; Category=invalid_response", stage, (int)response.StatusCode);
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
            if (logDiagnostics) logger?.LogWarning("Facebook HTTP failure: Stage={Stage}; Category=provider_rejection; HttpStatus={HttpStatus}; MetaCode={MetaCode}; MetaSubcode={MetaSubcode}", stage, (int)response.StatusCode, code, subcode);
            json.Dispose();
            throw new FacebookAuthorizationException();
        }

        return json;
    }
}
