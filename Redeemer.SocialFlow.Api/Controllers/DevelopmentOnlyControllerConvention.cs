using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Redeemer.SocialFlow.Api.Controllers;

public sealed class DevelopmentOnlyControllerConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers.Where(model =>
                     model.ControllerType == typeof(DevAiController) ||
                     model.ControllerType == typeof(DevKnowledgeDocumentsController) ||
                     model.ControllerType == typeof(DevLinkedInController) ||
                     model.ControllerType == typeof(DevLinkedInPublicationController)).ToArray())
            application.Controllers.Remove(controller);
    }
}
