namespace Redeemer.SocialFlow.Application.AI;

public sealed record GeneratedContent(
	string Title,
	string Content,
	string? CallToAction,
	string? VisualBrief,
	IReadOnlyList<string> Warnings);