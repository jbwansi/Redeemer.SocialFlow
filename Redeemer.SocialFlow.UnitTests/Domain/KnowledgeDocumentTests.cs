using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Domain;

public sealed class KnowledgeDocumentTests
{
    private static KnowledgeDocument Create(TimeProvider? clock = null) => KnowledgeDocument.Create(
        " Title ", " Theme ", ["Theme", "Other"], [KnowledgeUsage.Generation, KnowledgeUsage.Research],
        SourceType.InternalOfficial, AuthorityLevel.High, " fr ", timeProvider: clock);

    [Fact]
    public void Create_InitializesDraftAndMetadataWithClock()
    {
        var clock = new Clock();
        var document = Create(clock);
        Assert.NotEqual(Guid.Empty, document.Id);
        Assert.Equal("Title", document.Title);
        Assert.Equal("Theme", document.PrimaryTheme);
        Assert.Equal(new[] { "Theme", "Other" }, document.Themes);
        Assert.Equal(new[] { KnowledgeUsage.Generation, KnowledgeUsage.Research }, document.Usages);
        Assert.Equal(SourceType.InternalOfficial, document.SourceType);
        Assert.Equal("fr", document.Language);
        Assert.Equal(AuthorityLevel.High, document.AuthorityLevel);
        Assert.Equal(1, document.Version);
        Assert.True(document.IsActive);
        Assert.Equal(KnowledgeDocumentStatus.Draft, document.Status);
        Assert.Equal(clock.Now, document.CreatedAt);
        Assert.Equal(clock.Now, document.UpdatedAt);
        Assert.False(document.CanBeUsedForGeneration);
    }

    public static IEnumerable<object[]> EligibilityCases() =>
        from status in Enum.GetValues<KnowledgeDocumentStatus>()
        from active in new[] { true, false }
        from generation in new[] { true, false }
        select new object[] { status, active, generation };

    [Theory]
    [MemberData(nameof(EligibilityCases))]
    public void GenerationEligibility_RequiresAllThreeConditions(KnowledgeDocumentStatus status, bool active, bool generation)
    {
        var document = KnowledgeDocument.Create("T", "Theme", ["Theme"],
            generation ? [KnowledgeUsage.Internal, KnowledgeUsage.Generation] : [KnowledgeUsage.Research],
            SourceType.InternalOfficial, AuthorityLevel.Low, "fr", isActive: active);
        document.ChangeStatus(status);
        Assert.Equal(status == KnowledgeDocumentStatus.Approved && active && generation,
            document.CanBeUsedForGeneration);
    }

