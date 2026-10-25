using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Application.Accounts;

namespace Redeemer.SocialFlow.Infrastructure.Publishing;

public sealed class SqlitePublicationOperationStore(DbContextOptions<SocialFlowDbContext> options,
    TimeProvider? timeProvider = null, IWorkspaceAccessContext? workspace = null) : IPublicationOperationStore
{
    public async Task<PublicationClaim> TryClaimAsync(PublishScheduledPostRequest request, CancellationToken cancellationToken = default)
    {
        // Dedicated short-lived context: never commit unrelated caller-tracked changes.
        await using var db = new SocialFlowDbContext(options, workspace);
        // Microsoft.Data.Sqlite's default transaction takes the write reservation immediately.
        // Eligibility, existing-operation check and insert are serialized across contexts/processes.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (request.AutomaticPlanRevision is { } planRevision)
        {
            var plan = await db.LinkedInAutomations.AsNoTracking().SingleOrDefaultAsync(x => x.SocialPostId == request.PostId, cancellationToken);
            var plannedPost = await db.SocialPosts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PostId, cancellationToken);
            if (plan is null || plan.State != LinkedInAutomationState.Authorized || plan.Revision != planRevision ||
                request.WorkspaceProof is null || plan.AuthorizedByUserId != request.WorkspaceProof.UserId ||
                plan.SocialConnectionId != request.WorkspaceProof.ConnectionId || plannedPost is null ||
                plannedPost.UpdatedAt != plan.PostUpdatedAtUtc || plannedPost.ScheduledAt != plan.ScheduledAtUtc ||
                plan.ScheduledAtUtc > (timeProvider ?? TimeProvider.System).GetUtcNow())
                throw new SocialConnectionException(409, "Autorisation automatique modifiée ou absente.");
        }
        if (request.WorkspaceProof is { } proof)
        {
            var member = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == db.ActiveWorkspaceId && x.UserId == proof.UserId && x.Status == MembershipStatus.Active, cancellationToken);
            if (member is null || !WorkspacePermissions.Allows(member.Role, WorkspacePermission.Approve) ||
                !await db.Workspaces.AnyAsync(x => x.Id == db.ActiveWorkspaceId && x.Status == WorkspaceStatus.Active, cancellationToken))
                throw new SocialConnectionException(403, "Permission de publication insuffisante.");
            var connection = await db.SocialConnections.SingleOrDefaultAsync(x => x.Id == proof.ConnectionId, cancellationToken);
            if (connection is null || connection.Revision != proof.ConnectionRevision || connection.Platform != SocialPlatform.LinkedIn ||
                connection.AccountType != "Profile" || connection.ConnectionStatus != SocialConnectionStatus.Connected ||
                !connection.PublishingPermissionGranted || connection.ExpiresAtUtc is null || connection.ExpiresAtUtc <= (timeProvider ?? TimeProvider.System).GetUtcNow() ||
                request.Destination != "urn:li:person:" + connection.ExternalAccountId)
                throw new SocialConnectionException(409, "La connexion LinkedIn a changé ou n’est plus autorisée.");
            var confirmed = await db.SocialPosts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PostId, cancellationToken);
            if (confirmed is null) throw new PostNotFoundException(request.PostId);
            // An existing durable result may be read again after completion updated the post.
            if (!await db.PublicationOperations.AnyAsync(x => x.SocialPostId == request.PostId, cancellationToken) &&
                (confirmed.Platform != SocialPlatform.LinkedIn || confirmed.Content != proof.Content || confirmed.SelectedVisualId != proof.VisualId || confirmed.UpdatedAt != proof.UpdatedAt))
                throw new SocialConnectionException(409, "La publication a changé. Confirmez à nouveau son contenu.");
        }
        var existing = await db.PublicationOperations.SingleOrDefaultAsync(x => x.OperationId == request.OperationId, cancellationToken);
        if (existing is not null && existing.SocialPostId != request.PostId)
            throw new DomainException("The operation ID already belongs to another post.");
        existing ??= await db.PublicationOperations.SingleOrDefaultAsync(x => x.SocialPostId == request.PostId, cancellationToken);
        if (existing is not null) return new(PublicationOperationDto.FromEntity(existing), false);
        var post = await db.SocialPosts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PostId, cancellationToken)
            ?? throw new PostNotFoundException(request.PostId);
        var visual = post.SelectedVisualId is null ? null : await db.PostVisuals.AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == post.SelectedVisualId, cancellationToken);
        var operation = PublicationOperation.Claim(post, request.OperationId, request.Provider, request.Destination, timeProvider, visual);
        db.PublicationOperations.Add(operation);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(PublicationOperationDto.FromEntity(operation), true);
    }

    public async Task<PublicationOperationDto> CompleteAsync(Guid operationId, PostPublicationResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        await using var db = new SocialFlowDbContext(options, workspace);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var operation = await db.PublicationOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        if (operation.CompletedAt is not null) return PublicationOperationDto.FromEntity(operation);
        var state = result.Outcome switch
        {
            PostPublicationOutcome.Confirmed => PublicationOperationState.Confirmed,
            PostPublicationOutcome.Failed => PublicationOperationState.Failed,
            PostPublicationOutcome.Indeterminate => PublicationOperationState.Indeterminate,
            _ => throw new ArgumentException("Invalid publication result.", nameof(result))
        };
        if (state != PublicationOperationState.Indeterminate)
        {
            var post = await db.SocialPosts.SingleOrDefaultAsync(x => x.Id == operation.SocialPostId, cancellationToken)
                ?? throw new PostNotFoundException(operation.SocialPostId);
            if (state == PublicationOperationState.Confirmed) post.MarkAsPublished(timeProvider);
            else post.MarkAsFailed(timeProvider);
        }
        operation.Complete(state, result.ExternalId, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PublicationOperationDto.FromEntity(operation);
    }
}

