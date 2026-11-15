using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Entities;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Redeemer.SocialFlow.Infrastructure.Social;

public sealed class FacebookWorkspaceOAuthProvider(HttpClient http, IConfiguration config, TimeProvider clock)
	: MetaWorkspaceOAuthProvider(http, config, clock), IDiagnosticWorkspaceOAuthProvider, IPublishingCapabilityValidator
{
	private DiscoveryTrace? activeTrace;
	private PublishingCapabilityEvidence Evidence(PublishingCapabilityStatus status, string reason, string source, string? task = null) =>
		new(status, reason, source, Now, task);
	private PublishingCapabilityEvidence Capability(string[] scopes, JsonElement? account, string source)
	{
		if (!scopes.Contains("pages_manage_posts") || !scopes.Contains("pages_read_engagement"))
			return Evidence(PublishingCapabilityStatus.Denied, "PublishingScopeMissing", source);
		if (account is not { ValueKind: JsonValueKind.Object } page ||
			!page.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array ||
			tasks.EnumerateArray().Any(t => t.ValueKind != JsonValueKind.String))
			return Evidence(PublishingCapabilityStatus.Unverified, "PageTaskEvidenceUnavailable", source);
		var create = tasks.EnumerateArray().Select(t => t.GetString()).FirstOrDefault(t => t is "CREATE_CONTENT" or "PROFILE_PLUS_CREATE_CONTENT");
		return create is null ? Evidence(PublishingCapabilityStatus.Denied, "PageCreateContentTaskMissing", source) :
			Evidence(PublishingCapabilityStatus.Verified, "PageCreateContentTaskVerified", source, create);
	}
	protected override SocialCandidate PageCandidate(string id, string name, SocialCredential credential, JsonElement page,
		string userAccess, DateTimeOffset? userExpiry)
	{
		if (credential.AccessToken == userAccess) throw Unauthorized();
		var evidence = Capability(credential.Scopes, page, "MetaUserAccounts");
		return new(id, name, "Page", credential with { FacebookVerification = new(userAccess, userExpiry) },
			evidence.Status == PublishingCapabilityStatus.Verified, evidence);
	}
	private static string? NumericId(string? value) => value is { Length: > 0 and <= 32 } && value.All(c => c is >= '0' and <= '9') ? value : null;
	// Observations only; missing/invalid targets do not prove that a Page is unauthorized.
	private static FacebookGranularDiagnostic Granular(JsonElement data)
	{
		var fields = data.EnumerateObject().Where(p => p.NameEquals("granular_scopes")).ToArray();
		var shape = fields.Length == 0 ? "missing" : fields.Length > 1 ? "ambiguous" :
			fields[0].Value.ValueKind != JsonValueKind.Array ? "invalid" : "array";
		var entries = shape == "array" ? fields[0].Value.EnumerateArray().ToArray() : [];
		var malformed = entries.Any(e => e.ValueKind != JsonValueKind.Object ||
			e.EnumerateObject().Count(p => p.NameEquals("scope")) != 1 ||
			!e.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.String);
		var permissions = new List<FacebookGranularPermission>();
		foreach (var permission in new[] { "pages_show_list", "pages_read_engagement", "pages_manage_posts" })
		{
			var matches = entries.Where(e => e.ValueKind == JsonValueKind.Object && Text(e, "scope") == permission).ToArray();
			var status = shape != "array" ? shape : malformed ? "ambiguous" : matches.Length == 0 ? "missing" : matches.Length > 1 ? "ambiguous" : "array";
			var ids = new List<string>();
			if (status == "array")
			{
				var targets = matches[0].EnumerateObject().Where(p => p.NameEquals("target_ids")).ToArray();
				status = targets.Length == 0 ? "missing" : targets.Length > 1 ? "ambiguous" :
					targets[0].Value.ValueKind != JsonValueKind.Array ? "invalid" : "array";
				if (status == "array")
				{
					foreach (var target in targets[0].Value.EnumerateArray())
					{
						var id = NumericId(target.ValueKind == JsonValueKind.String ? target.GetString() : target.ValueKind == JsonValueKind.Number ? target.GetRawText() : null);
						if (id is null) { status = "invalid"; break; }
						if (!ids.Contains(id, StringComparer.Ordinal)) ids.Add(id);
						if (ids.Count > 500) { status = "too_many"; break; }
					}
					if (status == "array" && ids.Count == 0) status = "empty_array";
				}
			}
			permissions.Add(new(permission, status, status is "array" or "empty_array" ? ids.ToArray() : []));
		}
		return new(shape, permissions);
	}
	protected override void ObserveResponse(int status, JsonElement? root)
	{
		if (activeTrace is { Facebook: { } evidence } trace)
		{
			var shape = root is null ? "invalid_json" : "missing";
			JsonElement error = default;
			var graphError = false;
			if (root is { ValueKind: JsonValueKind.Object } value)
			{
				if (value.TryGetProperty("data", out var data)) shape = data.ValueKind == JsonValueKind.Array
					? data.GetArrayLength() == 0 ? "empty_array" : "array" : data.ValueKind.ToString().ToLowerInvariant();
				graphError = value.TryGetProperty("error", out error) && error.ValueKind == JsonValueKind.Object;
			}
			else if (root is not null) shape = "non_object_root";
			var response = new FacebookResponseDiagnostic(trace.Stage, status, shape, graphError,
				graphError ? ErrorNumber(error, "code") : null, graphError ? ErrorNumber(error, "error_subcode") : null);
			trace.Facebook = evidence with { Responses = [.. evidence.Responses ?? [], response] };
			if (graphError && status is >= 200 and < 300) trace.Failure = "graph_error";
		}
		base.ObserveResponse(status, root);
	}
	protected override void ObservePermissions(string[] granted)
	{
		if (activeTrace?.Facebook is { } evidence) activeTrace.Facebook = evidence with
		{
			PagesShowList = granted.Contains("pages_show_list"),
			PagesReadEngagement = granted.Contains("pages_read_engagement"),
			PagesManagePosts = granted.Contains("pages_manage_posts")
		};
	}
	public async Task<SocialDiscoveryResult> DiscoverWithDiagnosticsAsync(string code, CancellationToken ct)
	{
		var trace = new DiscoveryTrace { Facebook = new(NumericId(ClientId), NumericId(LoginConfigId)) };
		activeTrace = trace;
		try
		{
			var candidates = await DiscoverPages(code, ct, trace);
			var outcome = trace.Received == 0 ? "no_pages" : trace.Retained == 0 ? "pages_filtered" :
				trace.WithoutPublishing == trace.Retained ? "pages_without_publishing" : "pages_available";
			return new(candidates, trace.Snapshot(outcome), true);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
		catch (Exception error)
		{
			var status = (error as SocialConnectionException)?.ProviderHttpStatus;
			var outcome = trace.Failure ?? (status is not null ? "http_error" :
				error is HttpRequestException or OperationCanceledException ? "network_error" :
				error is SocialConnectionException { Status: 401 } ? "authorization_invalid" : "invalid_response");
			// No partial candidates survive a failed discovery; counts describe progress before failure.
			return new([], trace.Snapshot(outcome, status), false);
		}
		finally { activeTrace = null; }
	}
	public override SocialPlatform Platform => SocialPlatform.Facebook;
	private bool LegacyCredentials => Settings.GetValue<bool>("UseLegacyClientCredentials") &&
		Settings["ClientId"] is null && Settings["ClientSecret"] is null;
	protected override string ClientId => LegacyCredentials ? Configuration["Facebook:AppId"] ?? "" : base.ClientId;
	protected override string ClientSecret => LegacyCredentials ? Configuration["Facebook:AppSecret"] ?? "" : base.ClientSecret;
	private string? LoginConfigId => Settings["LoginConfigId"] ??
		(Settings.GetValue<bool>("UseLegacyLoginConfigId") ? Configuration["Facebook:LoginConfigId"] : null);

	// Availability is configuration readiness, never evidence of a user's grant.
	public override bool Available => ConfigurationProblem is null;
	private string? ConfigurationProblem =>
		!Settings.GetValue<bool>("Enabled") ? "Facebook est désactivé côté serveur (SocialConnections:Providers:Facebook:Enabled)." :
		string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret) ? "Identifiants Facebook SaaS manquants : configurez la paire ClientId/ClientSecret ou la référence explicite UseLegacyClientCredentials." :
		!Regex.IsMatch(Version, "^v[0-9]+\\.0$") ? "Version API Facebook manquante ou invalide (ApiVersion)." :
		!Uri.TryCreate(Callback, UriKind.Absolute, out var uri) || uri.Scheme != "https" ? "Origine HTTPS du callback Facebook invalide (Authentication:PublicOrigin)." :
		LoginConfigId is not null && !Regex.IsMatch(LoginConfigId, "^[0-9]+$") ? "LoginConfigId Facebook invalide : utilisez une configuration de jeton utilisateur." :
		Settings.GetValue<bool>("UseLegacyLoginConfigId") && LoginConfigId is null ? "La référence Facebook:LoginConfigId est absente." :
		LoginConfigId is null && !Scopes.Contains("pages_show_list") ? "La découverte des Pages nécessite le scope pages_show_list ou un LoginConfigId configuré pour les Pages." : null;
	public string ConfigurationMessage => ConfigurationProblem ??
		"Configuration Facebook disponible. Les Pages seront proposées selon les droits réellement accordés. La publication nécessite pages_manage_posts, pages_read_engagement et une tâche CREATE_CONTENT ou PROFILE_PLUS_CREATE_CONTENT confirmée par Meta ; elle n’est pas activée par la connexion.";

	public override string AuthorizationUrl(string state, string? challenge)
	{
		var parameters = new Dictionary<string, string?>
		{
			["client_id"] = ClientId,
			["redirect_uri"] = Callback,
			["response_type"] = "code",
			["state"] = state
		};
		if (LoginConfigId is { } id) parameters["config_id"] = id;
		else parameters["scope"] = string.Join(',', Scopes);
		return QueryHelpers.AddQueryString("https://www.facebook.com/" + Version + "/dialog/oauth", parameters);
	}

	private static DateTimeOffset? Earlier(DateTimeOffset? a, DateTimeOffset? b) =>
		a is null ? b : b is null ? a : a < b ? a : b;

	private DateTimeOffset? Deadline(JsonElement data, string field, bool required)
	{
		if (!data.TryGetProperty(field, out var value)) { if (required) throw Failure(); return null; }
		if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var seconds) || seconds < 0) throw Failure();
		// Zero does not supply a deadline. It is accepted only after online is_valid/app/type checks.
		if (seconds == 0) return null;
		var deadline = DateTimeOffset.FromUnixTimeSeconds(seconds);
		if (deadline <= Now) throw Unauthorized();
		return deadline;
	}

	private async Task<(DateTimeOffset? Expiry, string[] Scopes, FacebookGranularDiagnostic Granular)> Inspect(string token, string type, CancellationToken ct)
	{
		// This dedicated HttpClient has HTTP logging disabled. Neither token nor app secret enters a DTO.
		using var response = await Get(Endpoint("debug_token?input_token=" + Uri.EscapeDataString(token)), ClientId + "|" + ClientSecret, ct);
		var data = response.RootElement.GetProperty("data");
		if (type == "USER" && activeTrace?.Facebook is { } evidence)
		{
			var actualType = Text(data, "type");
			activeTrace.Facebook = evidence with
			{
				TokenType = actualType is "USER" or "PAGE" or "SYSTEM_USER" or "APP" ? actualType : "unknown",
				AppIdMatches = Text(data, "app_id") == ClientId
			};
		}
		if (!data.TryGetProperty("is_valid", out var valid) || valid.ValueKind != JsonValueKind.True ||
			Text(data, "app_id") != ClientId || Text(data, "type") != type) throw Unauthorized();
		var expiry = Earlier(Deadline(data, "expires_at", true), Deadline(data, "data_access_expires_at", false));
		// Capture only the validated final USER token; PAGE inspections must not overwrite it.
		if (type == "USER" && activeTrace?.Facebook is { } validatedEvidence)
			activeTrace.Facebook = validatedEvidence with { Granular = Granular(data) };
		var scopes = data.TryGetProperty("scopes", out var grants) && grants.ValueKind == JsonValueKind.Array
			? grants.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : [];
		return (expiry, scopes, Granular(data));
	}

	protected override async Task<DateTimeOffset?> UserExpiry(JsonElement token, string access, CancellationToken ct)
	{
		var verified = await Inspect(access, "USER", ct);
		// Relative expiry is additional evidence, not a substitute for validity/data-access checks.
		if (!token.TryGetProperty("expires_in", out var relative)) return verified.Expiry;
		if (relative.ValueKind != JsonValueKind.Number || !relative.TryGetInt64(out var seconds) || seconds < 0) throw Failure();
		return seconds == 0 ? verified.Expiry : Earlier(Expiry(token), verified.Expiry);
	}

	protected override async Task<SocialCredential> PageCredential(string id, string token, DateTimeOffset? expiry, string[] scopes, CancellationToken ct)
	{
		var verified = await Inspect(token, "PAGE", ct);
		using var page = await Graph(Uri.EscapeDataString(id) + "?fields=id", token, ct);
		if (Text(page.RootElement, "id") != id) throw Unauthorized();
		return new(token, null, Earlier(expiry, verified.Expiry), null, scopes.Intersect(verified.Scopes).ToArray());
	}

	protected override async Task<IReadOnlyList<SocialCandidate>> DiscoverAuthorizedPages(string access, DateTimeOffset? expiry,
		string[] granted, DiscoveryTrace? trace, CancellationToken ct)
	{
		if (trace is not null) trace.Stage = "authorized_pages_validation";
		// Read a validated USER inspection, never use diagnostics or configuration as authority.
		var user = await Inspect(access, "USER", ct);
		var ids = user.Granular.Permissions
			.Where(p => p.TargetsStatus == "array" && granted.Contains(p.Permission) && user.Scopes.Contains(p.Permission))
			.SelectMany(p => p.TargetIds).Distinct(StringComparer.Ordinal).Take(501).ToArray();
		if (ids.Length > 500) throw Failure();
		var result = new List<SocialCandidate>();
		foreach (var id in ids)
		{
			if (trace is not null) { trace.Stage = "authorized_page"; trace.Requests++; }
			using var page = await Graph(id + "?fields=id,name,access_token", access, ct);
			if (Text(page.RootElement, "id") != id) throw Unauthorized();
			if (trace is not null) { trace.Received++; trace.Receive(id); }
			var token = Text(page.RootElement, "access_token");
			if (string.IsNullOrWhiteSpace(token)) { trace?.Exclude("missing_page_token"); continue; }
			if (token == access) throw Unauthorized();
			var name = Text(page.RootElement, "name");
			if (name.Length > 256) throw Failure();
			if (trace is not null) trace.Stage = "authorized_page_validation";
			var credential = await PageCredential(id, token, Earlier(expiry, user.Expiry), granted, ct);
			// Reading /{id} does not bind the token to this Page: verify its own identity.
			using var identity = await Graph("me?fields=id", token, ct);
			if (Text(identity.RootElement, "id") != id) throw Unauthorized();
			// debug_token establishes PAGE type/app/validity/scopes, but not CREATE_CONTENT.
			// The Page node has no tasks field. Without authoritative task evidence, publishing
			// stays blocked even with pages_manage_posts and pages_read_engagement.
			var evidence = Capability(credential.Scopes, null, "MetaGranularDiscovery");
			result.Add(new(id, name.Length == 0 ? id : name, "Page",
				credential with { FacebookVerification = new(access, Earlier(expiry, user.Expiry)) }, false, evidence));
			if (trace is not null)
			{
				trace.Retained++; trace.WithoutPublishing++;
				trace.PageOutcome("retained_publishing_unverified");
			}
		}
		return result;
	}

	protected override async Task<SocialCredential> Validate(string id, string type, SocialCredential credential, CancellationToken ct)
	{
		if (type != "Page" || credential.ExpiresAtUtc <= Now) throw Unauthorized();
		var verified = await Inspect(credential.AccessToken, "PAGE", ct);
		using var page = await Graph("me?fields=id", credential.AccessToken, ct);
		if (Text(page.RootElement, "id") != id) throw Unauthorized();
		return credential with { ExpiresAtUtc = Earlier(credential.ExpiresAtUtc, verified.Expiry), Scopes = verified.Scopes };
	}

	public async Task<PublishingCapabilityValidation> ValidatePublishingCapabilityAsync(string id, string accountType,
		SocialCredential credential, CancellationToken ct)
	{
		try
		{
			var current = await ValidateAsync(id, accountType, credential, ct);
			if (current.FacebookVerification is not { } context)
				return new(current, Capability(current.Scopes, null, "MetaPageTokenInspection"));
			if (context.ExpiresAtUtc <= Now) throw Unauthorized();
			var user = await Inspect(context.UserAccessToken, "USER", ct);
			using var permissions = await Graph("me/permissions", context.UserAccessToken, ct);
			var grants = permissions.RootElement.GetProperty("data").EnumerateArray()
				.Where(p => Text(p, "status") == "granted").Select(p => Text(p, "permission"));
			var scopes = current.Scopes.Intersect(user.Scopes).Intersect(grants).ToArray();
			current = current with { Scopes = scopes, FacebookVerification = context with { ExpiresAtUtc = Earlier(context.ExpiresAtUtc, user.Expiry) } };
			if (!scopes.Contains("pages_manage_posts") || !scopes.Contains("pages_read_engagement"))
				return new(current, Capability(scopes, null, "MetaUserPermissions"));
			if (!scopes.Contains("pages_show_list"))
				return new(current, Capability(scopes, null, "MetaUserPermissions"));
			// Read only the authenticated user's accounts edge. Never request tasks on a Page node,
			// follow provider-supplied URLs, or derive tasks from token/granular scope presence.
			string? after = null;
			var cursors = new HashSet<string>(StringComparer.Ordinal);
			JsonElement? match = null;
			for (var n = 0; n < 5; n++)
			{
				using var accounts = await Graph("me/accounts?limit=100&fields=id,tasks" +
					(after is null ? "" : "&after=" + Uri.EscapeDataString(after)), context.UserAccessToken, ct);
				var data = accounts.RootElement.GetProperty("data");
				if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 100) throw Failure();
				foreach (var account in data.EnumerateArray())
					if (Text(account, "id") == id)
					{
						if (match is not null) throw Failure(); // Ambiguous task evidence.
						match = account.Clone();
					}
				if (!accounts.RootElement.TryGetProperty("paging", out var paging) || !paging.TryGetProperty("next", out _))
					return new(current, Capability(scopes, match, "MetaUserAccounts"));
				after = Text(paging.GetProperty("cursors"), "after");
				if (after.Length == 0 || !cursors.Add(after)) throw Failure();
			}
			throw Failure();
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
		catch (SocialConnectionException) { throw; }
		catch { throw Failure(); }
	}

	// Caller must independently require the persisted publishing grant; scopes alone never grant CREATE_CONTENT.
	public async Task<Publishing.Facebook.FacebookPublicationCredential> PublicationCredentialAsync(string id, SocialCredential credential, CancellationToken ct)
	{
		if (!Available) throw Unauthorized();
		var verified = await ValidateAsync(id, "Page", credential, ct);
		if (!verified.Scopes.Contains("pages_manage_posts") || !verified.Scopes.Contains("pages_read_engagement")) throw Unauthorized();
		using var identity = await Graph("me?fields=id", verified.AccessToken, ct);
		if (Text(identity.RootElement, "id") != id) throw Unauthorized();
		return new() { PageId = id, AccessToken = verified.AccessToken, ExpiresAt = verified.ExpiresAtUtc,
			ApiVersion = Version, AppSecret = ClientSecret };
	}
}
