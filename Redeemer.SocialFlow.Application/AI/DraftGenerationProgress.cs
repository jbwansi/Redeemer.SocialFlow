namespace Redeemer.SocialFlow.Application.AI;

public enum DraftGenerationStage { Configuration, KnowledgeSearch, ProviderCall, ResponseValidation, Persistence }

// Scoped per request. Contains no brief, provider response, credentials or user identity.
public sealed class DraftGenerationProgress
{
    public DraftGenerationStage Stage { get; set; } = DraftGenerationStage.Configuration;
}
