using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Redeemer.SocialFlow.Api.Development.LinkedIn;

/// <summary>Single-process local flow: pending authorizations deliberately expire on restart.</summary>
public sealed class LinkedInStateStore(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, (string Browser, DateTimeOffset Expires)> _pending = new();
    private readonly object _gate = new();

    public (string State, string Browser) Create()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            foreach (var key in _pending.Where(x => x.Value.Expires <= now).Select(x => x.Key).ToArray()) _pending.Remove(key);
            if (_pending.Count >= 256) throw new InvalidOperationException("Too many pending connections.");
            var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var browser = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            _pending.Add(state, (browser, now + Lifetime));
            return (state, browser);
        }
    }

    public bool TryConsume(string? state, string? browser)
    {
        if (state is null || browser is null || state.Length != 43 || browser.Length != 43) return false;
        lock (_gate)
        {
            if (!_pending.TryGetValue(state, out var pending)) return false;
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(browser), Encoding.UTF8.GetBytes(pending.Browser))) return false;
            _pending.Remove(state); // atomically single-use, even for refused/expired authorizations
            return pending.Expires > clock.GetUtcNow();
        }
    }
}
