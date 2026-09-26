using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Infrastructure.AI;

namespace Redeemer.SocialFlow.Api.Errors;

internal static class DevelopmentAiDiagnostics
{
    public static void Log(ILogger logger, Exception exception)
    {
        var sdk = exception as ClientResultException;
        var code = sdk is null ? "unavailable" : SafeErrorCode(sdk);
        var diagnostic = exception switch
        {
            OptionsValidationException => "OpenAI configuration is missing or invalid. Check OpenAI:ApiKey and OpenAI:Model.",
            ContentGenerationException => "OpenAI returned invalid or incomplete content after three attempts.",
            ClientResultException { Status: 401 } => "OpenAI authentication failed. Check the configured API credential.",
            ClientResultException { Status: 403 } => "OpenAI denied access. Check project and model permissions.",
            ClientResultException { Status: 404 } => "OpenAI resource was not found. Check the configured model.",
            ClientResultException { Status: 429 } when code == "insufficient_quota" => "OpenAI quota is exhausted. Check project billing and limits.",
            ClientResultException { Status: 429 } => "OpenAI rate or quota limit reached. Check usage and project limits.",
            ClientResultException { Status: >= 500 } => "OpenAI service failed. Retry later.",
            ClientResultException { Status: 400 } => "OpenAI rejected the request. Check model support and request schema.",
            ClientResultException { Status: 0 } or HttpRequestException => "OpenAI transport failed. Check network connectivity.",
            OperationCanceledException or TimeoutException => "Content generation timed out or was cancelled.",
            ClientResultException => "OpenAI request failed.",
            _ => "Content generation failed unexpectedly. Inspect local configuration and service setup."
        };

        // Never pass the exception object, Message, Data, headers, or generated output to logging.
        logger.LogWarning("Development AI generation failed. ExceptionType: {ExceptionType}; OpenAIStatus: {OpenAIStatus}; OpenAIErrorCode: {OpenAIErrorCode}; Diagnostic: {Diagnostic}",
            exception.GetType().FullName, sdk?.Status, code, diagnostic);
    }

    private static string SafeErrorCode(ClientResultException exception)
    {
        try
        {
            var content = exception.GetRawResponse()?.Content;
            if (content is null) return "unavailable";
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.Object ||
                !error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String)
                return "unavailable";

            // Treat provider text as untrusted, even in the code field. Emit only known literals.
            return code.GetString() switch
            {
                "invalid_api_key" => "invalid_api_key",
                "insufficient_quota" => "insufficient_quota",
                "rate_limit_exceeded" => "rate_limit_exceeded",
                "model_not_found" => "model_not_found",
                "invalid_request_error" => "invalid_request_error",
                "unsupported_parameter" => "unsupported_parameter",
                "invalid_value" => "invalid_value",
                "server_error" => "server_error",
                _ => "unrecognized"
            };
        }
        catch (Exception)
        {
            // Diagnostics must not replace the original failure or leak an unreadable response.
            return "unavailable";
        }
    }
}
