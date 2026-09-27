using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Persistence;

public sealed class SoftDeleteMigrationTests
{
    [Fact]
    public async Task Upgrade_PreservesExistingPostAndAudit_AndRestrictsPhysicalDeletion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(connection).Options;
        await using var context = new SocialFlowDbContext(options);
        await context.GetService<IMigrator>().MigrateAsync("20260927022624_AddAiGeneration");
        var postId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        // Seed the previous schema directly: it has no soft-delete columns yet.
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SocialPosts (Id, Title, Content, Platform, Status, CreatedAt, UpdatedAt) VALUES ({postId}, 'Existing', 'Body', 1, 1, {now}, {now})");
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AiGenerations (Id, SocialPostId, Subject, Objective, Audience, Platform, Provider, Model, Warnings, GeneratedAt) VALUES ({auditId}, {postId}, 'S', 'O', 'A', 1, 'Provider', 'Model', '[\" Warning \",\" Warning \"]', {now})");
        await context.Database.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        var post = await context.SocialPosts.SingleAsync();
        Assert.Equal(postId, post.Id);
        Assert.Equal("Existing", post.Title);
        Assert.False(post.IsDeleted);
        Assert.Null(post.DeletedAt);
        var audit = await context.AiGenerations.SingleAsync();
        Assert.Equal(auditId, audit.Id);
        Assert.Equal(new[] { " Warning ", " Warning " }, audit.Warnings);
        Assert.Equal("Provider", audit.Provider);
        Assert.Equal("Model", audit.Model);
        await new SocialPostService(context).DeleteAsync(postId);
        // Queries must respect deletion even when the entity is already tracked.
        var service = new SocialPostService(context);
        Assert.Null(await service.GetByIdAsync(postId));
        Assert.Empty(await service.ListAsync());
        await Assert.ThrowsAsync<PostNotFoundException>(() => service.SubmitForReviewAsync(postId));
        Assert.Single(await context.SocialPosts.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await context.AiGenerations.ToListAsync());
        context.ChangeTracker.Clear();
        context.SocialPosts.Remove(await context.SocialPosts.IgnoreQueryFilters().SingleAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        await using var read = new SocialFlowDbContext(options);
        Assert.Single(await read.SocialPosts.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await read.AiGenerations.ToListAsync());
    }
}
