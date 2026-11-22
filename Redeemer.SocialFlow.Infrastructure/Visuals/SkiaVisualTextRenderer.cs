using System.Text;
using Redeemer.SocialFlow.Application.Visuals;
using SkiaSharp;

namespace Redeemer.SocialFlow.Infrastructure.Visuals;

/// <summary>Bounded Latin typography; no silent font substitution, cropping or ellipsis.</summary>
public static class SkiaVisualTextRenderer
{
    public static SKTypeface ResolveFont(string text, bool bold = false, IEnumerable<string>? families = null)
    {
        var normalized = text.Normalize(NormalizationForm.FormC);
        if (normalized.Any(c => c != '\n' && (c < 32 || !(c <= 0x024f || c is >= '\u2000' and <= '\u206f' || c == '™'))))
            throw new BrandCompositionException("TextTypographyUnsupported");
        foreach (var family in families ?? ["Arial", "Segoe UI", "DejaVu Sans", "Liberation Sans", "Noto Sans"])
        {
            var face = SKTypeface.FromFamilyName(family, bold ? SKFontStyle.Bold : SKFontStyle.Normal);
            if (face is not null && string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                using var font = new SKFont(face, 24);
                if (font.ContainsGlyphs(normalized.Replace("\n", ""))) return face;
            }
            face?.Dispose();
        }
        throw new BrandCompositionException("TextFontUnavailable");
    }

    public static byte[] Render(byte[] brandedImage, VisualTextOptions text, VisualLimits limits, out string fontFamily)
    {
        VisualTextValidation.Validate(text);
        using var original = SKBitmap.Decode(brandedImage) ?? throw new InvalidVisualException("Invalid composition input.");
        const int width = 1200;
        var height = text.Layout == "Square" ? 1200 : 900;
        if (width > limits.MaxDimension || height > limits.MaxDimension || (long)width * height > limits.MaxPixels)
            throw new BrandCompositionException("CompositionDimensionsUnsupported");
        var allText = string.Join("\n", (text.Messages ?? []).Prepend(text.Subtitle ?? "").Prepend(text.Title ?? ""));
        using var regular = ResolveFont(allText);
        using var bold = ResolveFont(text.Title!, true);
        fontFamily = regular.FamilyName + " / " + bold.FamilyName;
        using var output = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.White);
        var square = text.Layout == "Square";
        // The complete original plus its official brand band is contained, never cropped or stretched.
        var picture = square ? new SKRect(32, 24, 1168, 620) : new SKRect(24, 24, 676, height - 24);
        var scale = Math.Min(picture.Width / original.Width, picture.Height / original.Height);
        var left = picture.MidX - original.Width * scale / 2;
        var top = picture.MidY - original.Height * scale / 2;
        canvas.DrawBitmap(original, new SKRect(left, top, left + original.Width * scale, top + original.Height * scale),
            new SKSamplingOptions(SKFilterMode.Linear));
        var area = square ? new SKRect(56, 660, 1144, height - 48) : new SKRect(724, 64, 1152, height - 64);
        var blocks = new List<(string Text, bool Bold, float Size)> { (text.Title!, true, 48) };
        if (!string.IsNullOrEmpty(text.Subtitle)) blocks.Add((text.Subtitle, false, 30));
        blocks.AddRange((text.Messages ?? []).Select(x => (x, false, 28f)));
        List<(string Text, bool Bold, float Size, float X, float Y)>? fitted = null;
        for (var factor = 1f; factor >= .749f; factor -= .05f)
        {
            var lines = new List<(string, bool, float, float, float)>();
            var y = area.Top;
            var fits = true;
            foreach (var block in blocks)
            {
                using var font = new SKFont(block.Bold ? bold : regular, block.Size * factor);
                var metrics = font.Metrics;
                var lineHeight = Math.Max(font.Size * 1.3f, metrics.Descent - metrics.Ascent + metrics.Leading);
                var wrapped = Wrap(block.Text.Normalize(NormalizationForm.FormC), font, area.Width);
                if (wrapped is null) { fits = false; break; }
                foreach (var line in wrapped)
                {
                    var baseline = y - metrics.Ascent;
                    if (baseline + metrics.Descent > area.Bottom) { fits = false; break; }
                    lines.Add((line, block.Bold, font.Size, area.Left, baseline));
                    y += lineHeight;
                }
                if (!fits) break;
                y += 16 * factor;
            }
            if (fits) { fitted = lines; break; }
        }
        if (fitted is null) throw new BrandCompositionException("TextOverflow");
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        foreach (var line in fitted)
        {
            using var font = new SKFont(line.Bold ? bold : regular, line.Size);
            canvas.DrawText(line.Text, line.X, line.Y, SKTextAlign.Left, font, paint);
        }
        using var image = SKImage.FromBitmap(output);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        if (bytes.Length > limits.MaxBytes) throw new BrandCompositionException("CompositionTooLarge");
        return bytes;
    }

    private static List<string>? Wrap(string text, SKFont font, float width)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            // Preserve spaces and explicit line breaks, and never split a word or discard a glyph.
            foreach (var word in System.Text.RegularExpressions.Regex.Matches(paragraph, @"\S+\s*|\s+", System.Text.RegularExpressions.RegexOptions.CultureInvariant).Cast<System.Text.RegularExpressions.Match>().Select(x => x.Value))
            {
                if (font.MeasureText(word) > width) return null;
                if (font.MeasureText(line + word) > width) { result.Add(line); line = word; }
                else line += word;
            }
            result.Add(line);
        }
        return result;
    }
}
