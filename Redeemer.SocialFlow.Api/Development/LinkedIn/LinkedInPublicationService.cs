using Microsoft.AspNetCore.DataProtection;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;
namespace Redeemer.SocialFlow.Api.Development.LinkedIn;
public sealed class LinkedInPublicationService(SocialFlowDbContext db, ILinkedInCredentialProvider credentials,
    LinkedInPublicationSettings settings, IServiceProvider services, IDataProtectionProvider protection, TimeProvider clock)
    : LocalPublicationService(db, settings, services, protection, clock)
{
    protected override SocialPlatform Platform => SocialPlatform.LinkedIn;
    protected override string Provider => "LinkedIn";
    public override async Task<LinkedInRuntime> RuntimeAsync(CancellationToken token)
    {
        RequireHistoricalWorkspace();
        var member = await credentials.GetAsync(token);
        var connected = member is not null && member.ExpiresAt > Clock.GetUtcNow();
        return new(connected, member?.ExpiresAt, connected ? "urn:li:person:" + member!.Subject : null,
            Settings.Enabled, Settings.WorkerEnabled, 30,
            "https://localhost:65474/api/dev/linkedin/connect");
    }

}
