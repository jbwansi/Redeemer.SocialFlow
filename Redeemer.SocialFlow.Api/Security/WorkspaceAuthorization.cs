using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;

namespace Redeemer.SocialFlow.Api.Security;

public sealed class WorkspaceRequirement : IAuthorizationRequirement { }
public sealed class WorkspaceAuthorization(IHttpContextAccessor http, IWorkspaceDirectory directory, WorkspaceAccessContext access)
    : AuthorizationHandler<WorkspaceRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, WorkspaceRequirement requirement)
    {
        var request = http.HttpContext?.Request;
        if (request is null) return;
        if (context.User.Identities.Any(i => i.AuthenticationType == AccountSecurity.LocalScheme && i.IsAuthenticated))
        {
            // The transitional administrator cannot impersonate another workspace.
            if (request.Headers.ContainsKey("X-Workspace-Id") && request.Headers["X-Workspace-Id"] != Workspace.HistoricalId.ToString()) return;
            access.Bind(Workspace.HistoricalId); context.Succeed(requirement); return;
        }
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!request.IsHttps || userId is null || !request.Headers.TryGetValue("X-Workspace-Id", out var header) ||
            header.Count != 1 || !Guid.TryParse(header[0], out var id)) return;
        if (await directory.GetAsync(userId, id, http.HttpContext!.RequestAborted) is null) return;
        access.Bind(id); context.Succeed(requirement);
    }
}
