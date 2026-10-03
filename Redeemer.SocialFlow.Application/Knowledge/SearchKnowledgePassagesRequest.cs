namespace Redeemer.SocialFlow.Application.Knowledge;

/// <summary>
/// Query must be non-blank and Limit positive. Optional Theme and Language must be non-blank
/// when supplied. Filters combine with AND. Theme matches any document theme, using the
/// Domain's trimmed, ordinal case-sensitive convention; Language matches the trimmed value.
/// No caller-supplied status or usage can override generation eligibility.
/// </summary>
public sealed record SearchKnowledgePassagesRequest(
    string Query, int Limit = 5, string? Theme = null, string? Language = null);
