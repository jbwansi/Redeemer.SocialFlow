using System.Security.Cryptography;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Visuals;
using SkiaSharp;
namespace Redeemer.SocialFlow.Infrastructure.Visuals;

public sealed class SkiaBrandComposer(VisualIdentityLibrary library, IVisualValidator validator, VisualLimits limits) : IVisualBrandComposer
{
    private static readonly SemaphoreSlim RenderingSlots = new(2);
    public async Task<ComposedImage> ComposeAsync(byte[] source, VisualIdentity identity, BrandCompositionOptions options, CancellationToken token)
    {
        await RenderingSlots.WaitAsync(token);
        try { return await RenderAsync(source, identity, options, token); }
        finally { RenderingSlots.Release(); }
    }
    private async Task<ComposedImage> RenderAsync(byte[] source, VisualIdentity identity, BrandCompositionOptions options, CancellationToken token)
    {
        if (identity.LibraryId is not Guid identityId || identity.ApprovalId is null) throw new BrandCompositionException("ApprovedIdentityRequired");
        if (options.Mode is not ("Logo" or "Slogan" or "LogoAndSlogan") && !(options.Mode == "None" && options.Text?.Display is "Title" or "Messages")) throw new BrandCompositionException("InvalidBrandMode");
        if (options.Layout is not ("Compact" or "Classic")) throw new BrandCompositionException("UnsafeBrandLayout");
        if (options.Text is not null) VisualTextValidation.Validate(options.Text);
        var logoRequired = options.Mode is "Logo" or "LogoAndSlogan";
        if (options.Mode == "LogoAndSlogan" && options.LogoContainsSlogan is null) throw new BrandCompositionException("LogoSloganDeclarationRequired");
        var sloganRequired = options.Mode == "Slogan" || options.Mode == "LogoAndSlogan" && options.LogoContainsSlogan == false;
        if (logoRequired && identity.LogoId is null) throw new BrandCompositionException("OfficialLogoUnavailable");
        if (sloganRequired && string.IsNullOrWhiteSpace(identity.Slogan)) throw new BrandCompositionException("ApprovedSloganUnavailable");
        if (sloganRequired && identity.Slogan!.Any(char.IsControl)) throw new BrandCompositionException("SloganTypographyUnsupported");
        await validator.ValidateAsync(source, token);
        byte[]? logoBytes = null;
        if (logoRequired)
        {
            try { logoBytes = (await library.ReadLogoAsync(identityId, identity.LogoId!.Value, token)).Bytes; }
            catch (VisualStorageException) { throw new BrandCompositionException("OfficialLogoUnreadable"); }
        }
        if (logoBytes is not null) await validator.ValidateAsync(logoBytes, token);
        token.ThrowIfCancellationRequested();
        using var original = SKBitmap.Decode(source);
        using var logo = logoBytes is null ? null : SKBitmap.Decode(logoBytes);
        if (original is null || logoRequired && logo is null) throw new InvalidVisualException("Invalid composition input.");
        var width = original.Width; var band = options.Mode == "None" ? 0 : options.Layout == "Compact" ? Math.Max(80, (int)Math.Ceiling(original.Height * .12)) : Math.Max(100, (int)Math.Ceiling(original.Height * .16));
        var height = checked(original.Height + band);
        if (height > limits.MaxDimension || (long)width * height > limits.MaxPixels || width < 200) throw new BrandCompositionException("CompositionDimensionsUnsupported");
        using var output = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(original, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        using var accent = new SKPaint { Color = SKColor.Parse(identity.Colors[0]) };
        canvas.DrawRect(0, original.Height, width, 4, accent);
        var margin = Math.Max(8, Math.Min(16, band * .10f)); var top = original.Height + margin;
        var contentHeight = band - 2 * margin;
        var textLeft = margin;
        if (logo is not null)
        {
            var availableWidth = sloganRequired ? width * .32f - 2 * margin : width - 2 * margin;
            var scale = Math.Min(availableWidth / logo.Width, contentHeight / logo.Height);
            if (scale <= 0) throw new BrandCompositionException("CompositionDimensionsUnsupported");
            var w = logo.Width * scale; var h = logo.Height * scale;
            var left = sloganRequired ? margin + (availableWidth - w) / 2 : (width - w) / 2;
            // Light/white marks need a dark backing; never recolor the official pixels.
            double luminance = 0, weight = 0;
            for (var y = 0; y < logo.Height; y += Math.Max(1, logo.Height / 128))
            for (var x = 0; x < logo.Width; x += Math.Max(1, logo.Width / 128))
            {
                var pixel = logo.GetPixel(x, y); var alpha = pixel.Alpha / 255d;
                luminance += alpha * (.2126 * pixel.Red + .7152 * pixel.Green + .0722 * pixel.Blue) / 255;
                weight += alpha;
            }
            if (weight > 0 && luminance / weight > .7)
            {
                using var backing = new SKPaint { Color = SKColors.Black };
                canvas.DrawRect(left - 4, top + (contentHeight - h) / 2 - 4, w + 8, h + 8, backing);
            }
            canvas.DrawBitmap(logo, new SKRect(left, top + (contentHeight - h) / 2, left + w, top + (contentHeight + h) / 2), new SKSamplingOptions(SKFilterMode.Linear));
            textLeft = width * .34f;
        }
        string? fontFamily = null;
        if (sloganRequired)
        {
            using var typeface = SkiaVisualTextRenderer.ResolveFont(identity.Slogan!);
            if (typeface is null) throw new BrandCompositionException("SloganFontUnavailable");
            using var font = new SKFont(typeface, Math.Min(48, contentHeight * .36f));
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            var text = identity.Slogan!.Normalize(System.Text.NormalizationForm.FormC);
            if (!font.ContainsGlyphs(text)) throw new BrandCompositionException("SloganTypographyUnsupported");
            var available = width - textLeft - margin;
            var measured = font.MeasureText(text);
            if (measured > available) font.Size *= available / measured;
            if (font.Size < 14) throw new BrandCompositionException("SloganTooLongForLayout");
            var metrics = font.Metrics;
            canvas.DrawText(text, textLeft + (available - font.MeasureText(text)) / 2,
                top + (contentHeight - metrics.Descent - metrics.Ascent) / 2, SKTextAlign.Left, font, paint);
            fontFamily = typeface.FamilyName;
        }
        token.ThrowIfCancellationRequested();
        using var image = SKImage.FromBitmap(output); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = encoded.ToArray();
        if (options.Text?.Display is "Title" or "Messages")
            bytes = SkiaVisualTextRenderer.Render(bytes, options.Text, limits, out fontFamily);
        if (bytes.Length > limits.MaxBytes) throw new BrandCompositionException("CompositionTooLarge");
        return new(bytes, logoBytes is null ? null : Convert.ToHexString(SHA256.HashData(logoBytes)).ToLowerInvariant(),
            logoRequired ? sloganRequired ? "LogoAndSlogan" : "Logo" : sloganRequired ? "Slogan" : "None", fontFamily);
    }
}
