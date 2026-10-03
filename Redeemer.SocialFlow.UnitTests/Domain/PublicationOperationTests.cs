using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Domain;

public sealed class PublicationOperationTests
{
    [Theory]
    [InlineData(SocialPostStatus.Draft)]
    [InlineData(SocialPostStatus.Approved)]
    [InlineData(SocialPostStatus.Cancelled)]
    public void Claim_RejectsPostsOutsideScheduled(SocialPostStatus status)
    {
        var clock = new Clock();
        var post = SocialPost.Create("Title", "Content", SocialPlatform.LinkedIn, clock);
        if (status != SocialPostStatus.Draft) { post.SubmitForReview(); post.Approve(); }
        if (status == SocialPostStatus.Cancelled) { post.Schedule(clock.Now.AddDays(1)); post.Cancel(); }
        Assert.Throws<DomainException>(() => PublicationOperation.Claim(post, Guid.NewGuid(), "Provider", "Target", clock));
    }

    [Fact]
    public void ClaimAndComplete_PreserveSnapshotAndRequireValidResult()
    {
        var clock = new Clock();
        var post = SocialPost.Create("Title", "Content é", SocialPlatform.LinkedIn, clock);
        post.SubmitForReview(); post.Approve(); post.Schedule(clock.Now.AddHours(1));
        Assert.Throws<DomainException>(() => PublicationOperation.Claim(post, Guid.NewGuid(), "Provider", "Target", clock));
        clock.Now = clock.Now.AddHours(1);
        Assert.Throws<DomainException>(() => PublicationOperation.Claim(post, Guid.Empty, "Provider", "Target", clock));
        Assert.Throws<DomainException>(() => PublicationOperation.Claim(post, Guid.NewGuid(), " ", "Target", clock));
        var operation = PublicationOperation.Claim(post, Guid.NewGuid(), "Provider", "Target", clock);
        Assert.Equal(PublicationOperationState.Indeterminate, operation.State);
        Assert.Null(operation.CompletedAt);
        Assert.Equal(post.Content, operation.Content);
        Assert.Equal(clock.Now, operation.ClaimedAt);
        Assert.Throws<DomainException>(() => operation.Complete(PublicationOperationState.Confirmed, " ", clock));
        Assert.Throws<DomainException>(() => operation.Complete(PublicationOperationState.Failed, "id", clock));
        Assert.Throws<DomainException>(() => operation.Complete((PublicationOperationState)999, null, clock));
        clock.Now = clock.Now.AddMinutes(2);
        operation.Complete(PublicationOperationState.Confirmed, "external", clock);
        Assert.Equal("external", operation.ExternalId);
        Assert.Equal(clock.Now, operation.CompletedAt);
        Assert.Throws<DomainException>(() => operation.Complete(PublicationOperationState.Failed, null, clock));
        Assert.Equal(SocialPostStatus.Scheduled, post.Status); // operation entity never transitions the post itself
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
