using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Redeemer.SocialFlow.Api.Development.LinkedIn;

// Intentionally not a record: its ToString must never include credentials.
public sealed class LinkedInConnection
{
    public required string AccessToken { get; init; }
    public required string Subject { get; init; }
    public required string ClientId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>One personal connection per local OS user. No database or frontend token storage.</summary>
public sealed class LinkedInTokenStore(IOptions<LinkedInOptions> options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDataProtector Protector()
    {
        // Fail closed rather than storing an unencrypted key ring on another OS.
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local LinkedIn storage requires Windows DPAPI.");
        var keys = new DirectoryInfo(Path.Combine(options.Value.StorageDirectory, "keys"));
        var provider = DataProtectionProvider.Create(keys, configuration =>
        {
            configuration.SetApplicationName("Redeemer.SocialFlow.LinkedIn.Local.v1");
            if (OperatingSystem.IsWindows()) configuration.ProtectKeysWithDpapi();
            else throw new PlatformNotSupportedException();
        });
        return provider.CreateProtector("LinkedIn.PersonalConnection.v1");
    }
    private string FilePath => Path.Combine(options.Value.StorageDirectory, "connection.protected");

    public async Task SaveAsync(LinkedInConnection connection, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        string? temporary = null;
        try
        {
            var encrypted = Protector().Protect(JsonSerializer.Serialize(connection));
            Directory.CreateDirectory(options.Value.StorageDirectory);
            temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, encrypted, token);
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }

    // Server-only. Never serialize this object into an HTTP result or log it.
    public async Task<LinkedInConnection?> ReadAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<LinkedInConnection>(Protector().Unprotect(await File.ReadAllTextAsync(FilePath, token)))
                ?? throw new InvalidOperationException("Invalid protected connection.");
        }
        finally { _gate.Release(); }
    }
}
