using System.Text.Json;
using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public sealed class StoredVisualIdentity : IWorkspaceResource
{
    public Guid WorkspaceId { get; private set; }
    private StoredVisualIdentity() { }
    public Guid Id { get; private set; }
    public string IdentityId { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? Slogan { get; private set; }
    public string Style { get; private set; } = "";
    public string? Instructions { get; private set; }
    public string ColorsJson { get; private set; } = "[]";
    public bool IsActive { get; private set; } = true;
    public bool IsDeleted { get; private set; }
    public bool IsDefault { get; private set; }
    public long Revision { get; private set; }
    public Guid? LogoId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public static StoredVisualIdentity Create(string code, string name, string style, string[] colors,
        string? slogan, string? instructions, DateTimeOffset now)
    {
        var identity = new StoredVisualIdentity { Id = Guid.NewGuid(), CreatedAt = now };
        identity.Update(code, name, style, colors, slogan, instructions, true);
        return identity;
    }
    public void Update(string code, string name, string style, string[] colors, string? slogan, string? instructions, bool active)
    {
        EnsureAvailable();
        var normalized = Required(code, 100).ToUpperInvariant();
        var title = Required(name, 200);
        var graphicStyle = Required(style, 1000);
        if (colors is null || colors.Length is < 1 or > 8 || colors.Any(c => c is null || c.Length != 7 || c[0] != '#' || !c[1..].All(Uri.IsHexDigit)))
            throw new DomainException("Supply one to eight #RRGGBB colors.");
        if (slogan?.Length > 300 || instructions?.Length > 4000) throw new DomainException("Identity text is too long.");
        IdentityId = normalized; Name = title; Style = graphicStyle;
        Slogan = string.IsNullOrWhiteSpace(slogan) ? null : slogan.Trim();
        Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim();
        ColorsJson = JsonSerializer.Serialize(colors.Select(c => c.ToUpperInvariant()).ToArray());
        IsActive = active;
        if (!active) IsDefault = false;
        Revision++;
    }
    public void SetDefault(bool value)
    {
        EnsureAvailable();
        if (value && !IsActive) throw new DomainException("An inactive identity cannot be the default.");
        IsDefault = value; Revision++;
    }
    public void SetLogo(Guid logoId) { EnsureAvailable(); LogoId = logoId; Revision++; }
    public void Delete() { EnsureAvailable(); IsDeleted = true; IsActive = false; IsDefault = false; Revision++; }
    public void EnsureAvailable() { if (IsDeleted) throw new DomainException("Identity has been deleted."); }
    private static string Required(string value, int maximum) =>
        string.IsNullOrWhiteSpace(value) || value.Length > maximum ? throw new DomainException("Invalid identity field.") : value.Trim();
}

// Immutable original files, including replaced logos, are retained for historical snapshots.
public sealed class IdentityLogo : IWorkspaceResource
{
    public Guid WorkspaceId { get; private set; }
    private IdentityLogo() { }
    public Guid Id { get; private set; }
    public Guid IdentityId { get; private set; }
    public string StorageKey { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public int Width { get; private set; }
    public int Height { get; private set; }
    public long ByteLength { get; private set; }
    public string Sha256 { get; private set; } = "";
    public bool IsReady { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public static IdentityLogo Reserve(Guid identityId, string contentType, int width, int height, long length, string hash)
    {
        if (identityId == Guid.Empty || contentType is not ("image/png" or "image/jpeg") || width <= 0 || height <= 0 || length <= 0 || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new DomainException("Invalid logo metadata.");
        var id = Guid.NewGuid();
        return new() { Id = id, IdentityId = identityId, StorageKey = id.ToString("N"), ContentType = contentType,
            Width = width, Height = height, ByteLength = length, Sha256 = hash, CreatedAt = DateTimeOffset.UtcNow };
    }
    public void Complete() { if (IsReady) throw new DomainException("Logo already completed."); IsReady = true; }
}
