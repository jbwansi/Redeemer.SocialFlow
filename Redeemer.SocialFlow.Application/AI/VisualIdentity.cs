namespace Redeemer.SocialFlow.Application.AI;

/// <summary>Provider-neutral identity supplied by the caller; no logo bytes are sent to AI.</summary>
public sealed class VisualIdentity
{
    public string Id { get; }
    public string Name { get; }
    public string? Slogan { get; }
    public string Style { get; }
    public IReadOnlyList<string> Colors { get; }

    public VisualIdentity(string id, string name, string style, IEnumerable<string> colors, string? slogan = null)
    {
        Id = Required(id, 100, nameof(id));
        Name = Required(name, 200, nameof(name));
        Style = Required(style, 1000, nameof(style));
        Slogan = string.IsNullOrWhiteSpace(slogan) ? null : Required(slogan, 300, nameof(slogan));
        ArgumentNullException.ThrowIfNull(colors);
        var palette = colors.Take(9).ToArray();
        if (palette.Length is < 1 or > 8 || palette.Any(color =>
            color is null || color.Length != 7 || color[0] != '#' || !color[1..].All(Uri.IsHexDigit)))
            throw new ArgumentException("Supply one to eight hexadecimal #RRGGBB colors.", nameof(colors));
        Colors = Array.AsReadOnly(palette);
    }

    internal static string Required(string value, int maximum, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new ArgumentException($"A non-empty value of at most {maximum} characters is required.", parameter);
        return value.Trim();
    }
}
