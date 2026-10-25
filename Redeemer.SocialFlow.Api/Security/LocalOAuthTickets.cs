using Redeemer.SocialFlow.Api.Development.LinkedIn;

namespace Redeemer.SocialFlow.Api.Security;

public sealed class LocalOAuthTickets(TimeProvider clock)
{
    private readonly LinkedInStateStore store = new(clock);
    public string Create() { var pair = store.Create(); return pair.State + "." + pair.Browser; }
    public bool Consume(string? value)
    {
        var pair = value?.Split('.');
        return pair is { Length: 2 } && store.TryConsume(pair[0], pair[1]);
    }
}
