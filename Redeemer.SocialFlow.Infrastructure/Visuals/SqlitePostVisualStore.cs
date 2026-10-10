using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Infrastructure.Visuals;

public sealed class SqlitePostVisualStore(DbContextOptions<SocialFlowDbContext> options, IVisualStorage storage,
    TimeProvider? timeProvider = null) : IPostVisualStore
{
    private TimeProvider Clock => timeProvider ?? TimeProvider.System;
    private SocialFlowDbContext Db() => new(options);
    private static async Task<SocialPost> Post(SocialFlowDbContext db, Guid id, CancellationToken token) =>
        await db.SocialPosts.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new PostNotFoundException(id);
    private static async Task<PostVisual> Visual(SocialFlowDbContext db, Guid postId, Guid id, CancellationToken token) =>
        await db.PostVisuals.SingleOrDefaultAsync(x => x.SocialPostId == postId && x.Id == id, token) ?? throw new VisualNotFoundException();
    private static VisualDto Dto(PostVisual v, SocialPost p) => new(v.Id, v.SocialPostId, v.Origin, v.ContentType,
        v.Width, v.Height, v.ByteLength, v.Sha256, v.CreatedAt, v.ValidatedAt, p.SelectedVisualId == v.Id,
        v.GenerationMetadataJson is null ? null : JsonSerializer.Deserialize<VisualGenerationMetadata>(v.GenerationMetadataJson));

    public async Task<VisualPostContext> EditablePostAsync(Guid postId, CancellationToken token)
    {
        await using var db = Db();
        var post = await Post(db, postId, token);
        post.EnsureCanManageVisuals();
        return new(post.Id, post.Platform, post.VisualBrief);
    }
    public async Task<VisualReservation> ReserveAsync(Guid postId, NewVisual visual, CancellationToken token)
    {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var post = await Post(db, postId, token);
        var entity = PostVisual.Create(post, visual.Origin, visual.Image.ContentType, visual.Image.Width,
            visual.Image.Height, visual.ByteLength, visual.Sha256,
            visual.Generation is null ? null : JsonSerializer.Serialize(visual.Generation), Clock.GetUtcNow());
        db.PostVisuals.Add(entity);
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return new(entity.Id, entity.StorageKey);
    }
    public async Task<VisualDto> CompleteAsync(Guid postId, Guid visualId, CancellationToken token)
    {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var post = await Post(db, postId, token);
        post.EnsureCanManageVisuals();
        var visual = await Visual(db, postId, visualId, token);
        visual.Complete();
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return Dto(visual, post);
    }
    public async Task AbandonAsync(Guid postId, Guid visualId, CancellationToken token)
    {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var visual = await Visual(db, postId, visualId, token);
        // If the completion commit succeeded despite a lost acknowledgement, retain the ready candidate.
        if (!visual.IsReady) visual.Delete(Clock.GetUtcNow());
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }
    public async Task<IReadOnlyList<VisualDto>> ListAsync(Guid postId, CancellationToken token)
    {
        await using var db = Db();
        var post = await Post(db, postId, token);
        var visuals = await db.PostVisuals.AsNoTracking().Where(x => x.SocialPostId == postId && x.IsReady && !x.IsDeleted).ToListAsync(token);
        return visuals.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Select(v => Dto(v, post)).ToArray();
    }
    public async Task<(VisualDto Visual, string Key)> GetAsync(Guid postId, Guid visualId, CancellationToken token)
    {
        await using var db = Db();
        var post = await Post(db, postId, token);
        var visual = await Visual(db, postId, visualId, token);
        if (visual.IsDeleted || !visual.IsReady) throw new VisualNotFoundException();
        return (Dto(visual, post), visual.StorageKey);
    }
    public async Task<VisualDto> SelectAsync(Guid postId, Guid visualId, CancellationToken token)
    {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var post = await Post(db, postId, token);
        var visual = await Visual(db, postId, visualId, token);
        post.SelectVisual(visual, Clock.GetUtcNow());
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return Dto(visual, post);
    }
    public async Task DeleteAsync(Guid postId, Guid visualId, CancellationToken token)
    {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var post = await Post(db, postId, token);
        post.EnsureCanManageVisuals();
        if (post.SelectedVisualId == visualId) throw new DomainException("Select another visual before deleting this candidate.");
        var visual = await Visual(db, postId, visualId, token);
        visual.Delete(Clock.GetUtcNow());
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }
    public async Task<int> CleanupAsync(DateTimeOffset cutoff, CancellationToken token)
    {
        // Minimum retention 24 hours. No endpoint/worker automatically invokes this maintenance.
        if (cutoff > Clock.GetUtcNow().AddDays(-1)) throw new ArgumentException("Retention must be at least 24 hours.");
        await using var db = Db();
        // Serialize retirements against selection, reservation completion and publication claims.
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var candidates = await db.PostVisuals.Where(v =>
            (v.IsDeleted || !v.IsReady) &&
            !db.SocialPosts.IgnoreQueryFilters().Any(p => p.SelectedVisualId == v.Id) &&
            !db.PublicationOperations.Any(o => o.SocialPostId == v.SocialPostId)).ToListAsync(token);
        int removed = 0;
        foreach (var visual in candidates.Where(v => (v.DeletedAt ?? v.CreatedAt) <= cutoff).OrderBy(v => v.PurgedAt ?? DateTimeOffset.MinValue).ThenBy(v => v.Id).Take(100))
        {
            if (!visual.IsDeleted) visual.Delete(Clock.GetUtcNow());
            // Revisit purged keys too: a stale interrupted writer must not leave a permanent orphan.
            await storage.DeleteAsync(visual.StorageKey, token);
            visual.MarkPurged(Clock.GetUtcNow());
            removed++;
        }
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return removed;
    }
}
