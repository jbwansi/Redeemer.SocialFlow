namespace Redeemer.SocialFlow.Api.Development.LinkedIn;

internal static class LinkedInDiagnostics
{
    // Only caller-owned constants and exception type names enter this event.
    // Never pass the exception object, Message, Data or provider response text.
    public static void Write(ILogger logger, IHostEnvironment environment, string stage,
        Exception? exception, int? httpStatus, string diagnosticCode)
    {
        if (!environment.IsDevelopment()) return;
        logger.LogWarning("LinkedIn OAuth: Stage={Stage} ExceptionType={ExceptionType} HttpStatus={HttpStatus} DiagnosticCode={DiagnosticCode}",
            stage, exception?.GetType().Name ?? "None", httpStatus, diagnosticCode);
    }
}
