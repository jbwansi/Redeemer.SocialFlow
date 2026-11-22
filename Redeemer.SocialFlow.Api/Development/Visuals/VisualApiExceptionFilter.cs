using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Api.Development.Visuals;

public sealed class VisualApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, title) = context.Exception switch
        {
            EditorialAngleUnavailableException => (404, "Angle introuvable dans cet espace."),
            Infrastructure.Terminology.TermNotFoundException => (404, "Terme introuvable dans cet espace."),
            VisualConceptException { Code: "EditorialContextRequired" } => (400, "Enregistrez un titre et un contenu éditorial avant de préparer le concept."),
            VisualConceptException => (409, "Enregistrez vos modifications, puis préparez et validez à nouveau le concept visuel."),
            IdentityNotFoundException or VisualNotFoundException or PostNotFoundException => (404, "Visual or post not found"),
            DomainException or DbUpdateConcurrencyException => (409, "Modification impossible : état incompatible, référence existante ou version périmée. Actualisez les données."),
            InvalidVisualException or ArgumentException => (400, "Invalid image or generation request"),
            VisualGenerationException { Failure: VisualGenerationFailure.RateLimited or VisualGenerationFailure.ConcurrencyLimit } => (429, "Visual generation limit reached"),
            VisualGenerationException or OptionsValidationException => (503, "Visual generation unavailable"),
            OperationCanceledException => (499, "Request cancelled"),
            _ => (500, "Visual operation failed")
        };
        // Never allow SDK/configuration/storage exception payloads into global exception logging.
        context.Result = new ObjectResult(new ProblemDetails { Status = status, Title = title,
            Extensions = { ["traceId"] = context.HttpContext.TraceIdentifier,
                ["code"] = context.Exception is VisualConceptException concept ? concept.Code : "VisualOperationFailed" } }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
