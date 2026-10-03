using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;

namespace Redeemer.SocialFlow.Api.Development.LinkedIn;

public sealed class ProtectedLinkedInCredentialProvider(LinkedInTokenStore store, IOptions<LinkedInOptions> options)
    : ILinkedInCredentialProvider
{
    public async Task<LinkedInMemberCredential?> GetAsync(CancellationToken cancellationToken)
    {
        var connection = await store.ReadAsync(cancellationToken);
        if (connection is null || !options.Value.IsConfigured ||
            !string.Equals(connection.ClientId, options.Value.ClientId, StringComparison.Ordinal)) return null;
        return new LinkedInMemberCredential
        {
            AccessToken = connection.AccessToken, Subject = connection.Subject, ExpiresAt = connection.ExpiresAt
        };
    }
}
