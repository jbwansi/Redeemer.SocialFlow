using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Redeemer.SocialFlow.Api.Controllers;

namespace Redeemer.SocialFlow.Api.Development.Visuals;

public sealed class VisualEndpointsConvention(bool enabled) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        if (enabled) return;
        foreach (var controller in application.Controllers.Where(c => c.ControllerType == typeof(VisualsController) || c.ControllerType == typeof(VisualIdentitiesController)).ToArray())
            application.Controllers.Remove(controller);
    }
}
