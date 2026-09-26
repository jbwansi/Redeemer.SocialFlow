namespace Redeemer.SocialFlow.Infrastructure.AI;

public sealed class OpenAIContentGeneratorOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
