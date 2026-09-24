using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Api.Errors;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            PostNotFoundException => (404, "Post not found", exception.Message),
            DomainException => (400, "Domain validation failed", exception.Message),
            ArgumentException { ParamName: "request" } => (400, "Invalid request", "The supplied request parameters are invalid. Check the date range."),
            _ => (500, "An unexpected error occurred", "The request could not be completed. Please try again later.")
        };

        if (status == 500)
            logger.LogError(exception, "Unhandled API exception. TraceId: {TraceId}", context.TraceIdentifier);

        context.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (!await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem }))
            await context.Response.WriteAsJsonAsync(problem, options: null,
                contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
