using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Api.Publishing;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Api.Controllers;

[ApiController, Route("api/workspace-publication-settings"), Authorize(Policy = "WorkspaceMember")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicationSettingsController(SocialFlowDbContext db, IWorkspaceAccessContext workspace) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        var row = await db.Workspaces.AsNoTracking().SingleOrDefaultAsync(x => x.Id == workspace.WorkspaceId, token);
        return row is null ? NotFound() : Ok(WorkspacePublicationSettings.Read(row.PublicationSettingsJson));
    }

    [HttpPut, Authorize(Policy = "WorkspaceManageSocial"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Update(WorkspacePublicationSettings request, CancellationToken token)
    {
        if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(request.TimeZone, out _) ||
            request.SuggestedTimes is null || request.SuggestedTimes.Length > 8 ||
            request.SuggestedTimes.Any(x => !TimeOnly.TryParseExact(x, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            return Problem(statusCode: 400, title: "Fuseau horaire ou créneaux invalides.");
        var row = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == workspace.WorkspaceId, token);
        if (row is null) return NotFound();
        if (!await ValidDestination(request.FacebookConnectionId, SocialPlatform.Facebook, "Page", token) ||
            !await ValidDestination(request.LinkedInConnectionId, SocialPlatform.LinkedIn, "Profile", token))
            return Problem(statusCode: 400, title: "Destination incompatible avec cet espace.");
        request = request with { SuggestedTimes = request.SuggestedTimes.Distinct().Order(StringComparer.Ordinal).ToArray() };
        row.SetPublicationSettings(JsonSerializer.Serialize(request));
        await db.SaveChangesAsync(token);
        return Ok(request);
    }

    private Task<bool> ValidDestination(Guid? id, SocialPlatform platform, string type, CancellationToken token) =>
        id is null ? Task.FromResult(true) : db.SocialConnections.AnyAsync(x => x.Id == id && x.WorkspaceId == workspace.WorkspaceId &&
            x.Platform == platform && x.AccountType == type, token);
}
