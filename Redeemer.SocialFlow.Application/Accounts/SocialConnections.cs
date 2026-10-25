using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
namespace Redeemer.SocialFlow.Application.Accounts;

public sealed class SocialConnectionException(int status, string message) : Exception(message) { public int Status { get; } = status; }
public sealed record SocialConnectionDto(Guid Id, SocialPlatform Platform, string ExternalAccountId, string DisplayName,
    string AccountType, SocialConnectionStatus ConnectionStatus, DateTimeOffset ConnectedAtUtc, DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? LastValidatedAtUtc, bool PublishingPermissionGranted, Guid Revision);
public sealed record SocialProviderDto(SocialPlatform Platform, bool Available, string Message);
public sealed record SocialOAuthResult(Guid SessionId, Guid WorkspaceId);
public sealed record SocialCandidateDto(string ExternalAccountId, string DisplayName, string AccountType, bool PublishingPermissionGranted);
// Server-only credentials. Never use these records as controller responses or logger arguments.
public sealed record SocialCredential(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? RefreshExpiresAtUtc, string[] Scopes)
{ public override string ToString() => "SocialCredential [REDACTED]"; }
public sealed record SocialCandidate(string ExternalAccountId, string DisplayName, string AccountType, SocialCredential Credential, bool PublishingPermissionGranted);
public interface ISocialSecretProtector
{
    bool Available { get; }
    string Protect(string value, string purpose);
    string Unprotect(string value, string purpose);
}
public interface IWorkspaceOAuthProvider
{
    SocialPlatform Platform { get; }
    bool Available { get; }
    bool SupportsPkce { get; }
    string AuthorizationUrl(string state, string? challenge);
    Task<IReadOnlyList<SocialCandidate>> DiscoverAsync(string code, string? verifier, CancellationToken ct);
    Task<SocialCredential> ValidateAsync(string externalAccountId, string accountType, SocialCredential credential, CancellationToken ct);
}
public interface ISocialConnections
{
    Task<IReadOnlyList<SocialConnectionDto>> ListAsync(Guid workspaceId, CancellationToken ct);
    Task<IReadOnlyList<SocialProviderDto>> ProvidersAsync(Guid workspaceId, CancellationToken ct);
    Task<string> StartAsync(Guid workspaceId, SocialPlatform platform, CancellationToken ct);
    Task<SocialOAuthResult> CallbackAsync(SocialPlatform platform, string state, string? code, bool denied, CancellationToken ct);
    Task<IReadOnlyList<SocialCandidateDto>> CandidatesAsync(Guid workspaceId, Guid sessionId, CancellationToken ct);
    Task SelectAsync(Guid workspaceId, Guid sessionId, IReadOnlyList<string> externalIds, CancellationToken ct);
    Task DisconnectAsync(Guid workspaceId, Guid id, Guid revision, CancellationToken ct);
    Task ValidateAsync(Guid workspaceId, Guid id, Guid revision, CancellationToken ct);
    Task<IReadOnlyList<Guid>> DestinationsAsync(Guid workspaceId, Guid postId, CancellationToken ct);
    Task SetDestinationsAsync(Guid workspaceId, Guid postId, IReadOnlyList<Guid> ids, CancellationToken ct);
}
