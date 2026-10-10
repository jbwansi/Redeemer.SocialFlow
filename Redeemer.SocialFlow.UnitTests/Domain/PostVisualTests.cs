using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Domain;

public sealed class PostVisualTests
{
    private static SocialPost Post() => SocialPost.Create("Title", "Content", SocialPlatform.LinkedIn);
    private static PostVisual Visual(SocialPost post) => PostVisual.Create(post, VisualOrigin.Uploaded,
        "image/png", 16, 12, 100, new string('a', 64), null, DateTimeOffset.UtcNow);
    [Fact]
    public void SelectionRequiresReadyOwnedCandidateAndPreservesLegacyUrl()
    {
        var post = Post();
        post.Update("Title", "Content", null, "Brief", "https://example.com/legacy.png");
        var visual = Visual(post);
        Assert.Throws<DomainException>(() => post.SelectVisual(visual, DateTimeOffset.UtcNow));
        visual.Complete();
        Assert.Throws<DomainException>(() => Post().SelectVisual(visual, DateTimeOffset.UtcNow));
        post.SelectVisual(visual, DateTimeOffset.UtcNow);
        Assert.Equal(visual.Id, post.SelectedVisualId);
        Assert.NotNull(visual.ValidatedAt);
        Assert.Equal("https://example.com/legacy.png", post.VisualUrl);
        Assert.Equal("Brief", post.VisualBrief);
    }
    [Fact]
    public void DeletedCandidatesCannotBeCompletedOrSelected()
    {
        var post = Post();
        var visual = Visual(post);
        visual.Delete(DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => visual.Complete());
        Assert.Throws<DomainException>(() => post.SelectVisual(visual, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => Visual(post).MarkPurged(DateTimeOffset.UtcNow));
    }
    [Theory]
    [InlineData("review")]
    [InlineData("approved")]
    [InlineData("scheduled")]
    [InlineData("cancelled")]
    [InlineData("published")]
    [InlineData("failed")]
    [InlineData("deleted")]
    public void NonEditablePostsRejectVisualMutations(string state)
    {
        var post = Post();
        var visual = Visual(post);
        visual.Complete();
        if (state == "deleted") post.SoftDelete();
        else
        {
            post.SubmitForReview();
            if (state != "review") post.Approve();
            if (state is "scheduled" or "cancelled" or "published" or "failed") post.Schedule(DateTimeOffset.UtcNow.AddDays(1));
            if (state == "cancelled") post.Cancel();
            if (state == "published") post.MarkAsPublished();
            if (state == "failed") post.MarkAsFailed();
        }
        Assert.Throws<DomainException>(() => Visual(post));
        Assert.Throws<DomainException>(() => post.SelectVisual(visual, DateTimeOffset.UtcNow));
    }
    [Fact]
    public void RejectedPostsCanSelectAnotherVersion()
    {
        var post = Post();
        var first = Visual(post);
        first.Complete();
        post.SelectVisual(first, DateTimeOffset.UtcNow);
        post.SubmitForReview();
        post.Reject();
        var second = Visual(post);
        second.Complete();
        post.SelectVisual(second, DateTimeOffset.UtcNow);
        Assert.Equal(second.Id, post.SelectedVisualId);
        Assert.False(first.IsDeleted);
        Assert.NotNull(first.ValidatedAt);
    }
}
