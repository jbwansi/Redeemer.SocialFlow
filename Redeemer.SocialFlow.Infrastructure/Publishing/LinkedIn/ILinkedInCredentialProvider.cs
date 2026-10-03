namespace Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;

/// <summary>Server-only credential boundary; never serialize or log its result.</summary>
public interface ILinkedInCredentialProvider
{
    Task<LinkedInMemberCredential?> GetAsync(CancellationToken cancellationToken);
}

// Not a record: generated ToString must not expose the token.
public sealed class LinkedInMemberCredential
{
    public required string AccessToken { get; init; }
    public required string Subject { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