    [Fact]
    public void Collections_AreDefensivelyCopiedAndReadOnly()
    {
        var themes = new[] { "Theme", "Theme", "Other" };
        var usages = new[] { KnowledgeUsage.Generation, KnowledgeUsage.Generation };
        var document = KnowledgeDocument.Create("T", "Theme", themes, usages, SourceType.InternalOfficial, AuthorityLevel.Medium, "en");
        themes[0] = "Changed";
        usages[0] = KnowledgeUsage.Internal;
        Assert.Equal(new[] { "Theme", "Other" }, document.Themes);
        Assert.Equal(new[] { KnowledgeUsage.Generation }, document.Usages);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)document.Themes)[0] = "Changed");
        Assert.Throws<NotSupportedException>(() => ((IList<KnowledgeUsage>)document.Usages).Clear());
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("theme")]
    public void Create_RejectsPrimaryThemeOutsideThemes(string primary) =>
        Assert.Throws<DomainException>(() => KnowledgeDocument.Create("T", primary, ["Theme"], [], SourceType.InternalOfficial, AuthorityLevel.Low, "fr"));

    [Fact]
    public void Update_IsAtomicAndMaintainsThemeInvariant()
    {
        var clock = new Clock();
        var document = Create(clock);
        var created = document.CreatedAt;
        clock.Now = clock.Now.AddHours(1);
        Assert.Throws<DomainException>(() => document.Update("Changed", "Missing", ["Other"], [], SourceType.Other, AuthorityLevel.Low, "en", 2));
        Assert.Equal("Title", document.Title);
        Assert.Equal("Theme", document.PrimaryTheme);
        Assert.Equal(created, document.UpdatedAt);
        document.Update("Changed", "New", ["New"], [], SourceType.Other, AuthorityLevel.Low, "en", 2);
        Assert.Contains(document.PrimaryTheme, document.Themes);
        Assert.Equal(2, document.Version);
        Assert.Equal(created, document.CreatedAt);
        Assert.Equal(clock.Now, document.UpdatedAt);
        document.ChangeStatus(KnowledgeDocumentStatus.Approved);
        Assert.False(document.CanBeUsedForGeneration); // No permitted usages.
        clock.Now = clock.Now.AddHours(1);
        document.SetActive(false);
        Assert.False(document.IsActive);
        Assert.Equal(clock.Now, document.UpdatedAt);
    }

    [Theory]
    [InlineData("", "Theme", SourceType.InternalOfficial, "fr", 1)]
    [InlineData("T", " ", SourceType.InternalOfficial, "fr", 1)]
    [InlineData("T", "Theme", (SourceType)0, "fr", 1)]
    [InlineData("T", "Theme", SourceType.InternalOfficial, " ", 1)]
    [InlineData("T", "Theme", SourceType.InternalOfficial, "fr", 0)]
    [InlineData("T", "Theme", SourceType.InternalOfficial, "fr", -1)]
    public void Create_RejectsInvalidMetadata(string title, string primary, SourceType source, string language, int version) =>
        Assert.Throws<DomainException>(() => KnowledgeDocument.Create(title, primary, ["Theme"], [], source, AuthorityLevel.High, language, version));

    [Fact]
    public void RejectsInvalidCollectionsAndEnums()
    {
        Assert.Throws<ArgumentNullException>(() => KnowledgeDocument.Create("T", "Theme", null!, [], SourceType.Other, AuthorityLevel.Low, "en"));
        Assert.Throws<ArgumentNullException>(() => KnowledgeDocument.Create("T", "Theme", ["Theme"], null!, SourceType.Other, AuthorityLevel.Low, "en"));
        Assert.Throws<DomainException>(() => KnowledgeDocument.Create("T", "Theme", [], [], SourceType.Other, AuthorityLevel.Low, "en"));
        Assert.Throws<DomainException>(() => KnowledgeDocument.Create("T", "Theme", ["Theme", " "], [], SourceType.Other, AuthorityLevel.Low, "en"));
        Assert.Throws<DomainException>(() => KnowledgeDocument.Create("T", "Theme", ["Theme"], [(KnowledgeUsage)999], SourceType.Other, AuthorityLevel.Low, "en"));
        Assert.Throws<DomainException>(() => KnowledgeDocument.Create("T", "Theme", ["Theme"], [], SourceType.Other, (AuthorityLevel)999, "en"));
        var document = Create();
        Assert.Throws<DomainException>(() => document.ChangeStatus((KnowledgeDocumentStatus)999));
        Assert.Equal(KnowledgeDocumentStatus.Draft, document.Status);
    }

    [Fact]
    public void Create_UsesSystemTimeAndUniqueIdentity()
    {
        var before = DateTimeOffset.UtcNow;
        var document = Create();
        Assert.InRange(document.CreatedAt, before, DateTimeOffset.UtcNow);
        Assert.NotEqual(document.Id, Create().Id);
    }

    [Theory]
    [InlineData(SourceType.InternalOfficial)]
    [InlineData(SourceType.OfficialExternal)]
    [InlineData(SourceType.Book)]
    [InlineData(SourceType.Academic)]
    [InlineData(SourceType.ProfessionalArticle)]
    [InlineData(SourceType.PersonalNotes)]
    [InlineData(SourceType.Other)]
    public void CreateAndUpdate_AcceptSourceTypeWithoutInferringAuthority(SourceType source)
    {
        var document = KnowledgeDocument.Create("T", "Theme", ["Theme"], [], source, AuthorityLevel.Low, "en");
        Assert.Equal(source, document.SourceType);
        Assert.Equal(AuthorityLevel.Low, document.AuthorityLevel);
        document.Update("T", "Theme", ["Theme"], [], source, AuthorityLevel.High, "en", 1);
        Assert.Equal(source, document.SourceType);
        Assert.Equal(AuthorityLevel.High, document.AuthorityLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(8)]
    public void UndefinedSourceType_IsRejectedWithoutMutatingDocument(int source)
    {
        Assert.Throws<DomainException>(() => KnowledgeDocument.Create("T", "Theme", ["Theme"], [],
            (SourceType)source, AuthorityLevel.Low, "en"));
        var document = Create();
        var updatedAt = document.UpdatedAt;
        Assert.Throws<DomainException>(() => document.Update("Changed", "New", ["New"], [],
            (SourceType)source, AuthorityLevel.Low, "en", 2));
        Assert.Equal(SourceType.InternalOfficial, document.SourceType);
        Assert.Equal("Title", document.Title);
        Assert.Equal("Theme", document.PrimaryTheme);
        Assert.Equal(AuthorityLevel.High, document.AuthorityLevel);
        Assert.Equal(updatedAt, document.UpdatedAt);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
