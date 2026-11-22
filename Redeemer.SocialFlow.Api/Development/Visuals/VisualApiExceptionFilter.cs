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
            BrandCompositionException { Code: "TextCanonicalRequired" } => (409, "Corrigez les graphies recommandées par le référentiel, puis vérifiez et validez à nouveau le texte."),
            BrandCompositionException { Code: "TextReviewRequired" } => (409, "Relisez et validez les textes après une nouvelle vérification du référentiel."),
            BrandCompositionException { Code: "TextOverflow" } => (409, "Le texte ne tient pas lisiblement. Raccourcissez-le ou choisissez la disposition carrée. Aucun texte n’a été tronqué."),
            BrandCompositionException { Code: "TextFontUnavailable" } => (409, "Aucune police disponible ne couvre ce texte. Polices vérifiées : Arial, Segoe UI, DejaVu Sans, Liberation Sans, Noto Sans."),
            BrandCompositionException { Code: "TextTypographyUnsupported" } => (409, "Cette typographie accepte les caractères latins, accents, apostrophes, ® et ™. Un caractère ou une écriture nécessite un autre moteur de rendu."),
            BrandCompositionException { Code: "InvalidTextLength" } => (400, "Indiquez un titre (240 caractères maximum), un sous-titre facultatif (400 maximum) et, en mode messages, 3 à 5 messages de 200 caractères maximum."),
            BrandCompositionException { Code: "UnsafeBrandLayout" } => (409, "Disposition indisponible : aucune zone libre garantie dans l’image. Choisissez un bandeau."),
            BrandCompositionException { Code: "OfficialLogoUnreadable" } => (409, "Le fichier officiel du logo est inaccessible ou son intégrité ne peut pas être vérifiée."),
            BrandCompositionException { Code: "OfficialLogoUnavailable" } => (409, "Cette révision approuvée ne contient pas de logo officiel."),
            BrandCompositionException { Code: "ApprovedSloganUnavailable" } => (409, "Cette révision approuvée ne contient pas de slogan."),
            BrandCompositionException { Code: "LogoSloganDeclarationRequired" } => (409, "Vérifiez et indiquez si le slogan figure déjà dans le logo."),
            BrandCompositionException { Code: "SloganTypographyUnsupported" or "SloganFontUnavailable" } => (409, "Le rendu fidèle de ce slogan n’est pas pris en charge par la typographie disponible."),
            BrandCompositionException { Code: "SloganTooLongForLayout" } => (409, "Le slogan serait trop petit : choisissez une image plus large ou une composition sans slogan séparé."),
            BrandCompositionException { Code: "AlreadyComposed" } => (409, "Utilisez le candidat original pour créer une autre présentation de marque."),
            BrandCompositionException => (409, "Composition indisponible : vérifiez les dimensions et les options de présentation."),
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
                ["code"] = context.Exception is BrandCompositionException composition ? composition.Code : context.Exception is VisualConceptException concept ? concept.Code : "VisualOperationFailed" } }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
