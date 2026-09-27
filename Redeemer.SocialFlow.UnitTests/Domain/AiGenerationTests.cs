using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Domain;

public sealed class AiGenerationTests
{
    [Fact]
    public void Create_PreservesBriefMetadataAndOrderedWarningsWithDefensiveCopy()
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        var warnings = new[] { " Warning ", "", "Warning", " Warning " };
        var generation = AiGeneration.Create(id, " Subject ", "Objective", "Audience",
            SocialPlatform.LinkedIn, "Provider", "configured-model", warnings, new Clock(now));
        Assert.NotEqual(Guid.Empty, generation.Id);
        Assert.Equal(id, generation.SocialPostId);
        Assert.Equal(" Subject ", generation.Subject);
        Assert.Equal("Objective", generation.Objective);
        Assert.Equal("Audience", generation.Audience);
        Assert.Equal(SocialPlatform.LinkedIn, generation.Platform);
        Assert.Equal("Provider", generation.Provider);
        Assert.Equal("configured-model", generation.Model);
        Assert.Equal(now, generation.GeneratedAt);
        Assert.Equal(warnings, generation.Warnings);
        warnings[0] = "mutated";
        Assert.Equal(" Warning ", generation.Warnings[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)generation.Warnings)[0] = "mutated");
    }

    [Theory]
    [InlineData("", "O", "A", "P", "M")]
    [InlineData("S", " ", "A", "P", "M")]
    [InlineData("S", "O", null, "P", "M")]
    [InlineData("S", "O", "A", "", "M")]
    [InlineData("S", "O", "A", "P", " ")]
    public void Create_RejectsMissingBriefOrMetadata(string subject, string objective, string? audience, string provider, string model)
    {
        Assert.Throws<DomainException>(() => AiGeneration.Create(Guid.NewGuid(), subject, objective,
            audience!, SocialPlatform.Facebook, provider, model, []));
    }

    [Fact]
    public void Create_RequiresPostValidPlatformAndWarnings()
    {
        Assert.Throws<DomainException>(() => AiGeneration.Create(Guid.Empty, "S", "O", "A", SocialPlatform.Facebook, "P", "M", []));
        Assert.Throws<DomainException>(() => AiGeneration.Create(Guid.NewGuid(), "S", "O", "A", (SocialPlatform)999, "P", "M", []));
        Assert.Throws<ArgumentNullException>(() => AiGeneration.Create(Guid.NewGuid(), "S", "O", "A", SocialPlatform.Facebook, "P", "M", null!));
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
