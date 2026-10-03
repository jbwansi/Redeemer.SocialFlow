using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Redeemer.SocialFlow.Api.Development.LinkedIn;

public sealed class LinkedInOAuthClient(HttpClient http, IOptions<LinkedInOptions> options, TimeProvider clock, ILogger<LinkedInOAuthClient> logger, IHostEnvironment environment)
{
	public async Task<LinkedInConnection> ExchangeAsync(string code, CancellationToken cancellationToken)
	{
		var stage = "TokenExchange";
		var diagnosticCode = "LI_TOKEN_TRANSPORT";
		int? httpStatus = null;
		try
		{
			var settings = options.Value;
			// Conservative expiry: start counting before token exchange, not after userinfo/storage.
			var issuedAt = clock.GetUtcNow();
			using var body = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["grant_type"] = "authorization_code",
				["code"] = code,
				["client_id"] = settings.ClientId,
				["client_secret"] = settings.ClientSecret,
				["redirect_uri"] = LinkedInOptions.Callback
			});
			using var response = await http.PostAsync("https://www.linkedin.com/oauth/v2/accessToken", body, cancellationToken);
			httpStatus = (int)response.StatusCode;
			diagnosticCode = "LI_TOKEN_HTTP";
			if (!response.IsSuccessStatusCode) throw new InvalidOperationException("LinkedIn token exchange failed.");
			diagnosticCode = "LI_TOKEN_RESPONSE";
			using var tokenJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
			var root = tokenJson.RootElement;
			var accessToken = root.GetProperty("access_token").GetString();
			var seconds = root.GetProperty("expires_in").GetInt64();
			if (string.IsNullOrWhiteSpace(accessToken) || seconds <= 0) throw new InvalidOperationException("Invalid LinkedIn token response.");
			diagnosticCode = "LI_TOKEN_SCOPE_FORMAT";
			if (root.TryGetProperty("scope", out var granted))
			{
				var scopes = ParseScopes(granted);
				diagnosticCode = "LI_TOKEN_PERMISSION_MISSING";
				if (LinkedInOptions.Scopes.Split(' ').Except(scopes, StringComparer.Ordinal).Any())
					throw new InvalidOperationException("Required LinkedIn scopes were not granted.");
			}
			else
			{
				// RFC 6749 section 5.1: omission means identical to requested scopes.
				// This client only exchanges codes from our state-bound fixed-scope flow;
				// it does not import tokens with an unknown grant requiring introspection.
				LinkedInDiagnostics.Write(logger, environment, stage, null, httpStatus, "LI_TOKEN_SCOPE_ABSENT");
			}
			diagnosticCode = "LI_TOKEN_EXPIRY";
			var expiresAt = issuedAt.AddSeconds(seconds);
			stage = "UserInfo";
			httpStatus = null;
			diagnosticCode = "LI_USERINFO_TRANSPORT";
			using var identityRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.linkedin.com/v2/userinfo");
			identityRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
			using var identityResponse = await http.SendAsync(identityRequest, cancellationToken);
			httpStatus = (int)identityResponse.StatusCode;
			diagnosticCode = "LI_USERINFO_HTTP";
			if (!identityResponse.IsSuccessStatusCode) throw new InvalidOperationException("LinkedIn identity lookup failed.");
			diagnosticCode = "LI_USERINFO_RESPONSE";
			using var identity = JsonDocument.Parse(await identityResponse.Content.ReadAsStringAsync(cancellationToken));
			var subject = identity.RootElement.GetProperty("sub").GetString();
			if (string.IsNullOrWhiteSpace(subject) || expiresAt <= clock.GetUtcNow())
				throw new InvalidOperationException("Invalid or expired LinkedIn connection.");
			// Use the documented authenticated userinfo endpoint. No unvalidated ID-token claims,
			// refresh token, name, email or speculative person-URN mapping are consumed.
			return new LinkedInConnection { AccessToken = accessToken, Subject = subject, ClientId = settings.ClientId, ExpiresAt = expiresAt };
		}
		catch (Exception exception)
		{
			LinkedInDiagnostics.Write(logger, environment, stage, exception,
				httpStatus ?? (exception is HttpRequestException httpError ? (int?)httpError.StatusCode : null), diagnosticCode);
			throw;
		}
	}

	private static string[] ParseScopes(JsonElement scope)
	{
		if (scope.ValueKind != JsonValueKind.String)
			throw new InvalidOperationException("Invalid scope format.");
		var value = scope.GetString()!;
		// Token documentation: URL-encoded spaces. Introspection documentation: commas.
		// Accept commas defensively in token responses too; do not confuse the endpoints.
		for (var index = 0; index < value.Length; index++)
		{
			if (value[index] != '%') continue;
			if (index + 2 >= value.Length || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2]))
				throw new InvalidOperationException("Invalid scope encoding.");
			index += 2;
		}
		value = Uri.UnescapeDataString(value.Replace('+', ' '));
		if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
				!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' or ':' or ' ' or ',')) ||
			value.Split(',').Any(part => string.IsNullOrWhiteSpace(part)))
			throw new InvalidOperationException("Invalid scope format.");
		return value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
	}
}
