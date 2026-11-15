using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;

// Server-only, validated credentials. Never serialize or log this type.
public sealed class FacebookPublicationCredential
{
    public required string PageId { get; init; }
    public required string AccessToken { get; init; }
    public required string AppSecret { get; init; }
    public required string ApiVersion { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string Proof => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), Encoding.UTF8.GetBytes(AccessToken))).ToLowerInvariant();
    public override string ToString() => "FacebookPublicationCredential [REDACTED]";
}
public interface IFacebookPublicationCredentialProvider
{
    Task<FacebookPublicationCredential?> GetAsync(string pageId, CancellationToken ct);
}

// Keeps the historical local flow on the same publisher without adapting a USER token into a PAGE token.
internal sealed class LocalFacebookPublicationCredentials(IFacebookConnectionStore connections, FacebookGraphClient graph,
    IOptions<FacebookOptions> options, TimeProvider clock) : IFacebookPublicationCredentialProvider
{
    public async Task<FacebookPublicationCredential?> GetAsync(string pageId, CancellationToken ct)
    {
        if (!options.Value.Configured) return null;
        var connection = await connections.ReadAsync(ct);
        if (connection is null || connection.AppId != options.Value.AppId || connection.HasElapsedDeadline(clock.GetUtcNow()) ||
            FacebookOptions.RequiredScopes.Except(connection.Scopes).Any() || !connection.Pages.Any(p => p.Id == pageId && p.CanCreate)) return null;
        var verified = await graph.VerifyUserTokenAsync(connection.UserToken, ct);
        await graph.PermissionsAsync(connection.UserToken, ct);
        var page = (await graph.PagesAsync(connection.UserToken, ct)).SingleOrDefault(p => p.Id == pageId && p.CanCreate);
        if (page is null || verified.HasElapsedDeadline(clock.GetUtcNow())) return null;
        var deadlines = new[] { connection.ExpiresAt, connection.DataAccessExpiresAt, verified.ExpiresAt, verified.DataAccessExpiresAt };
        return new() { PageId = page.Id, AccessToken = page.AccessToken, AppSecret = options.Value.AppSecret,
            ApiVersion = FacebookOptions.Version, ExpiresAt = deadlines.Min() };
    }
}
