namespace Redeemer.SocialFlow.Application.Terminology;

public sealed record TermRule(Guid Id, long Revision, string Canonical, string[] Aliases,
    string Language, string UsageContext, string? Note, string Source);
public sealed record TerminologyCatalog(Guid WorkspaceId, string Fingerprint, IReadOnlyList<TermRule> Terms);
public interface ITerminologyReader
{
    Task<TerminologyCatalog> ReadAsync(CancellationToken token);
}
public sealed record TerminologyCheck(string Text, IReadOnlyList<string> Warnings, IReadOnlyList<TermRule> Terms);
public sealed record TerminologyEvidence(Guid WorkspaceId, string Fingerprint, IReadOnlyList<TermRule> Terms,
    IReadOnlyList<string> Warnings);
