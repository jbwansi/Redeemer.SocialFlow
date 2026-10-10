using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Redeemer.SocialFlow.Api.Development.Visuals;

public sealed class LocalVisualAccess
{
    private readonly byte[]? _secretHash;
    public bool Enabled { get; }
    public bool AllowHttpLoopback { get; }
    public bool Configured => _secretHash is not null;
    public LocalVisualAccess(bool enabled, bool allowHttpLoopback, string? secret, params string?[] openAiKeys)
    {
        Enabled = enabled;
        AllowHttpLoopback = allowHttpLoopback;
        if (secret is { Length: >= 32 and <= 256 } && !string.IsNullOrWhiteSpace(secret) &&
            !openAiKeys.Any(key => key is not null && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(secret)), SHA256.HashData(Encoding.UTF8.GetBytes(key)))))
            _secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }
    public bool Matches(string supplied) => _secretHash is not null &&
        CryptographicOperations.FixedTimeEquals(_secretHash, SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
}

public sealed class LocalVisualAccessFilter(LocalVisualAccess access, IHostEnvironment environment) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var request = http.Request;
        int? status = null;
        if (!environment.IsDevelopment() || !access.Enabled) status = 404;
        else if (!access.Configured) status = 503;
        else if (http.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
            !IsLocalHost(request.Host.Host) || (!request.IsHttps && !access.AllowHttpLoopback) ||
            request.Headers.Keys.Any(k => k.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase))) status = 403;
        else if (!request.Headers.TryGetValue("X-SocialFlow-Visual-Admin", out var secret) || secret.Count != 1 ||
            secret[0] is not { Length: <= 256 } value || !access.Matches(value)) status = 401;
        if (status is { } code)
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = code, Title = "Visual access denied" }) { StatusCode = code };
            return;
        }
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        await next();
    }
    private static bool IsLocalHost(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
}
