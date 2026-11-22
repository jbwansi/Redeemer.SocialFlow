using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.AI;

public sealed record GenerateContentRequest(
	string Subject,
	string Objective,
	string Audience,
	SocialPlatform Platform)
{
    public string? Context { get; init; }
    public EditorialIdentity? EditorialIdentity { get; init; }
    public Terminology.TerminologyCatalog? TerminologyCatalog { get; init; }
    public IReadOnlyList<Terminology.TermRule> Terms { get; init; } = Array.Empty<Terminology.TermRule>();

    public IReadOnlyList<Knowledge.KnowledgePassage> ReferencePassages { get; init; } = Array.Empty<Knowledge.KnowledgePassage>();
}
