using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public enum TermCategory { Brand = 1, Method, Certification, Organization, Technology, Acronym, Concept }
public enum TermStatus { Proposed = 1, Validated, Retired }

public sealed class WorkspaceTerm : IWorkspaceResource
{
    private WorkspaceTerm() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; private set; }
    public string Canonical { get; private set; } = "";
    public string SearchKey { get; private set; } = "";
    public string AliasesJson { get; private set; } = "[]";
    public TermCategory Category { get; private set; }
    public string Language { get; private set; } = "";
    public string UsageContext { get; private set; } = "";
    public string? Note { get; private set; }
    public string? Source { get; private set; }
    public TermStatus Status { get; private set; } = TermStatus.Proposed;
    public long Revision { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public string UpdatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public string? VerifiedBy { get; private set; }
    public static string Key(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormC), @"\s+", " ").ToUpperInvariant();
    public static WorkspaceTerm Create(string actor) => new() { CreatedBy = Actor(actor), CreatedAt = DateTimeOffset.UtcNow };
    private static string Actor(string value) => string.IsNullOrWhiteSpace(value) || value.Length > 450
        ? throw new DomainException("An authenticated terminology manager is required.") : value;
    public void Update(string canonical, string[] aliases, TermCategory category, string? language,
        string? context, string? note, string? source, string actor)
    {
        if (string.IsNullOrWhiteSpace(canonical) || canonical.Length > 200 || canonical.Any(char.IsControl) ||
            !canonical.Any(char.IsLetterOrDigit) || !Enum.IsDefined(category) || aliases is null || aliases.Length > 10 ||
            aliases.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200 || x.Any(char.IsControl) || !x.Any(char.IsLetterOrDigit)) ||
            language?.Length > 30 || context?.Length > 300 || note?.Length > 1000 || source?.Length > 1000)
            throw new DomainException("Invalid terminology fields or limits exceeded.");
        // Preserve canonical spelling; normalization is only a separate lookup representation.
        Canonical = canonical.Trim(); SearchKey = Key(Canonical);
        AliasesJson = JsonSerializer.Serialize(aliases.Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray());
        Category = category; Language = language?.Trim() ?? ""; UsageContext = context?.Trim() ?? "";
        Note = note?.Trim(); Source = source?.Trim(); Status = TermStatus.Proposed;
        VerifiedAt = null; VerifiedBy = null; Touch(actor);
    }
    public void Validate(string actor)
    {
        if (Status != TermStatus.Proposed || string.IsNullOrWhiteSpace(Source))
            throw new DomainException("A proposed term and a reference source are required for validation.");
        VerifiedBy = Actor(actor); VerifiedAt = DateTimeOffset.UtcNow; Status = TermStatus.Validated; Touch(actor);
    }
    public void Retire(string actor) { Status = TermStatus.Retired; Touch(actor); }
    private void Touch(string actor) { UpdatedBy = Actor(actor); UpdatedAt = DateTimeOffset.UtcNow; Revision++; }
}

public sealed class WorkspaceTermRevision : IWorkspaceResource
{
    private WorkspaceTermRevision() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; private set; }
    public Guid TermId { get; private set; }
    public long Revision { get; private set; }
    public string SnapshotJson { get; private set; } = "";
    public static WorkspaceTermRevision Capture(WorkspaceTerm term) => new()
        { WorkspaceId = term.WorkspaceId, TermId = term.Id, Revision = term.Revision, SnapshotJson = JsonSerializer.Serialize(term) };
}
