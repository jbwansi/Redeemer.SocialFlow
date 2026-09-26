using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Redeemer.SocialFlow.Api.Controllers;

public sealed class DevelopmentOnlyControllerConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        var controller = application.Controllers.FirstOrDefault(model => model.ControllerType == typeof(DevAiController));
        if (controller is not null)
            application.Controllers.Remove(controller);
    }
}
