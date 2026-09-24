using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Domain;

public class SocialPostTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SocialPlatform.Facebook)]
    [InlineData(SocialPlatform.LinkedIn)]
    [InlineData(SocialPlatform.Instagram)]
    public void Create_InitializesDraft(SocialPlatform platform)
    {
        var post = SocialPost.Create(" Title ", " Content ", platform, new TestClock());

        Assert.NotEqual(Guid.Empty, post.Id);
        Assert.Equal("Title", post.Title);
        Assert.Equal("Content", post.Content);
        Assert.Equal(platform, post.Platform);
        Assert.Equal(SocialPostStatus.Draft, post.Status);
        Assert.Equal(InitialTime, post.CreatedAt);
        Assert.Equal(InitialTime, post.UpdatedAt);
        Assert.Null(post.CallToAction);
        Assert.Null(post.VisualBrief);
        Assert.Null(post.VisualUrl);
        Assert.Null(post.ScheduledAt);
        Assert.Null(post.PublishedAt);
    }

    [Fact]
    public void Create_UsesSystemClockAndUniqueIdentityByDefault()
    {
        var before = DateTimeOffset.UtcNow;
        var first = SocialPost.Create("Title", null, SocialPlatform.Facebook);
        var second = SocialPost.Create("Title", null, SocialPlatform.Facebook);

        Assert.NotEqual(first.Id, second.Id);
        Assert.InRange(first.CreatedAt, before, DateTimeOffset.UtcNow);
        Assert.Equal(first.CreatedAt, first.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Create_RequiresTitle(string? title)
    {
        Assert.Throws<DomainException>(() => SocialPost.Create(title!, "Content", SocialPlatform.Facebook));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void Create_RejectsUndefinedPlatform(int platform)
    {
        Assert.Throws<DomainException>(() => SocialPost.Create("Title", "Content", (SocialPlatform)platform));
    }

    public static IEnumerable<object[]> WorkflowCases()
    {
        foreach (var status in Enum.GetValues<SocialPostStatus>())
        foreach (var operation in Enum.GetValues<Operation>())
        {
            SocialPostStatus? expected = (status, operation) switch
            {
                (SocialPostStatus.Draft or SocialPostStatus.Rejected, Operation.Submit) => SocialPostStatus.ReadyForReview,
                (SocialPostStatus.ReadyForReview, Operation.Approve) => SocialPostStatus.Approved,
                (SocialPostStatus.ReadyForReview, Operation.Reject) => SocialPostStatus.Rejected,
                (SocialPostStatus.Approved, Operation.Schedule) => SocialPostStatus.Scheduled,
                (SocialPostStatus.Scheduled, Operation.Publish) => SocialPostStatus.Published,
                (SocialPostStatus.Scheduled, Operation.Fail) => SocialPostStatus.Failed,
                (SocialPostStatus.Scheduled, Operation.Cancel) => SocialPostStatus.Cancelled,
                _ => null
            };
            yield return new object[] { status, operation, expected! };
        }
    }

    [Theory]
    [MemberData(nameof(WorkflowCases))]
    public void Workflow_EnforcesEveryTransition(
        SocialPostStatus source, Operation operation, SocialPostStatus? expected)
    {
        var clock = new TestClock();
        var post = PostIn(source, clock);
        var before = Snapshot(post);
        var originalSchedule = post.ScheduledAt;
        clock.Now = InitialTime.AddMinutes(1);
        var scheduledAt = clock.Now.AddDays(1);

        if (expected is null)
        {
            Assert.Throws<DomainException>(() => Apply(post, operation, scheduledAt));
            Assert.Equal(before, Snapshot(post));
            return;
        }

        Apply(post, operation, scheduledAt);

        Assert.Equal(expected.Value, post.Status);
        Assert.Equal(InitialTime, post.CreatedAt);
        Assert.Equal(clock.Now, post.UpdatedAt);
        Assert.Equal(operation == Operation.Publish ? clock.Now : (DateTimeOffset?)null, post.PublishedAt);
        Assert.Equal(operation switch
        {
            Operation.Schedule => scheduledAt,
            Operation.Cancel => null,
            _ => originalSchedule
        }, post.ScheduledAt);
    }

    [Theory]
    [InlineData(SocialPostStatus.Draft, null)]
    [InlineData(SocialPostStatus.Draft, "")]
    [InlineData(SocialPostStatus.Draft, " \t\n")]
    [InlineData(SocialPostStatus.Rejected, null)]
    [InlineData(SocialPostStatus.Rejected, "")]
    [InlineData(SocialPostStatus.Rejected, " \t\n")]
    public void SubmitForReview_RequiresContent(SocialPostStatus status, string? content)
    {
        var clock = new TestClock();
        var post = PostIn(status, clock);
        post.Update("Title", content, null, null);
        Assert.Equal(string.Empty, post.Content);
        var before = Snapshot(post);
        clock.Now = InitialTime.AddMinutes(1);

        Assert.Throws<DomainException>(post.SubmitForReview);
        Assert.Equal(before, Snapshot(post));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n")]
    public void Create_AllowsMissingContentInDraft(string? content)
    {
        var post = SocialPost.Create("Title", content, SocialPlatform.LinkedIn);
        Assert.Equal(string.Empty, post.Content);
        Assert.Equal(SocialPostStatus.Draft, post.Status);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Schedule_RequiresStrictlyFutureInstant(long ticks)
    {
        var clock = new TestClock();
        var post = PostIn(SocialPostStatus.Approved, clock);
        var before = Snapshot(post);
        var date = clock.Now.AddTicks(ticks).ToOffset(TimeSpan.FromHours(2));

        if (ticks <= 0)
        {
            Assert.Throws<DomainException>(() => post.Schedule(date));
            Assert.Equal(before, Snapshot(post));
        }
        else
        {
            post.Schedule(date);
            Assert.Equal(date, post.ScheduledAt);
            Assert.Equal(SocialPostStatus.Scheduled, post.Status);
        }
    }

    [Theory]
    [InlineData(SocialPostStatus.Draft)]
    [InlineData(SocialPostStatus.Rejected)]
    public void Update_ChangesEditablePostAndCanClearOptionalFields(SocialPostStatus status)
    {
        var clock = new TestClock();
        var post = PostIn(status, clock);
        var id = post.Id;
        clock.Now = InitialTime.AddMinutes(1);

        post.Update(" New title ", " New content ", " Learn more ", " Bright image ", " https://example.com/image.png ");

        Assert.Equal("New title", post.Title);
        Assert.Equal("New content", post.Content);
        Assert.Equal("Learn more", post.CallToAction);
        Assert.Equal("Bright image", post.VisualBrief);
        Assert.Equal("https://example.com/image.png", post.VisualUrl);
        Assert.Equal(status, post.Status);
        Assert.Equal(id, post.Id);
        Assert.Equal(SocialPlatform.Facebook, post.Platform);
        Assert.Equal(InitialTime, post.CreatedAt);
        Assert.Equal(clock.Now, post.UpdatedAt);

        post.Update("Title", null, null, null, null);
        Assert.Equal(string.Empty, post.Content);
        Assert.Null(post.CallToAction);
        Assert.Null(post.VisualBrief);
        Assert.Null(post.VisualUrl);
    }

    [Theory]
    [InlineData(SocialPostStatus.ReadyForReview)]
    [InlineData(SocialPostStatus.Approved)]
    [InlineData(SocialPostStatus.Scheduled)]
    [InlineData(SocialPostStatus.Published)]
    [InlineData(SocialPostStatus.Failed)]
    [InlineData(SocialPostStatus.Cancelled)]
    public void Update_RejectsChangesOutsideDraftAndRejected(SocialPostStatus status)
    {
        var clock = new TestClock();
        var post = PostIn(status, clock);
        var before = Snapshot(post);
        clock.Now = InitialTime.AddMinutes(1);

        Assert.Throws<DomainException>(() => post.Update("Changed", "Changed", "CTA", "Brief", "url"));
        Assert.Equal(before, Snapshot(post));
    }

    [Theory]
    [InlineData(SocialPostStatus.Draft, null)]
    [InlineData(SocialPostStatus.Draft, "")]
    [InlineData(SocialPostStatus.Draft, " \t")]
    [InlineData(SocialPostStatus.Rejected, null)]
    [InlineData(SocialPostStatus.Rejected, "")]
    [InlineData(SocialPostStatus.Rejected, " \t")]
    public void Update_RequiresTitleWithoutPartialMutation(SocialPostStatus status, string? title)
    {
        var clock = new TestClock();
        var post = PostIn(status, clock);
        var before = Snapshot(post);
        clock.Now = InitialTime.AddMinutes(1);

        Assert.Throws<DomainException>(() => post.Update(title!, "Changed", "Changed", "Changed", "Changed"));
        Assert.Equal(before, Snapshot(post));
    }

    [Fact]
    public void RejectedPost_CanBeEditedResubmittedApprovedAndPublished()
    {
        var clock = new TestClock();
        var post = PostIn(SocialPostStatus.Rejected, clock);
        post.Update("Revised", "Revised content", "Read more", "Photo", "https://example.com/photo.png");
        post.SubmitForReview();
        post.Approve();
        var scheduledAt = clock.Now.AddHours(1);
        post.Schedule(scheduledAt);
        clock.Now = scheduledAt;
        post.MarkAsPublished();

        Assert.Equal(SocialPostStatus.Published, post.Status);
        Assert.Equal(scheduledAt, post.PublishedAt);
        Assert.Equal(scheduledAt, post.ScheduledAt);
        Assert.Equal("Revised content", post.Content);
    }

    [Theory]
    [InlineData(SocialPostStatus.Draft, true)]
    [InlineData(SocialPostStatus.Rejected, true)]
    [InlineData(SocialPostStatus.ReadyForReview, false)]
    [InlineData(SocialPostStatus.Approved, false)]
    [InlineData(SocialPostStatus.Scheduled, false)]
    [InlineData(SocialPostStatus.Published, false)]
    [InlineData(SocialPostStatus.Failed, false)]
    [InlineData(SocialPostStatus.Cancelled, false)]
    public void EnsureCanDelete_OnlyAllowsDraftAndRejectedWithoutMutation(SocialPostStatus status, bool allowed)
    {
        var post = PostIn(status, new TestClock());
        var before = Snapshot(post);
        if (allowed) post.EnsureCanDelete();
        else Assert.Throws<DomainException>(post.EnsureCanDelete);
        Assert.Equal(before, Snapshot(post));
    }

    private static SocialPost PostIn(SocialPostStatus status, TestClock clock)
    {
        var post = SocialPost.Create("Title", "Content", SocialPlatform.Facebook, clock);
        post.Update("Title", "Content", "CTA", "Brief", "https://example.com/image.png");
        if (status == SocialPostStatus.Draft) return post;
        post.SubmitForReview();
        if (status == SocialPostStatus.ReadyForReview) return post;
        if (status == SocialPostStatus.Rejected)
        {
            post.Reject();
            return post;
        }
        post.Approve();
        if (status == SocialPostStatus.Approved) return post;
        post.Schedule(clock.Now.AddDays(1));
        switch (status)
        {
            case SocialPostStatus.Published: post.MarkAsPublished(); break;
            case SocialPostStatus.Failed: post.MarkAsFailed(); break;
            case SocialPostStatus.Cancelled: post.Cancel(); break;
        }
        return post;
    }

    private static void Apply(SocialPost post, Operation operation, DateTimeOffset scheduledAt)
    {
        switch (operation)
        {
            case Operation.Submit: post.SubmitForReview(); break;
            case Operation.Approve: post.Approve(); break;
            case Operation.Reject: post.Reject(); break;
            case Operation.Schedule: post.Schedule(scheduledAt); break;
            case Operation.Publish: post.MarkAsPublished(); break;
            case Operation.Fail: post.MarkAsFailed(); break;
            case Operation.Cancel: post.Cancel(); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static object Snapshot(SocialPost post) => new
    {
        post.Id, post.Title, post.Content, post.Platform, post.Status,
        post.CallToAction, post.VisualBrief, post.VisualUrl,
        post.ScheduledAt, post.PublishedAt, post.CreatedAt, post.UpdatedAt
    };

    public enum Operation { Submit, Approve, Reject, Schedule, Publish, Fail, Cancel }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = InitialTime;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
