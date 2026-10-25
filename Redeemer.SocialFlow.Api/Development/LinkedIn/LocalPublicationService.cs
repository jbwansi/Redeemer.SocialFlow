using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;

namespace Redeemer.SocialFlow.Api.Development.LinkedIn;

public sealed record LinkedInPublicationSettings(bool Enabled, bool WorkerEnabled);
public sealed record LinkedInRuntime(bool Connected, DateTimeOffset? ExpiresAt, string? Destination,
    bool PublicationEnabled, bool WorkerEnabled, int WorkerIntervalSeconds, string ConnectUrl);
public sealed record LinkedInPublicationView(PublicationOperationDto? Operation, bool CanPublish);
public sealed record LinkedInPreview(string Content, string Destination, string Confirmation);
internal sealed record PublicationConfirmation(Guid PostId, Guid OperationId, string Content, string Destination, DateTimeOffset ExpiresAt, Guid? VisualId = null);
public sealed class LinkedInPublicationException(int status) : Exception
{
    public int Status { get; } = status;
}

/// <summary>Development host boundary: derive destination server-side, delegate all durable writes to the use case.</summary>
public abstract class LocalPublicationService(SocialFlowDbContext db, LinkedInPublicationSettings settings,
    IServiceProvider services, IDataProtectionProvider protection, TimeProvider clock)
{
    protected TimeProvider Clock => clock;
    protected LinkedInPublicationSettings Settings => settings;
    protected void RequireHistoricalWorkspace()
    {
        if (db.ActiveWorkspaceId != Redeemer.SocialFlow.Domain.Entities.Workspace.HistoricalId)
            throw new LinkedInPublicationException(403);
    }
    protected abstract SocialPlatform Platform { get; }
    protected abstract string Provider { get; }
    protected virtual string ConfirmationPurpose => Provider + ".ManualPublicationConfirmation.v1";
    private IDataProtector Protector => protection.CreateProtector(ConfirmationPurpose);
    public abstract Task<LinkedInRuntime> RuntimeAsync(CancellationToken token);
    public async Task<LinkedInPublicationView> GetAsync(Guid id, CancellationToken token)
    {
        RequireHistoricalWorkspace();
        var post = await db.SocialPosts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new LinkedInPublicationException(404);
        var operation = await db.PublicationOperations.AsNoTracking().SingleOrDefaultAsync(x => x.SocialPostId == id, token);
        if (operation is not null) return new(PublicationOperationDto.FromEntity(operation), false);
        var runtime = await RuntimeAsync(token);
        return new(operation is null ? null : PublicationOperationDto.FromEntity(operation),
            operation is null && runtime.PublicationEnabled && runtime.Connected && post.Platform == Platform &&
            post.Status == SocialPostStatus.Scheduled && post.ScheduledAt is not null && post.ScheduledAt <= clock.GetUtcNow());
    }

    public async Task<LinkedInPreview> PreviewAsync(Guid id, CancellationToken token)
    {
        if (!(await GetAsync(id, token)).CanPublish) throw new LinkedInPublicationException(409);
        var post = await db.SocialPosts.AsNoTracking().SingleAsync(x => x.Id == id, token);
        var runtime = await RuntimeAsync(token);
        if (!runtime.Connected) throw new LinkedInPublicationException(409);
        var confirmation = new PublicationConfirmation(id, Guid.NewGuid(), post.Content, runtime.Destination!, clock.GetUtcNow().AddMinutes(5), post.SelectedVisualId);
        return new(post.Content, runtime.Destination!, Protector.Protect(JsonSerializer.Serialize(confirmation)));
    }

    public async Task<PublicationOperationDto> PublishAsync(Guid id, string? confirmation, CancellationToken token)
    {
        if (!settings.Enabled) throw new LinkedInPublicationException(503);
        PublicationConfirmation proof;
        try
        {
            proof = JsonSerializer.Deserialize<PublicationConfirmation>(Protector.Unprotect(confirmation ?? ""))!;
            if (proof is null || proof.PostId != id || proof.ExpiresAt <= clock.GetUtcNow()) throw new InvalidOperationException();
        }
        catch (Exception) { throw new LinkedInPublicationException(400); }
        // Repeated submissions can only observe the durable result, never create another attempt.
        var current = await GetAsync(id, token);
        if (current.Operation is not null) return current.Operation;
        if (!current.CanPublish) throw new LinkedInPublicationException(409);
        var runtime = await RuntimeAsync(token);
        var post = await db.SocialPosts.AsNoTracking().SingleAsync(x => x.Id == id, token);
        if (runtime.Destination != proof.Destination || post.Content != proof.Content || post.SelectedVisualId != proof.VisualId) throw new LinkedInPublicationException(409);
        return await ExecuteAsync(id, proof.OperationId, runtime.Destination!, token);
    }

    public async Task ProcessDueAsync(CancellationToken token)
    {
        RequireHistoricalWorkspace();
        if (!settings.WorkerEnabled) return;
        var runtime = await RuntimeAsync(token);
        if (!runtime.Connected || !runtime.PublicationEnabled) return;
        // SQLite cannot order/compare DateTimeOffset directly; compare instants in memory.
        // Exclude every claimed operation, including unfinished, Failed and Indeterminate.
        var candidates = await db.SocialPosts.AsNoTracking()
            .Where(post => post.Platform == Platform && post.Status == SocialPostStatus.Scheduled &&
                !db.PublicationOperations.Any(operation => operation.SocialPostId == post.Id))
            .Select(post => new { post.Id, post.ScheduledAt }).ToListAsync(token);
        foreach (var post in candidates.Where(post => post.ScheduledAt is not null && post.ScheduledAt <= clock.GetUtcNow())
                     .OrderBy(post => post.ScheduledAt).ThenBy(post => post.Id).Take(20))
        {
            token.ThrowIfCancellationRequested();
            try { await ExecuteAsync(post.Id, Guid.NewGuid(), runtime.Destination!, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { /* A claim, if created, stays protected. Never log provider exception payloads. */ }
        }
    }

    private Task<PublicationOperationDto> ExecuteAsync(Guid id, Guid operationId, string destination, CancellationToken token)
        => services.GetRequiredService<IPublishScheduledPost>().ExecuteAsync(new(id, operationId, Provider, destination), token);
}
