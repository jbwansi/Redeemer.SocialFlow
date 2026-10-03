using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public sealed class KnowledgeDocument
{
    private readonly TimeProvider _timeProvider;
    private KnowledgeDocument() : this(TimeProvider.System) { }
    private KnowledgeDocument(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string PrimaryTheme { get; private set; } = string.Empty;
    public IReadOnlyList<string> Themes { get; private set; } = Array.Empty<string>();
    public KnowledgeDocumentStatus Status { get; private set; }
    public IReadOnlyList<KnowledgeUsage> Usages { get; private set; } = Array.Empty<KnowledgeUsage>();
    public SourceType SourceType { get; private set; }
    public AuthorityLevel AuthorityLevel { get; private set; }
    public string Language { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool CanBeUsedForGeneration => Status == KnowledgeDocumentStatus.Approved &&
        IsActive && Usages.Contains(KnowledgeUsage.Generation);

    public static KnowledgeDocument Create(string title, string primaryTheme, IReadOnlyList<string> themes,
        IReadOnlyList<KnowledgeUsage> usages, SourceType sourceType, AuthorityLevel authorityLevel,
        string language, int version = 1, bool isActive = true, TimeProvider? timeProvider = null)
    {
        var document = new KnowledgeDocument(timeProvider ?? TimeProvider.System);
        document.Update(title, primaryTheme, themes, usages, sourceType, authorityLevel, language, version);
        document.Id = Guid.NewGuid();
        document.Status = KnowledgeDocumentStatus.Draft;
        document.IsActive = isActive;
        document.CreatedAt = document.UpdatedAt;
        return document;
    }

    public void Update(string title, string primaryTheme, IReadOnlyList<string> themes,
        IReadOnlyList<KnowledgeUsage> usages, SourceType sourceType, AuthorityLevel authorityLevel,
        string language, int version)
    {
        if (new[] { title, primaryTheme, language }.Any(string.IsNullOrWhiteSpace))
            throw new DomainException("Title, primary theme and language are required.");
        if (!Enum.IsDefined(sourceType)) throw new DomainException("Invalid source type.");
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(usages);
        if (themes.Any(string.IsNullOrWhiteSpace)) throw new DomainException("Themes must not be blank.");
        var normalizedThemes = themes.Select(theme => theme.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (!normalizedThemes.Contains(primaryTheme.Trim(), StringComparer.Ordinal))
            throw new DomainException("Primary theme must belong to themes.");
        if (!Enum.IsDefined(authorityLevel) || usages.Any(usage => !Enum.IsDefined(usage)))
            throw new DomainException("Invalid authority level or usage.");
        if (version < 1) throw new DomainException("Version must be positive.");

        // Validate everything before changing state; copy collections to protect invariants.
        Title = title.Trim();
        PrimaryTheme = primaryTheme.Trim();
        Themes = Array.AsReadOnly(normalizedThemes);
        Usages = Array.AsReadOnly(usages.Distinct().ToArray());
        SourceType = sourceType;
        AuthorityLevel = authorityLevel;
        Language = language.Trim();
        Version = version;
        UpdatedAt = _timeProvider.GetUtcNow();
    }

    public void ChangeStatus(KnowledgeDocumentStatus status)
    {
        if (!Enum.IsDefined(status)) throw new DomainException("Invalid document status.");
        Status = status;
        UpdatedAt = _timeProvider.GetUtcNow();
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = _timeProvider.GetUtcNow();
    }
}
