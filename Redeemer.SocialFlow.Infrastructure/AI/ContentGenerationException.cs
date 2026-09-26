namespace Redeemer.SocialFlow.Infrastructure.AI;

public sealed class ContentGenerationException()
    : Exception("OpenAI did not return valid editorial content after 3 attempts.");
