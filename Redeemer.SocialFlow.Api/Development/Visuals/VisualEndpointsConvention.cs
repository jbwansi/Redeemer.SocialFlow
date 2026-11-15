using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Redeemer.SocialFlow.Api.Controllers;

namespace Redeemer.SocialFlow.Api.Development.Visuals;

public sealed class VisualEndpointsConvention(bool enabled, bool workspaceEnabled = false) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers.ToArray())
        {
            if (!enabled && (controller.ControllerType == typeof(VisualsController) || controller.ControllerType == typeof(VisualIdentitiesController)))
                application.Controllers.Remove(controller);
            if (controller.ControllerType != typeof(WorkspaceVisualsController)) continue;
            if (!workspaceEnabled)
            {
                application.Controllers.Remove(controller);
                continue;
            }
            foreach (var action in controller.Actions.Where(a => a.ActionName is nameof(PostVisualActions.Upload)
                or nameof(PostVisualActions.Generate) or nameof(PostVisualActions.Select) or nameof(PostVisualActions.Delete)))
                foreach (var selector in action.Selectors)
                    selector.EndpointMetadata.Add(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute("WorkspaceEdit"));
        }
    }
}
