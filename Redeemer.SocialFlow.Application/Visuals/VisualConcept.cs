using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Terminology;

namespace Redeemer.SocialFlow.Application.Visuals;

// Version the recipe when its meaning changes. Catalogs are always resolved in the active workspace.
public sealed record VisualConcept(string Subject, string Message, string Scene, string Brief,
    string Style, string[] Exclusions, string Fingerprint, TerminologyEvidence? Terminology = null)
{
    public const string Version = "editorial-concept-v3-terminology";
    public static VisualConcept Create(VisualPostContext post, VisualIdentity identity, string? suppliedBrief, TerminologyCatalog? catalog = null)
    {
        if (!post.Title.Any(char.IsLetterOrDigit) || !post.Content.Any(char.IsLetterOrDigit))
            throw new VisualConceptException("EditorialContextRequired");
        if (suppliedBrief is not null && suppliedBrief.Trim() != (post.VisualBrief ?? "").Trim())
            throw new VisualConceptException("SaveEditorialChangesFirst");
        var brief = string.IsNullOrWhiteSpace(post.VisualBrief)
            ? "Illustrer le sujet et le message enregistrés, sans ajouter de faits." : post.VisualBrief;
        if (catalog is not null && catalog.WorkspaceId != post.WorkspaceId)
            throw new VisualConceptException("TerminologyWorkspaceMismatch");
        var rules = catalog?.Terms ?? Array.Empty<TermRule>();
        var title = TerminologyValidator.Check(post.Title, rules, correct: true);
        var content = TerminologyValidator.Check(post.Content, rules, correct: true);
        var visualBrief = TerminologyValidator.Check(brief, rules, correct: true);
        // Approved brand fields are never rewritten by terminology; conflicts are shown for human review.
        var brand = new[] { identity.Name, identity.Slogan, identity.Style, identity.Instructions }
            .Select(x => TerminologyValidator.Check(x, rules)).ToArray();
        var checks = new[] { title, content, visualBrief }.Concat(brand).ToArray();
        var evidence = catalog is null ? null : new TerminologyEvidence(catalog.WorkspaceId, catalog.Fingerprint,
            checks.SelectMany(x => x.Terms).DistinctBy(x => x.Id).OrderBy(x => x.Id).ToArray(), checks.SelectMany(x => x.Warnings).Distinct().ToArray());
        var request = new GenerateVisualRequest(visualBrief.Text, post.Platform, identity, title: title.Text, content: content.Text);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Version, post.Id, post.WorkspaceId, post.UpdatedAt, request.Title, request.Content,
            request.VisualBrief, request.Platform, request.Layout, Identity = identity.Snapshot(), TerminologyFingerprint = catalog?.Fingerprint
        }))));
        return new(title.Text, content.Text,
            $"Illustration de « {title.Text} » ; représenter le message enregistré sans ajouter de faits. Le brief reste complémentaire.",
            request.VisualBrief, identity.Style,
            ["Aucun logo ou certification inventé", "Aucune affiliation ou réalisation non établie", "Aucun thème substitué par le brief",
                "Une graphie exacte dans les pixels nécessite une composition textuelle déterministe ultérieure"], fingerprint, evidence);
    }
}

public sealed class VisualConceptException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
