using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.AI;

public sealed record GenerateContentRequest(
	string Subject,
	string Objective,
	string Audience,
	SocialPlatform Platform);