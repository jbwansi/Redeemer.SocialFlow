using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Infrastructure.Publishing;

public sealed class SqlitePublicationOperationStore(DbContextOptions<SocialFlowDbContext> options,
    TimeProvider? timeProvider = null) : IPublicationOperationStore
{
    public async Task<PublicationClaim> TryClaimAsync(PublishScheduledPostRequest request, CancellationToken cancellationToken = default)
    {
        // Dedicated short-lived context: never commit unrelated caller-tracked changes.
        await using var db = new SocialFlowDbContext(options);
        // Microsoft.Data.Sqlite's default transaction takes the write reservation immediately.
        // Eligibility, existing-operation check and insert are serialized across contexts/processes.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.PublicationOperations.SingleOrDefaultAsync(x => x.OperationId == request.OperationId, cancellationToken);
        if (existing is not null && existing.SocialPostId != request.PostId)
            throw new DomainException("The operation ID already belongs to another post.");
        existing ??= await db.PublicationOperations.SingleOrDefaultAsync(x => x.SocialPostId == request.PostId, cancellationToken);
        if (existing is not null) return new(PublicationOperationDto.FromEntity(existing), false);
        var post = await db.SocialPosts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PostId, cancellationToken)
            ?? throw new PostNotFoundException(request.PostId);
        var operation = PublicationOperation.Claim(post, request.OperationId, request.Provider, request.Destination, timeProvider);
        db.PublicationOperations.Add(operation);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(PublicationOperationDto.FromEntity(operation), true);
    }

    public async Task<PublicationOperationDto> CompleteAsync(Guid operationId, PostPublicationResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        await using var db = new SocialFlowDbContext(options);
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
