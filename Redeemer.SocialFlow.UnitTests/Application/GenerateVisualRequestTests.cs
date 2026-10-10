using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Application;

public sealed class GenerateVisualRequestTests
{
    private static VisualIdentity Identity => new("brand", "Example", "Simple", ["#123456"]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyBriefIsRejected(string? brief) =>
        Assert.Throws<ArgumentException>(() => new GenerateVisualRequest(brief!, SocialPlatform.LinkedIn, Identity));

    [Fact]
    public void LengthAndEnumsAreValidated()
    {
        Assert.Throws<ArgumentException>(() => new GenerateVisualRequest(new string('a', 4001), SocialPlatform.LinkedIn, Identity));
        Assert.Throws<ArgumentException>(() => new GenerateVisualRequest("Brief", (SocialPlatform)999, Identity));
        Assert.Throws<ArgumentException>(() => new GenerateVisualRequest("Brief", SocialPlatform.LinkedIn, Identity, (VisualLayout)999));
        Assert.Throws<ArgumentNullException>(() => new GenerateVisualRequest("Brief", SocialPlatform.LinkedIn, null!));
        Assert.Equal(4000, new GenerateVisualRequest(new string('a', 4000), SocialPlatform.LinkedIn, Identity).VisualBrief.Length);
    }

    [Fact]
    public void ExistingPostBriefCanBeUsedWithoutMutatingPost()
    {
        var post = SocialPost.Create("Title", "Content", SocialPlatform.LinkedIn);
        post.Update("Title", "Content", null, "  Existing visual brief  ");
        var updated = post.UpdatedAt;
        var request = new GenerateVisualRequest(post.VisualBrief!, post.Platform, Identity);
        Assert.Equal(post.VisualBrief, request.VisualBrief);
        Assert.Equal(updated, post.UpdatedAt);
        Assert.Null(post.VisualUrl);
        Assert.Equal(SocialPostStatus.Draft, post.Status);
    }

    [Fact]
    public void IdentityPaletteIsCopied()
    {
        var colors = new[] { "#123456" };
        var identity = new VisualIdentity("id", "Name", "Style", colors);
        colors[0] = "#FFFFFF";
        Assert.Equal("#123456", identity.Colors[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)identity.Colors)[0] = "#000000");
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#XYZXYZ")]
    [InlineData("#123")]
    public void InvalidColorsAreRejected(string color) =>
        Assert.Throws<ArgumentException>(() => new VisualIdentity("id", "Name", "Style", [color]));

    [Fact]
    public void IdentityLimitsAreValidated()
    {
        Assert.Throws<ArgumentException>(() => new VisualIdentity("", "Name", "Style", ["#123456"]));
        Assert.Throws<ArgumentException>(() => new VisualIdentity("id", "", "Style", ["#123456"]));
        Assert.Throws<ArgumentException>(() => new VisualIdentity("id", "Name", new string('x', 1001), ["#123456"]));
        Assert.Throws<ArgumentException>(() => new VisualIdentity("id", "Name", "Style", []));
        Assert.Throws<ArgumentException>(() => new VisualIdentity("id", "Name", "Style", Enumerable.Repeat("#123456", 9)));
        Assert.Throws<ArgumentException>(() => new VisualIdentity("id", "Name", "Style", ["#123456"], new string('x', 301)));
    }
}
