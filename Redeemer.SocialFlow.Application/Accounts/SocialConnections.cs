using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
namespace Redeemer.SocialFlow.Application.Accounts;

public sealed class SocialConnectionException(int status, string message) : Exception(message)
{ public int Status { get; } = status; public int? ProviderHttpStatus { get; init; } }
// Only application-defined codes, counters and validated numeric Facebook Page IDs; never secrets or raw bodies.
public sealed record SocialPageDiscovery(string? ExternalPageId, string Outcome);
public sealed record FacebookResponseDiagnostic(string Stage, int HttpStatus, string DataShape,
    bool GraphError, int? GraphCode, int? GraphSubcode);
public sealed record FacebookGranularPermission(string Permission, string TargetsStatus, IReadOnlyList<string> TargetIds);
public sealed record FacebookGranularDiagnostic(string Shape, IReadOnlyList<FacebookGranularPermission> Permissions);
public sealed record FacebookDiscoveryEvidence(string? ClientId, string? LoginConfigId,
    string? TokenType = null, bool? AppIdMatches = null, bool? PagesShowList = null,
    bool? PagesReadEngagement = null, bool? PagesManagePosts = null,
    IReadOnlyList<FacebookResponseDiagnostic>? Responses = null, FacebookGranularDiagnostic? Granular = null);
public sealed record SocialDiscoveryDiagnostic(string Outcome, string Stage, int PagesReceived, int PagesRetained,
    int PagesWithoutPublishing, int Requests, bool Complete, int? HttpStatus, IReadOnlyDictionary<string,int> Exclusions,
    IReadOnlyList<SocialPageDiscovery>? Pages = null, FacebookDiscoveryEvidence? Facebook = null);
public sealed record SocialDiscoveryResult(IReadOnlyList<SocialCandidate> Candidates, SocialDiscoveryDiagnostic Diagnostic, bool Succeeded);
public sealed record SocialDiscoveryPayload(SocialCandidate[] Candidates, SocialDiscoveryDiagnostic? Diagnostic);
public interface IDiagnosticWorkspaceOAuthProvider
{
    Task<SocialDiscoveryResult> DiscoverWithDiagnosticsAsync(string code, CancellationToken ct);
}
public sealed record SocialConnectionDto(Guid Id, SocialPlatform Platform, string ExternalAccountId, string DisplayName,
    string AccountType, SocialConnectionStatus ConnectionStatus, DateTimeOffset ConnectedAtUtc, DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? LastValidatedAtUtc, bool PublishingPermissionGranted, Guid Revision,
    PublishingCapabilityEvidence? PublishingCapability = null);
public sealed record SocialProviderDto(SocialPlatform Platform, bool Available, string Message);
public sealed record SocialOAuthResult(Guid SessionId, Guid WorkspaceId);
public sealed record SocialCandidateDto(string ExternalAccountId, string DisplayName, string AccountType, bool PublishingPermissionGranted,
    PublishingCapabilityEvidence? PublishingCapability = null);
public sealed record PublishingCapabilityEvidence(PublishingCapabilityStatus Status, string Reason, string Source,
    DateTimeOffset? CheckedAtUtc, string? Task = null);
// Optional, verification-only USER context; encrypted with the PAGE credential, never sent to publishers or DTOs.
public sealed record FacebookVerificationContext(string UserAccessToken, DateTimeOffset? ExpiresAtUtc)
{ public override string ToString() => "FacebookVerificationContext [REDACTED]"; }
// Server-only credentials. Never use these records as controller responses or logger arguments.
public sealed record SocialCredential(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? RefreshExpiresAtUtc, string[] Scopes,
    FacebookVerificationContext? FacebookVerification = null)
{ public override string ToString() => "SocialCredential [REDACTED]"; }
public sealed record SocialCandidate(string ExternalAccountId, string DisplayName, string AccountType, SocialCredential Credential, bool PublishingPermissionGranted,
    PublishingCapabilityEvidence? PublishingCapability = null);
public sealed record PublishingCapabilityValidation(SocialCredential Credential, PublishingCapabilityEvidence Evidence);
public interface IPublishingCapabilityValidator
{
    Task<PublishingCapabilityValidation> ValidatePublishingCapabilityAsync(string id, string accountType, SocialCredential credential, CancellationToken ct);
}
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
    Task<SocialDiscoveryDiagnostic?> DiagnosticsAsync(Guid workspaceId, Guid sessionId, CancellationToken ct);
    Task SelectAsync(Guid workspaceId, Guid sessionId, IReadOnlyList<string> externalIds, CancellationToken ct);
    Task DisconnectAsync(Guid workspaceId, Guid id, Guid revision, CancellationToken ct);
    Task ValidateAsync(Guid workspaceId, Guid id, Guid revision, CancellationToken ct);
    Task<IReadOnlyList<Guid>> DestinationsAsync(Guid workspaceId, Guid postId, CancellationToken ct);
    Task SetDestinationsAsync(Guid workspaceId, Guid postId, IReadOnlyList<Guid> ids, CancellationToken ct);
}
