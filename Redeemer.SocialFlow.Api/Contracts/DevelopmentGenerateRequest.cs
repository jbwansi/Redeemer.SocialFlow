using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Api.Contracts;

public sealed record DevelopmentGenerateRequest(
    string Subject,
    string Objective,
    string Audience,
    SocialPlatform Platform);
