using Redeemer.SocialFlow.Application.Visuals;

namespace Redeemer.SocialFlow.Infrastructure.Visuals;

public sealed class LocalVisualStorageOptions
{
    public string RootPath { get; set; } = "";
    public string[] ForbiddenRoots { get; set; } = [];
}

/// <summary>Private immutable files. The root must be controlled exclusively by the host OS user.</summary>
public sealed class LocalVisualStorage : IVisualStorage
{
    private readonly string _root;
    private readonly int _maxBytes;
    public LocalVisualStorage(LocalVisualStorageOptions options, VisualLimits limits)
    {
        if (!Path.IsPathFullyQualified(options.RootPath)) throw new ArgumentException("An absolute private storage root is required.");
        _root = Path.GetFullPath(options.RootPath).TrimEnd(Path.DirectorySeparatorChar);
        if (options.ForbiddenRoots.Any(path => Within(_root, Path.GetFullPath(path))))
            throw new ArgumentException("Storage cannot be inside a public or application directory.");
        _maxBytes = limits.MaxBytes;
        CheckAncestors(_root);
        Directory.CreateDirectory(_root);
        CheckAncestors(_root);
    }
    private static bool Within(string path, string root) => path.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void CheckAncestors(string path)
    {
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new VisualStorageException();
    }
    private string Resolve(string key)
    {
        if (key.Length != 32 || !key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new VisualStorageException();
        CheckAncestors(_root);
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!Within(path, _root) || (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new VisualStorageException();
        return path;
    }
    public async Task WriteAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken token = default)
    {
        string? temporary = null;
        try
        {
            var path = Resolve(key);
            if (bytes.Length == 0 || bytes.Length > _maxBytes) throw new VisualStorageException();
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, token);
                await stream.FlushAsync(token);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { throw new VisualStorageException(); }
        finally
        {
            if (temporary is not null) { try { File.Delete(temporary); } catch { /* Private temp file is eligible for maintenance. */ } }
        }
    }
    public async Task<byte[]> ReadAsync(string key, CancellationToken token = default)
    {
        try
        {
            await using var stream = new FileStream(Resolve(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (stream.Length > _maxBytes) throw new VisualStorageException();
            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, token);
            return bytes;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { throw new VisualStorageException(); }
    }
    public Task DeleteAsync(string key, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var path = Resolve(key);
            File.Delete(path);
            // Only temporary files for this retired key; active writes hold FileShare.None.
            foreach (var file in Directory.EnumerateFiles(_root, key + ".*.tmp"))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new VisualStorageException();
                File.Delete(file);
            }
            return Task.CompletedTask;
        }
        catch { throw new VisualStorageException(); }
    }
}
