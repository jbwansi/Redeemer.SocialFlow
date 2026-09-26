using System.ComponentModel.DataAnnotations;
using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Api.Contracts;

public sealed record GenerateDraftRequest(
    [Required] string Subject,
    [Required] string Objective,
    [Required] string Audience,
    [Required, EnumDataType(typeof(SocialPlatform))] SocialPlatform? Platform);
