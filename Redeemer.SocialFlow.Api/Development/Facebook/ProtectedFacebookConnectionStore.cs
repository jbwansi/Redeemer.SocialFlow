using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
namespace Redeemer.SocialFlow.Api.Development.Facebook;
public sealed class ProtectedFacebookConnectionStore(IOptions<FacebookOptions> options) : IFacebookConnectionStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string Pathname => Path.Combine(options.Value.StorageDirectory, "connection.protected");
    private IDataProtector Protector()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local Facebook credentials require Windows DPAPI.");
        return DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(options.Value.StorageDirectory, "keys")), builder =>
        { builder.SetApplicationName("Redeemer.SocialFlow.Facebook.Local.v1");
          if (OperatingSystem.IsWindows()) builder.ProtectKeysWithDpapi(); else throw new PlatformNotSupportedException();
        }).CreateProtector("Facebook.Pages.v1");
    }
    private async Task<FacebookConnection?> Read(CancellationToken token)
    {
        if (!File.Exists(Pathname)) return null;
        var result = JsonSerializer.Deserialize<FacebookConnection>(Protector().Unprotect(await File.ReadAllTextAsync(Pathname, token)));
        return result?.AppId == options.Value.AppId ? result : null;
    }
    private async Task Save(FacebookConnection connection, CancellationToken token)
    {
        var encrypted = Protector().Protect(JsonSerializer.Serialize(connection));
        Directory.CreateDirectory(options.Value.StorageDirectory);
        var temporary = Pathname + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, encrypted, token); File.Move(temporary, Pathname, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task<FacebookConnection?> ReadAsync(CancellationToken token = default)
    { await gate.WaitAsync(token); try { return await Read(token); } finally { gate.Release(); } }
    public async Task SaveAsync(FacebookConnection connection, CancellationToken token = default)
    { await gate.WaitAsync(token); try { await Save(connection, token); } finally { gate.Release(); } }
    public async Task SelectAsync(Guid connectionId, string pageId, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try {
            var connection = await Read(token);
            if (connection is null || connection.Id != connectionId || !connection.Pages.Any(p => p.Id == pageId && p.CanCreate)) throw new FacebookAuthorizationException();
            connection.SelectedPageId = pageId; await Save(connection, token);
        } finally { gate.Release(); }
    }
    public async Task DisconnectAsync(Guid connectionId, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { var current = await Read(token); if (current is null) return;
            if (current.Id != connectionId) throw new FacebookAuthorizationException(); File.Delete(Pathname);
        } finally { gate.Release(); }
    }
}
