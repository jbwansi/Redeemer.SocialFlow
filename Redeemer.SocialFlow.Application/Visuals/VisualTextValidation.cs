using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Redeemer.SocialFlow.Application.Terminology;

namespace Redeemer.SocialFlow.Application.Visuals;

public static class VisualTextValidation
{
    public static void Validate(VisualTextOptions text)
    {
        if (text.Display is not ("Illustration" or "Title" or "Messages") || text.Layout is not ("Landscape" or "Square"))
            throw new BrandCompositionException("InvalidTextOptions");
        var messages = text.Messages ?? [];
        if (text.Display == "Illustration")
        {
            if (!string.IsNullOrEmpty(text.Title) || !string.IsNullOrEmpty(text.Subtitle) || messages.Length != 0)
                throw new BrandCompositionException("HiddenTextNotAllowed");
            return;
        }
        if (string.IsNullOrWhiteSpace(text.Title) || text.Title.Length > 240 || text.Subtitle?.Length > 400 ||
            (text.Display == "Messages" ? messages.Length is < 3 or > 5 : messages.Length != 0) ||
            messages.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200))
            throw new BrandCompositionException("InvalidTextLength");
        if (messages.Prepend(text.Title).Prepend(text.Subtitle ?? "").Any(x => x.Any(c => char.IsControl(c) && c != '\n')))
            throw new BrandCompositionException("TextTypographyUnsupported");
    }

    public static VisualTextReview Review(VisualPostContext post, VisualTextOptions text, TerminologyCatalog? catalog)
    {
        Validate(text);
        if (catalog is not null && catalog.WorkspaceId != post.WorkspaceId)
            throw new VisualConceptException("TerminologyWorkspaceMismatch");
        var checks = (text.Messages ?? []).Prepend(text.Title ?? "").Prepend(text.Subtitle ?? "")
            .Select(x => TerminologyValidator.Check(x, catalog?.Terms ?? [], correct: false)).ToArray();
        // Never rewrite validated text behind the user's back. Recommendations must be reviewed and corrected in the form.
        var evidence = catalog is null ? null : new TerminologyEvidence(catalog.WorkspaceId, catalog.Fingerprint,
            checks.SelectMany(x => x.Terms).DistinctBy(x => x.Id).OrderBy(x => x.Id).ToArray(),
            checks.SelectMany(x => x.Warnings).Distinct().ToArray());
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { Version = "visual-text-v1", post.Id, post.WorkspaceId, post.UpdatedAt, Text = text, Catalog = catalog?.Fingerprint }))));
        var correctionsRequired = checks.Any(x => TerminologyValidator.Check(x.Text, catalog?.Terms ?? [], correct: true).Text != x.Text);
        return new(text, evidence, fingerprint, correctionsRequired);
    }
}
