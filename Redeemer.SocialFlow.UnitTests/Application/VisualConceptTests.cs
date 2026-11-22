using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Domain.Enums;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Application;

public sealed class VisualConceptTests
{
    private static readonly VisualIdentity Identity = new("brand", "Brand", "Flat", ["#123456"]);
    private static VisualPostContext Post => new(Guid.NewGuid(), SocialPlatform.Facebook, "A complementary scene",
        "Saved topic", "The saved editorial message", Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public void PreviewIsDeterministicAndKeepsEditorialMessageSeparateFromBrief()
    {
        var post = Post;
        var first = VisualConcept.Create(post, Identity, post.VisualBrief);
        Assert.Equal(first.Fingerprint, VisualConcept.Create(post, Identity, null).Fingerprint);
        Assert.Equal(post.Title, first.Subject);
        Assert.Equal(post.Content, first.Message);
        Assert.Equal(post.VisualBrief, first.Brief);
        Assert.Contains(post.Title, first.Scene);
        Assert.Throws<VisualConceptException>(() => VisualConcept.Create(post, Identity, "Replace the topic"));
    }

    [Theory]
    [InlineData("", "Message")]
    [InlineData("Title", "  ")]
    [InlineData("Title", "!!!")]
    public void RequiresEditorialContext(string title, string content) =>
        Assert.Equal("EditorialContextRequired", Assert.Throws<VisualConceptException>(() =>
            VisualConcept.Create(Post with { Title = title, Content = content }, Identity, null)).Code);

    [Fact]
    public void FingerprintBindsPostWorkspaceVersionContentBriefPlatformAndIdentity()
    {
        var post = Post;
        var original = VisualConcept.Create(post, Identity, null).Fingerprint;
        foreach (var changed in new[] { post with { Id = Guid.NewGuid() }, post with { WorkspaceId = Guid.NewGuid() },
            post with { UpdatedAt = post.UpdatedAt.AddSeconds(1) }, post with { Title = "New title" },
            post with { Content = "New message" }, post with { VisualBrief = "New brief" },
            post with { Platform = SocialPlatform.LinkedIn } })
            Assert.NotEqual(original, VisualConcept.Create(changed, Identity, null).Fingerprint);
        Assert.NotEqual(original, VisualConcept.Create(post, new("brand", "Brand", "Photo", ["#123456"]), null).Fingerprint);
    }
}
