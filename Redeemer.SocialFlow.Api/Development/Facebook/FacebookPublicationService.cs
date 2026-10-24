using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
namespace Redeemer.SocialFlow.Api.Development.Facebook;
public sealed class FacebookPublicationService(SocialFlowDbContext db, IFacebookConnectionStore connections,
    IOptions<FacebookOptions> options, IServiceProvider services, IDataProtectionProvider protection, TimeProvider clock)
    : LocalPublicationService(db, new(options.Value.Enabled && options.Value.PublicationEnabled,
        options.Value.Enabled && options.Value.PublicationEnabled && options.Value.WorkerEnabled), services, protection, clock)
{
    protected override SocialPlatform Platform => SocialPlatform.Facebook;
    protected override string Provider => "Facebook";
    public override async Task<LinkedInRuntime> RuntimeAsync(CancellationToken token)
    {
        var connection = await connections.ReadAsync(token);
        var page = connection?.Pages.SingleOrDefault(p => p.Id == connection.SelectedPageId && p.CanCreate);
        var connected = options.Value.Configured && connection?.AppId == options.Value.AppId && page is not null &&
            connection!.ExpiresAt > Clock.GetUtcNow() && !FacebookOptions.RequiredScopes.Except(connection.Scopes).Any();
        return new(connected, connection?.ExpiresAt, connected ? page!.Id : null,
            options.Value.Enabled && options.Value.PublicationEnabled, options.Value.WorkerEnabled && options.Value.PublicationEnabled,
            30, "");
    }
}
