using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Domain.Enums;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Application;

public sealed class PostPublicationContractTests
{
    [Theory]
    [InlineData(SocialPlatform.Facebook)]
    [InlineData(SocialPlatform.LinkedIn)]
    [InlineData(SocialPlatform.Instagram)]
    public void Request_PreservesContentDestinationAndCallerOperationId(SocialPlatform platform)
    {
        var id = Guid.NewGuid();
        var request = new PostPublicationRequest(" Texte éè\n ", platform, " opaque-target ", id);
        Assert.Equal(" Texte éè\n ", request.Content);
        Assert.Equal(" opaque-target ", request.Destination);
        Assert.Equal(platform, request.Platform);
        Assert.Equal(id, request.OperationId);
        Assert.Equal(id, new PostPublicationRequest("Content", platform, "destination", id).OperationId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n")]
    public void Request_RejectsBlankContentAndDestination(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new PostPublicationRequest(value!, SocialPlatform.LinkedIn, "target", Guid.NewGuid()));
        Assert.ThrowsAny<ArgumentException>(() => new PostPublicationRequest("Content", SocialPlatform.LinkedIn, value!, Guid.NewGuid()));
    }

    [Fact]
    public void Request_RejectsInvalidPlatformAndEmptyOperationId()
    {
        Assert.Throws<ArgumentException>(() => new PostPublicationRequest("Content", (SocialPlatform)999, "target", Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new PostPublicationRequest("Content", SocialPlatform.LinkedIn, "target", Guid.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Confirmed_RequiresExternalIdentifier(string? id) =>
        Assert.ThrowsAny<ArgumentException>(() => PostPublicationResult.Confirmed(id!));

    [Fact]
    public void Outcomes_AreDistinctAndOnlyConfirmationContainsExternalId()
    {
        var confirmed = PostPublicationResult.Confirmed(" opaque-external-id ");
        Assert.Equal(PostPublicationOutcome.Confirmed, confirmed.Outcome);
        Assert.Equal(" opaque-external-id ", confirmed.ExternalId);
        var failed = PostPublicationResult.Failed();
        Assert.Equal(PostPublicationOutcome.Failed, failed.Outcome);
        Assert.Null(failed.ExternalId);
        var uncertain = PostPublicationResult.Indeterminate();
        Assert.Equal(PostPublicationOutcome.Indeterminate, uncertain.Outcome);
        Assert.Null(uncertain.ExternalId);
        Assert.NotEqual(failed, uncertain);
    }
}
