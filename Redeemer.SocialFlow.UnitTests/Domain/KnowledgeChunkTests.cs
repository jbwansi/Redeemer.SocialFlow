using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Exceptions;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Domain;

public sealed class KnowledgeChunkTests
{
    [Fact]
    public void Create_PreservesContentAndAssociatesDocumentWithClock()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var chunk = KnowledgeChunk.Create(id, " Original\ncontent ", 0, 1, " Section ", new Clock(now));
        Assert.NotEqual(Guid.Empty, chunk.Id);
        Assert.Equal(id, chunk.KnowledgeDocumentId);
        Assert.Equal(" Original\ncontent ", chunk.Content);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Equal(1, chunk.PageNumber);
        Assert.Equal("Section", chunk.Section);
        Assert.Equal(now, chunk.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Create_AllowsMissingLocationAndUsesSystemTime(string? section)
    {
        var before = DateTimeOffset.UtcNow;
        var chunk = KnowledgeChunk.Create(Guid.NewGuid(), "Content", 2, section: section);
        Assert.Null(chunk.PageNumber);
        Assert.Null(chunk.Section);
        Assert.InRange(chunk.CreatedAt, before, DateTimeOffset.UtcNow);
        Assert.NotEqual(chunk.Id, KnowledgeChunk.Create(chunk.KnowledgeDocumentId, "Content", 3).Id);
    }

    [Theory]
    [InlineData(null, 0, null)]
    [InlineData(" ", 0, null)]
    [InlineData("Content", -1, null)]
    [InlineData("Content", 0, 0)]
    [InlineData("Content", 0, -1)]
    public void Create_RejectsInvalidContentOrLocation(string? content, int index, int? page) =>
        Assert.Throws<DomainException>(() => KnowledgeChunk.Create(Guid.NewGuid(), content!, index, page));

    [Fact]
    public void Create_RequiresDocumentId() =>
        Assert.Throws<DomainException>(() => KnowledgeChunk.Create(Guid.Empty, "Content", 0));

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
