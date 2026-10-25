using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Infrastructure;

public sealed partial class FacebookTokenExpiryTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Access = "private-user+token/&?";
    private const string AppSecret = "private-app-secret";
    private const string Code = "private-code";

    [Theory]
    [InlineData("\"42\"", "42")]
    [InlineData("42", "42")]
    [InlineData("9007199254740993", "9007199254740993")]
    public async Task MissingDurationsUsesVerifiedAbsoluteExpiryAndStillChecksPermissionsAndPages(string appJson, string configuredApp)
    {
        using var harness = new Harness(configuredApp);
        harness.Handler.Data["app_id"] = JsonNode.Parse(appJson);
        var result = await harness.Graph.ExchangeAsync(Code, default);
        Assert.Equal(Now.AddHours(1), result.ExpiresAt);
        Assert.Equal(Access, result.UserToken);
        Assert.Equal(configuredApp, result.AppId);
        Assert.Equal(FacebookOptions.RequiredScopes, result.Scopes);
        Assert.Single(result.Pages);
        Assert.Equal(1, harness.Handler.DebugCalls);
        Assert.Equal(1, harness.Handler.PermissionCalls);
        Assert.Equal(1, harness.Handler.PageCalls);
        AssertSafe(harness.Log);
    }

    [Theory]
    [InlineData("expires_in", "3600")]
    [InlineData("expires", "\"3600\"")]
    public async Task SuppliedDurationNeverUsesDebugToken(string field, string duration)
    {
        using var harness = new Harness();
        harness.Handler.Extension[field] = JsonNode.Parse(duration);
        var result = await harness.Graph.ExchangeAsync(Code, default);
        Assert.Equal(Now.AddHours(1), result.ExpiresAt);
        Assert.Equal(0, harness.Handler.DebugCalls);
        Assert.Contains(harness.Log.Messages, x => x.Contains("DebugTokenStatus=not_requested; Reason=relative_expiry_present"));
        var diagnostics = harness.Log.States.Where(x => x.ContainsKey("Granted")).ToArray();
        Assert.Equal(3, diagnostics.Length);
        foreach (var permission in FacebookOptions.RequiredScopes)
        {
            var fields = Assert.Single(diagnostics, x => Equals(x["Permission"], permission));
            Assert.Equal("permissions", fields["Stage"]);
            Assert.Equal("me_permissions", fields["GrantedSource"]);
            Assert.Equal("true", fields["Granted"]);
            Assert.Equal("unknown", fields["GranularScopePresent"]);
            Assert.Equal("unknown", fields["TargetIdsCount"]);
        }
        AssertSafe(harness.Log);
    }

    [Theory]
    [InlineData("expires_in", "null")]
    [InlineData("expires", "0")]
    [InlineData("expires_in", "\"malformed\"")]
    public async Task PresentInvalidDurationDoesNotFallBack(string field, string duration)
    {
        using var harness = new Harness();
        harness.Handler.Extension[field] = JsonNode.Parse(duration);
        await Assert.ThrowsAsync<JsonException>(() => harness.Graph.ExchangeAsync(Code, default));
        Assert.Equal(0, harness.Handler.DebugCalls);
        Assert.Equal(0, harness.Handler.PermissionCalls);
    }

    [Theory]
    [InlineData("is_valid", "false")]
    [InlineData("is_valid", "\"true\"")]
    [InlineData("is_valid", null)]
    [InlineData("app_id", "\"43\"")]
    [InlineData("app_id", "42.0")]
    [InlineData("app_id", "4.2e1")]
    [InlineData("app_id", "null")]
    [InlineData("app_id", null)]
    [InlineData("expires_at", null)]
    [InlineData("expires_at", "null")]
    
    [InlineData("expires_at", "-1")]
    [InlineData("expires_at", "3600")]
    [InlineData("expires_at", "1893456000")]
    [InlineData("expires_at", "\"private-malformed\"")]
    [InlineData("expires_at", "1893459600.5")]
    [InlineData("expires_at", "true")]
    [InlineData("expires_at", "999999999999999999999999")]
    [InlineData("expires_at", "253402300800")]
    [InlineData("expires_at", "2524608000")]
    [InlineData("issued_at", "1893459601")]
    [InlineData("type", "\"PAGE\"")]
    [InlineData("data_access_expires_at", "0")]
    [InlineData("data_access_expires_at", "1893456000")]
    [InlineData("error", "{\"message\":\"private-error\"}")]
    public async Task UnverifiableMetadataIsRejectedBeforePageAccess(string field, string? json)
    {
        using var harness = new Harness();
        if (json is null) harness.Handler.Data.Remove(field); else harness.Handler.Data[field] = JsonNode.Parse(json);
        await Assert.ThrowsAsync<JsonException>(() => harness.Graph.ExchangeAsync(Code, default));
        Assert.Equal(1, harness.Handler.DebugCalls);
        Assert.Equal(0, harness.Handler.PermissionCalls);
        Assert.Equal(0, harness.Handler.PageCalls);
        Assert.Contains(harness.Log.Messages, message => message.Contains("Stage=debug_token") && message.Contains("Field=" + field));
        AssertSafe(harness.Log);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":{\"is_valid\":false,\"is_valid\":true}}")]
    public async Task InvalidEnvelopeIsRejected(string body)
    {
        using var harness = new Harness();
        harness.Handler.RawDebug = body;
        await Assert.ThrowsAsync<JsonException>(() => harness.Graph.ExchangeAsync(Code, default));
        Assert.Equal(0, harness.Handler.PermissionCalls);
        AssertSafe(harness.Log);
    }

    [Theory]
    [InlineData("network", "network_or_tls")]
    [InlineData("timeout", "timeout")]
    [InlineData("json", "invalid_response")]
    [InlineData("meta", "authorization_or_configuration")]
    public async Task DebugFailuresAreSafeAndNeverRetried(string failure, string category)
    {
        using var harness = new Harness();
        harness.Handler.Failure = failure;
        await Assert.ThrowsAnyAsync<Exception>(() => harness.Graph.ExchangeAsync(Code, default));
        Assert.Equal(1, harness.Handler.DebugCalls);
        Assert.Equal(0, harness.Handler.PermissionCalls);
        Assert.Contains(harness.Log.Messages, message => message.Contains("Stage=debug_token") && message.Contains("Category=" + category));
        AssertSafe(harness.Log);
    }

    [Fact]
    public async Task CancellationPropagatesToDebugRequest()
    {
        using var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        harness.Handler.Cancel = cancellation;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Graph.ExchangeAsync(Code, cancellation.Token));
        Assert.Equal(0, harness.Handler.PermissionCalls);
        Assert.Contains(harness.Log.Messages, message => message.Contains("Category=cancelled"));
        AssertSafe(harness.Log);
    }

    [Fact]
    public async Task DataAccessExpiryBoundsConnectionAndMissingPermissionsStillFail()
    {
        using var harness = new Harness();
        harness.Handler.Data["data_access_expires_at"] = Now.AddMinutes(30).ToUnixTimeSeconds();
        var connection = await harness.Graph.ExchangeAsync(Code, default);
        Assert.Equal(Now.AddMinutes(30), connection.DataAccessExpiresAt);
        Assert.Equal(Now.AddHours(1), connection.ExpiresAt);
        harness.Handler.MissingPermissions = true;
        await Assert.ThrowsAsync<FacebookAuthorizationException>(() => harness.Graph.ExchangeAsync(Code, default));
        Assert.Equal(1, harness.Handler.PageCalls);
    }

    [Fact]
    public async Task ExpiryDuringPageLookupCannotReturnUsableConnection()
    {
        using var harness = new Harness();
        harness.Handler.ExpireOnPages = true;
        await Assert.ThrowsAsync<JsonException>(() => harness.Graph.ExchangeAsync(Code, default));
        Assert.Contains(harness.Log.Messages, message => message.Contains("expired_before_storage"));
    }

    [Theory]
    [InlineData(null, "missing")]
    [InlineData("0", "zero")]
    [InlineData("null", "unexpected_type")]
    [InlineData("\"1893459600\"", "unexpected_type")]
    [InlineData("true", "unexpected_type")]
    [InlineData("{}", "unexpected_type")]
    [InlineData("1893459600.5", "not_int64")]
    [InlineData("1e9", "not_int64")]
    [InlineData("999999999999999999999999", "not_int64")]
    [InlineData("-1", "negative")]
    [InlineData("253402300800", "out_of_range")]
    [InlineData("1893456000", "expired")]
    [InlineData("3600", "expired")]
    [InlineData("2524608000", "beyond_policy")]
    [InlineData("1893459600", "valid")]
    public async Task ExpiryDiagnosticsDistinguishShapesWithoutChangingAcceptance(string? json, string expected)
    {
        using var harness = new Harness();
        if (json is null) harness.Handler.Data.Remove("expires_at"); else harness.Handler.Data["expires_at"] = JsonNode.Parse(json);
        if (expected == "zero") Assert.Null((await harness.Graph.ExchangeAsync(Code, default)).ExpiresAt);
        else if (expected == "valid") Assert.Equal(Now.AddHours(1), (await harness.Graph.ExchangeAsync(Code, default)).ExpiresAt);
        else
        {
            await Assert.ThrowsAsync<JsonException>(() => harness.Graph.ExchangeAsync(Code, default));
            Assert.Equal(0, harness.Handler.PermissionCalls);
            Assert.Contains(harness.Log.Messages, x => x.Contains("Field=expires_at; Reason=" + expected));
        }
        Assert.Contains(harness.Log.Messages, x => x.Contains("ExpiryState=" + expected));
        AssertSafe(harness.Log);
    }

    [Theory]
    [InlineData("type", "\"USER\"", "TokenType=USER", true)]
    [InlineData("type", null, "TokenType=missing", false)]
    [InlineData("type", "\"PAGE\"", "TokenType=PAGE", false)]
    [InlineData("type", "\"private-type\"", "TokenType=other", false)]
    [InlineData("type", "42", "TokenType=unexpected_type", false)]
    [InlineData("is_valid", "false", "Validity=false", false)]
    [InlineData("is_valid", "\"private-value\"", "Validity=unexpected_type", false)]
    [InlineData("is_valid", null, "Validity=missing", false)]
    [InlineData("app_id", "\"43\"", "AppMatch=mismatch", false)]
    [InlineData("app_id", "\"private-id\"", "AppMatch=invalid_format", false)]
    [InlineData("app_id", null, "AppMatch=missing", false)]
    public async Task MetadataDiagnosticsContainOnlyAllowlistedLabels(string field, string? json, string expected, bool accepted)
    {
        using var harness = new Harness();
        if (json is null) harness.Handler.Data.Remove(field); else harness.Handler.Data[field] = JsonNode.Parse(json);
        if (accepted) await harness.Graph.ExchangeAsync(Code, default);
        else await Assert.ThrowsAsync<JsonException>(() => harness.Graph.ExchangeAsync(Code, default));
        Assert.Contains(harness.Log.Messages, x => x.Contains(expected));
        AssertSafe(harness.Log);
    }

    [Theory]
    [InlineData("{\"data\":[]}", 0, 0, 0, 0)]
    [InlineData("{\"data\":[{\"id\":\"123\",\"name\":\"private-name\"}]}", 1, 0, 1, 0)]
    [InlineData("{\"data\":[{\"id\":\"123\",\"name\":\"private-name\",\"access_token\":\"private-page\",\"tasks\":[\"ANALYZE\"]}]}", 1, 0, 0, 1)]
    [InlineData("{\"data\":[{\"id\":\"123\",\"name\":\"private-name\",\"access_token\":\"private-page\",\"tasks\":[\"CREATE_CONTENT\"]}]}", 1, 1, 0, 0)]
    public async Task PageDiagnosticsDistinguishEmptyAndFilteredResults(string body, int returned, int eligible, int missingToken, int task)
    {
        using var harness = new Harness();
        harness.Handler.RawPages = body;
        var connection = await harness.Graph.ExchangeAsync(Code, default);
        Assert.Equal(eligible, connection.Pages.Length);
        var output = string.Join('\n', harness.Log.Messages);
        Assert.Contains("PagesFetchStatus=completed; PagesCount=" + eligible + "; ReturnedPagesCount=" + returned, output);
        Assert.Contains("MissingPageTokenCount=" + missingToken + "; UnsupportedTaskCount=" + task, output);
        Assert.Contains("HasPagesShowList=True; HasPagesManagePosts=True", output);
        AssertSafe(harness.Log);
    }
    [Theory]
    [InlineData(null, "missing", 0, 0, 0)]
    [InlineData("[]", "array", 0, 0, 0)]
    [InlineData("{}", "unexpected_type", 0, 0, 0)]
    [InlineData("[{\"scope\":\"pages_show_list\",\"target_ids\":[]}]", "array", 1, 0, 0)]
    [InlineData("[{\"scope\":\"pages_show_list\",\"target_ids\":[\"private-resource\"]}]", "array", 1, 1, 0)]
    [InlineData("[{\"scope\":\"pages_show_list\"}]", "array", 1, 0, 1)]
    public async Task ResourceGrantDiagnosticsAreSafeObservationsNotAuthorizationRules(string? json, string shape, int entries, int targets, int missing)
    {
        using var harness = new Harness();
        if (json is not null) harness.Handler.Data["granular_scopes"] = JsonNode.Parse(json);
        harness.Handler.RawPages = "{\"data\":[]}";
        Assert.Empty((await harness.Graph.ExchangeAsync(Code, default)).Pages);
        Assert.Contains(harness.Log.Messages, x => x.Contains($"GranularScopesShape={shape}; Permission=pages_show_list; MatchingEntries={entries}; TargetEntriesCount={targets}; MissingTargetsCount={missing}"));
        AssertSafe(harness.Log);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"error\":{\"code\":190,\"message\":\"private-error\"}}")]
    public async Task InvalidAccountsResponseCannotBeReportedAsCompletedEmpty(string body)
    {
        using var harness = new Harness();
        harness.Handler.RawPages = body;
        await Assert.ThrowsAnyAsync<Exception>(() => harness.Graph.ExchangeAsync(Code, default));
        Assert.Contains(harness.Log.Messages, x => x.Contains("PagesFetchStatus=failed"));
        Assert.DoesNotContain(harness.Log.Messages, x => x.Contains("PagesFetchStatus=completed"));
        AssertSafe(harness.Log);
    }

    [Fact]
    public async Task EmptyFirstBatchStillFollowsCursorWithSameUserToken()
    {
        using var harness = new Harness();
        harness.Handler.EmptyFirstBatch = true;
        Assert.Single((await harness.Graph.ExchangeAsync(Code, default)).Pages);
        Assert.Equal(2, harness.Handler.PageCalls);
        Assert.Contains(harness.Log.Messages, x => x.Contains("ReturnedPagesCount=1") && x.Contains("BatchesCount=2"));
        AssertSafe(harness.Log);
    }
    [Theory]
    [InlineData(null, true)]
    [InlineData("987654", true)]
    [InlineData("", false)]
    [InlineData("not-a-config", false)]
    [InlineData("123&scope=other", false)]
    public void BusinessConfigurationMustBeNumericOrAbsent(string? config, bool valid)
    {
        var options = new FacebookOptions { AppId = "42", AppSecret = AppSecret, LoginConfigId = config };
        Assert.Equal(valid, options.Configured);
    }
    [Theory]
    [InlineData("pages_show_list")]
    [InlineData("pages_manage_posts")]
    [InlineData("pages_read_engagement")]
    public async Task PermissionResourceDiagnosticsUseExactScopeAndOnlyCounts(string permission)
    {
        using var harness = new Harness();
        harness.Handler.Data["scopes"] = JsonSerializer.SerializeToNode(FacebookOptions.RequiredScopes);
        harness.Handler.Data["granular_scopes"] = JsonNode.Parse("[{\"scope\":\"" + permission + "\",\"target_ids\":[\"private-resource-one\",\"private-resource-two\"]}]");
        harness.Handler.RawPages = "{\"data\":[]}";
        Assert.Empty((await harness.Graph.ExchangeAsync(Code, default)).Pages);
        var fields = Assert.Single(harness.Log.States, x => x.ContainsKey("Granted") && Equals(x["Permission"], permission));
        Assert.Equal("true", fields["Granted"]);
        Assert.Equal("true", fields["GranularScopePresent"]);
        Assert.Equal("2", fields["TargetIdsCount"]);
        AssertSafe(harness.Log);
    }
    [Theory]
    [InlineData(null, null)]
    [InlineData("[]", false)]
    [InlineData("[\"pages_show_list_extra\"]", false)]
    [InlineData("[42]", null)]
    public async Task MissingDiagnosticMetadataIsUnknownNotDenied(string? scopesJson, bool? granted)
    {
        using var harness = new Harness();
        if (scopesJson is not null) harness.Handler.Data["scopes"] = JsonNode.Parse(scopesJson);
        await harness.Graph.ExchangeAsync(Code, default);
        var fields = Assert.Single(harness.Log.States, x => x.ContainsKey("Granted") && Equals(x["Permission"], "pages_show_list"));
        Assert.Equal(granted is null ? "unknown" : granted.Value ? "true" : "false", fields["Granted"]);
        Assert.Equal("unknown", fields["GranularScopePresent"]);
        Assert.Equal("unknown", fields["TargetIdsCount"]);
        AssertSafe(harness.Log);
    }
    [Theory]
    [InlineData(false, "Development", 0)]
    [InlineData(true, "Production", 0)]
    [InlineData(true, "Development", 1)]
    public async Task OptionalInspectionRequiresExplicitDevelopmentActivation(bool enabled, string environment, int calls)
    {
        using var harness = new Harness(diagnostics: enabled, environment: environment);
        harness.Handler.Extension["expires_in"] = 3600;
        var result = await harness.Graph.ExchangeAsync("private-code", default);
        Assert.Equal(calls, harness.Handler.DebugCalls);
        Assert.Equal(Now.AddHours(1), result.ExpiresAt);
        var rows = harness.Log.States.Where(s => s.ContainsKey("DiagnosticStatus")).ToArray();
        Assert.Equal(calls * 3, rows.Length);
        Assert.Equal(calls, harness.Log.States.Count(s => s.ContainsKey("CommonTargetPresent")));
        Assert.Equal(calls * 3, harness.Log.States.Count(s => s.ContainsKey("ExpectedPageMatched")));
        foreach (var row in rows)
        {
            Assert.Equal("completed", row["DiagnosticStatus"]);
            Assert.Equal("unknown", row["Granted"]);
            Assert.Equal("unknown", row["GranularScopePresent"]);
            Assert.Equal("unknown", row["TargetIdsCount"]);
            Assert.Equal(6, row.Count); // Five allowlisted fields plus logging's OriginalFormat.
        }
        AssertSafe(harness.Log);
    }
    [Fact]
    public async Task OptionalInspectionReportsExactPermissionsAndOnlyTargetCounts()
    {
        using var harness = new Harness(diagnostics: true);
        harness.Handler.Extension["expires_in"] = 3600;
        harness.Handler.Data["scopes"] = new JsonArray("pages_show_list", "pages_manage_posts_suffix");
        harness.Handler.Data["granular_scopes"] = JsonNode.Parse("""
            [{"scope":"pages_show_list","target_ids":["private-resource"]},
             {"scope":"pages_manage_posts","target_ids":[]},
             {"scope":"pages_read_engagement"}]
            """);
        await harness.Graph.ExchangeAsync("private-code", default);
        var rows = harness.Log.States.Where(s => s.ContainsKey("DiagnosticStatus")).ToDictionary(s => (string)s["Permission"]!);
        Assert.Equal("true", rows["pages_show_list"]["Granted"]);
        Assert.Equal("1", rows["pages_show_list"]["TargetIdsCount"]);
        Assert.Equal("false", rows["pages_manage_posts"]["Granted"]);
        Assert.Equal("0", rows["pages_manage_posts"]["TargetIdsCount"]);
        Assert.Equal("unknown", rows["pages_read_engagement"]["TargetIdsCount"]);
        Assert.All(rows.Values, row => Assert.Equal("true", row["GranularScopePresent"]));
        AssertSafe(harness.Log);
    }
    [Theory]
    [InlineData("network", "request_failed")]
    [InlineData("timeout", "timeout")]
    [InlineData("json", "invalid_response")]
    [InlineData("meta", "request_failed")]
    [InlineData("envelope", "invalid_response")]
    public async Task OptionalInspectionFailureDoesNotFailConnection(string failure, string status)
    {
        using var harness = new Harness(diagnostics: true);
        harness.Handler.Extension["expires_in"] = 3600;
        harness.Handler.Failure = failure;
        if (failure == "envelope") harness.Handler.RawDebug = "{\"data\":[]}";
        var result = await harness.Graph.ExchangeAsync("private-code", default);
        Assert.Single(result.Pages);
        Assert.Equal(1, harness.Handler.DebugCalls);
        var rows = harness.Log.States.Where(s => s.ContainsKey("DiagnosticStatus")).ToArray();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => { Assert.Equal(status, row["DiagnosticStatus"]); Assert.Equal("unknown", row["Granted"]); });
        Assert.DoesNotContain(harness.Log.Messages, m => m.Contains("Facebook HTTP failure"));
        Assert.Equal("unknown", Assert.Single(harness.Log.States, s => s.ContainsKey("CommonTargetPresent"))["CommonTargetPresent"]);
        Assert.All(harness.Log.States.Where(s => s.ContainsKey("ExpectedPageMatched")), s => Assert.Equal("unknown", s["ExpectedPageMatched"]));
        AssertSafe(harness.Log);
    }
    [Fact]
    public async Task OptionalInspectionPreservesCallerCancellation()
    {
        using var harness = new Harness(diagnostics: true);
        using var cancellation = new CancellationTokenSource();
        harness.Handler.Extension["expires_in"] = 3600;
        harness.Handler.Cancel = cancellation;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Graph.ExchangeAsync("private-code", cancellation.Token));
        AssertSafe(harness.Log);
    }
    private static void AssertSafe(Log log)
    {
        var output = string.Join('\n', log.Messages);
        Assert.DoesNotContain("private-", output);
        Assert.DoesNotContain("input_token", output);
        Assert.DoesNotContain("https://", output);
        Assert.DoesNotContain("Authorization:", output);
        Assert.DoesNotContain("Bearer ", output);
    }
    private sealed class Harness : IDisposable
    {
        public Log Log { get; } = new();
        public Handler Handler { get; }
        private readonly HttpClient http;
        public FacebookGraphClient Graph { get; }
        public Harness(string app = "42", bool diagnostics = false, string environment = "Development", string? expectedPage = null)
        {
            var clock = new Clock(); Handler = new Handler(clock, app); http = new HttpClient(Handler);
            Graph = new FacebookGraphClient(http, Options.Create(new FacebookOptions { AppId = app, AppSecret = AppSecret, OAuthResourceDiagnosticsEnabled = diagnostics, OAuthResourceDiagnosticsExpectedPageId = expectedPage }), clock, Log, new HostEnvironment { EnvironmentName = environment });
        }
        public void Dispose() { http.Dispose(); Handler.Dispose(); }
    }
    private sealed class HostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Current = Now;
        public override DateTimeOffset GetUtcNow() => Current;
    }
    private sealed class Handler(Clock clock, string app) : HttpMessageHandler
    {
        public JsonObject Extension { get; } = new() { ["access_token"] = Access };
        public JsonObject Data { get; } = new() { ["type"] = "USER", ["is_valid"] = true, ["app_id"] = "42", ["expires_at"] = Now.AddHours(1).ToUnixTimeSeconds() };
        public int DebugCalls, PermissionCalls, PageCalls, ExchangeCalls;
        public string? Failure, RawDebug, RawPages;
        public bool MissingPermissions, ExpireOnPages, EmptyFirstBatch;
        public CancellationTokenSource? Cancel;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("graph.facebook.com", request.RequestUri.Host);
            Assert.StartsWith("/v26.0/", request.RequestUri.AbsolutePath);
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("oauth/access_token")) return Json(++ExchangeCalls == 1 ? "{\"access_token\":\"private-intermediate-user\"}" : Extension.ToJsonString());
            if (path.EndsWith("debug_token"))
            {
                DebugCalls++;
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("?input_token=" + Uri.EscapeDataString(Access), request.RequestUri.Query);
                Assert.DoesNotContain(AppSecret, request.RequestUri.ToString());
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.Equal(app + "|" + AppSecret, request.Headers.Authorization.Parameter);
                Cancel?.Cancel(); token.ThrowIfCancellationRequested();
                if (Failure == "network") throw new HttpRequestException("private-network-url " + request.RequestUri);
                if (Failure == "timeout") throw new TaskCanceledException("private-timeout");
                if (Failure == "json") return Json("private-invalid-json");
                if (Failure == "meta") return Json("{\"error\":{\"code\":190,\"message\":\"private-body\"}}", HttpStatusCode.BadRequest);
                return Json(RawDebug ?? new JsonObject { ["data"] = Data.DeepClone() }.ToJsonString());
            }
            if (path.EndsWith("permissions"))
            {
                Assert.Equal(Access, request.Headers.Authorization!.Parameter);
                PermissionCalls++;
                return Json(JsonSerializer.Serialize(new { data = (MissingPermissions ? [] : FacebookOptions.RequiredScopes).Select(p => new { permission = p, status = "granted" }) }));
            }
            Assert.EndsWith("accounts", path);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(Access, request.Headers.Authorization.Parameter);
            Assert.Contains("fields=id,name,access_token,tasks&limit=100", request.RequestUri.Query);
            Assert.Contains("appsecret_proof=", request.RequestUri.Query);
            PageCalls++;
            if (EmptyFirstBatch && PageCalls == 1) return Json("{\"data\":[],\"paging\":{\"next\":\"https://private-host/never-follow\",\"cursors\":{\"after\":\"private-cursor\"}}}");
            if (EmptyFirstBatch && PageCalls == 2) Assert.Contains("after=private-cursor", request.RequestUri.Query);
            if (RawPages is not null) return Json(RawPages);
            if (ExpireOnPages) clock.Current = Now.AddHours(1);
            return Json("{\"data\":[{\"id\":\"123\",\"name\":\"Test Page\",\"access_token\":\"private-page\",\"tasks\":[\"CREATE_CONTENT\"]}]}");
        }
        private static Task<HttpResponseMessage> Json(string body, HttpStatusCode status = HttpStatusCode.OK)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
    private sealed class Log : ILogger<FacebookGraphClient>
    {
        public List<string> Messages { get; } = [];
        public List<Dictionary<string, object?>> States { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception); Messages.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> fields) States.Add(fields.ToDictionary(x => x.Key, x => x.Value));
        }
    }
}
