namespace Redeemer.SocialFlow.Infrastructure.AI;

public sealed class OpenAIVisualGeneratorOptions
{
    public const string SectionName = "OpenAI:Images";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Quality { get; set; } = "medium";
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxConcurrentGenerations { get; set; } = 2;
    public int MaxImageBytes { get; set; } = 20 * 1024 * 1024;

    internal bool IsValid() =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model) &&
        Model.StartsWith("gpt-image-", StringComparison.Ordinal) && Model.Length <= 100 &&
        !Model.Any(char.IsWhiteSpace) && Quality is "low" or "medium" or "high" &&
        TimeoutSeconds is >= 1 and <= 600 && MaxConcurrentGenerations is >= 1 and <= 16 &&
        MaxImageBytes is >= 1024 and <= 50 * 1024 * 1024;
}
