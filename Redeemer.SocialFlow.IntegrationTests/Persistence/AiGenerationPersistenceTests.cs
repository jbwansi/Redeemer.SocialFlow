using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.SocialPosts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Persistence;

public sealed class AiGenerationPersistenceTests
{
    [Fact]
    public async Task SupportsMultipleAuditsAndPreservesThemAfterSoftDelete()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(connection).Options;
        await using var context = new SocialFlowDbContext(options);
        await context.Database.MigrateAsync();
        var post = SocialPost.Create("Existing", "Body", SocialPlatform.LinkedIn);
        context.SocialPosts.Add(post);
        await context.SaveChangesAsync();
        await context.Database.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        var warnings = new[] { " Attention éè \"quoted\"\n", "", "duplicate", "duplicate" };
        var first = AiGeneration.Create(post.Id, " Original subject ", "Objective", "Audience",
            post.Platform, "Provider", "model-1", warnings);
        context.AiGenerations.AddRange(first, AiGeneration.Create(post.Id, "Second", "O", "A",
            post.Platform, "Provider", "model-2", []));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var stored = await context.AiGenerations.SingleAsync(x => x.Id == first.Id);
        Assert.Equal(warnings, stored.Warnings);
        Assert.Equal(first.GeneratedAt, stored.GeneratedAt);
        Assert.Equal(" Original subject ", stored.Subject);
        Assert.Equal("Existing", (await context.SocialPosts.SingleAsync()).Title);
        Assert.Equal(2, await context.AiGenerations.CountAsync());
        Assert.False(context.ChangeTracker.HasChanges());
        context.ChangeTracker.Clear();
        (await context.SocialPosts.SingleAsync()).SoftDelete();
        await context.SaveChangesAsync();
        Assert.Equal(2, await context.AiGenerations.CountAsync());
        Assert.Empty(await context.SocialPosts.ToListAsync());
        Assert.True((await context.SocialPosts.IgnoreQueryFilters().SingleAsync()).IsDeleted);
    }

    [Fact]
    public async Task ForeignKey_RejectsAuditWithoutExistingPost()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new SocialFlowDbContext(new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(connection).Options);
        await context.Database.MigrateAsync();
        context.AiGenerations.Add(AiGeneration.Create(Guid.NewGuid(), "S", "O", "A", SocialPlatform.Facebook, "P", "M", []));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Empty(await context.AiGenerations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AuditInsertFailure_RollsBackPostInSameSave()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(connection).Options;
        await using (var context = new SocialFlowDbContext(options))
        {
            await context.Database.MigrateAsync();
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailAudit BEFORE INSERT ON AiGenerations BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
            var useCase = new GenerateSocialPostDraft(new Generator(), context);
            await Assert.ThrowsAsync<DbUpdateException>(() => useCase.ExecuteAsync(new("S", "O", "A", SocialPlatform.Facebook)));
        }
        await using var read = new SocialFlowDbContext(options);
        Assert.Empty(await read.SocialPosts.ToListAsync());
        Assert.Empty(await read.AiGenerations.ToListAsync());
    }

    private sealed class Generator : IContentGenerator
    {
        public Task<ContentGenerationResult> GenerateAsync(GenerateContentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContentGenerationResult(new("Title", "Body", null, null, []), new("Provider", "Model")));
    }
}
