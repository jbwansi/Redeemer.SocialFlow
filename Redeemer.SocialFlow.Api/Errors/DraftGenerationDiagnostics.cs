using System.ClientModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Infrastructure.AI;

namespace Redeemer.SocialFlow.Api.Errors;

internal static class DraftGenerationDiagnostics
{
    public const string ErrorCode = "DraftGenerationFailed";

    public static void Record(ILogger logger, Exception error, DraftGenerationStage stage,
        string traceId, IConfiguration configuration)
    {
        if (stage == DraftGenerationStage.ProviderCall && error is ContentGenerationException)
            stage = DraftGenerationStage.ResponseValidation;

        // Constant allowlist: never pass the exception, Message, Data, stack, inner exception
        // or arbitrary runtime type names to the logger. SDK exceptions can contain bodies.
        var type = error switch
        {
            OptionsValidationException => "OptionsValidationException",
            ContentGenerationException => "ContentGenerationException",
            ClientResultException => "ClientResultException",
            HttpRequestException => "HttpRequestException",
            OperationCanceledException => "OperationCanceledException",
            TimeoutException => "TimeoutException",
            DbUpdateException => "DbUpdateException",
            InvalidOperationException => "InvalidOperationException",
            _ => "OtherException"
        };
        int? status = stage == DraftGenerationStage.ProviderCall ? error switch
        {
            ClientResultException provider => provider.Status,
            HttpRequestException transport => (int?)transport.StatusCode,
            _ => null
        } : null;
        if (status is < 100 or > 599) status = null;
        var category = stage switch
        {
            DraftGenerationStage.Configuration => "ConfigurationFailure",
            DraftGenerationStage.KnowledgeSearch => "KnowledgeSearchFailure",
            DraftGenerationStage.ResponseValidation => "InvalidGeneration",
            DraftGenerationStage.Persistence => "PersistenceFailure",
            _ => error switch
            {
                OperationCanceledException or TimeoutException => "ProviderTimeout",
                ClientResultException => "ProviderRejected",
                HttpRequestException => "ProviderTransportFailure",
                _ => "ProviderFailure"
            }
        };
        logger.LogError(new EventId(5101, ErrorCode),
            "Draft generation failed: Stage={Stage} Category={Category} TraceId={TraceId} ExceptionType={ExceptionType} ProviderHttpStatus={ProviderHttpStatus} ApiKeyPresent={ApiKeyPresent} ModelPresent={ModelPresent}",
            stage.ToString(), category, traceId, type, status,
            !string.IsNullOrWhiteSpace(configuration["OpenAI:ApiKey"]),
            !string.IsNullOrWhiteSpace(configuration["OpenAI:Model"]));
    }
}
