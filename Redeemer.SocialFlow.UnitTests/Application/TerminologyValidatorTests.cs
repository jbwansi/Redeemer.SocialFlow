using Redeemer.SocialFlow.Application.Terminology;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Exceptions;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Application;

public sealed class TerminologyValidatorTests
{
    private static TermRule Rule(string canonical, params string[] aliases) => new(Guid.NewGuid(), 2, canonical, aliases, "", "", null, "https://reference.test");
    [Theory]
    [InlineData("lego serious play", "LEGO® SERIOUS PLAY®")]
    [InlineData("Lego Serious Play", "LEGO® SERIOUS PLAY®")]
    [InlineData("LEGO® SERIOUS PLAY®", "LEGO® SERIOUS PLAY®")]
    public void CanonicalSpellingIsDataNotAHardCodedBrand(string input, string expected)
    {
        var result = TerminologyValidator.Check(input, [Rule("LEGO® SERIOUS PLAY®", "lego serious play")], true);
        Assert.Equal(expected, result.Text);
        Assert.Single(result.Terms);
    }
    [Theory]
    [InlineData("disc scrum okr", "DISC Scrum OKR")]
    [InlineData("discussion scrumptious okra", "discussion scrumptious okra")]
    [InlineData("(disc), scrum/okr", "(DISC), Scrum/OKR")]
    public void WordBoundariesAvoidPartialProfessionalTerms(string input, string expected) =>
        Assert.Equal(expected, TerminologyValidator.Check(input, [Rule("DISC"), Rule("Scrum"), Rule("OKR")], true).Text);

    [Fact]
    public void UnicodeCanonicalEquivalencePreservesOtherTextAndSymbols()
    {
        var input = "🙂 e\u0301cole et ÉCOLE™ ; inconnu®";
        var result = TerminologyValidator.Check(input, [Rule("École™", "école")], true);
        Assert.Equal("🙂 École™ et École™ ; inconnu®", result.Text);
        Assert.Equal("école", TerminologyValidator.Check("école", [Rule("Ecole")], true).Text);
    }
    [Theory]
    [InlineData("https://example.test/scrum?q=okr")]
    [InlineData("www.scrum.test/okr")]
    [InlineData("[lien](https://test.example/scrum)")]
    [InlineData("« scrum et okr »")]
    [InlineData("\"scrum\"")]
    [InlineData("\"source avec \\\"scrum\\\" cité\"")]
    [InlineData("<code>scrum</code>")]
    [InlineData("“scrum”")]
    [InlineData("'scrum'")]
    [InlineData("`scrum`")]
    [InlineData("```text\nscrum\n```")]
    [InlineData("~~~\nokr\n~~~")]
    [InlineData("> scrum\n> okr")]
    [InlineData("    scrum")]
    public void LiteralRegionsRemainByteForByteUnchanged(string input)
    {
        var result = TerminologyValidator.Check(input, [Rule("Scrum"), Rule("OKR")], true);
        Assert.Equal(input, result.Text);
        Assert.NotEmpty(result.Warnings);
    }
    [Fact]
    public void ApostrophesInProseAreNotMistakenForQuotations() =>
        Assert.Equal("L’approche Scrum et l'outil OKR", TerminologyValidator.Check("L’approche scrum et l'outil okr", [Rule("Scrum"), Rule("OKR")], true).Text);
    [Fact]
    public void AmbiguousAliasesRequireHumanReview()
    {
        var result = TerminologyValidator.Check("AC", [Rule("Alpha Concept", "AC"), Rule("Another Concept", "AC")], true);
        Assert.Equal("AC", result.Text); Assert.Contains(result.Warnings, x => x.Contains("ambiguë"));
    }
    [Fact]
    public void LongestKnownExpressionWinsWithoutDoubleReplacement()
    {
        var result = TerminologyValidator.Check("scrum master et scrum", [Rule("Scrum"), Rule("Scrum Master™", "scrum master")], true);
        Assert.Equal("Scrum Master™ et Scrum", result.Text);
    }
    [Fact]
    public void CrossingExpressionsAreAmbiguousRatherThanArbitrarilySelected()
    {
        var result = TerminologyValidator.Check("alpha beta gamma", [Rule("Alpha Beta™", "alpha beta"), Rule("Beta Gamma™", "beta gamma")], true);
        Assert.Equal("alpha beta gamma", result.Text);
        Assert.Contains(result.Warnings, x => x.Contains("chevauchantes"));
    }
    [Fact]
    public void ContextDependentAliasesAreNeverAutomaticallyRewritten()
    {
        var result = TerminologyValidator.Check("AC", [Rule("Alpha Concept", "AC") with { UsageContext = "Only in training" }], true);
        Assert.Equal("AC", result.Text); Assert.Contains(result.Warnings, x => x.Contains("Contexte"));
    }
    [Fact]
    public void NoKnownTermMeansNoInsertionOrCertification()
    {
        var result = TerminologyValidator.Check("A different subject", [Rule("Scrum")], true);
        Assert.Equal("A different subject", result.Text); Assert.Empty(result.Terms); Assert.Empty(result.Warnings);
    }
    [Fact]
    public void ValidationRequiresSourceAndEditsRemoveValidation()
    {
        var term = WorkspaceTerm.Create("owner");
        term.Update("École™", ["école"], TermCategory.Method, "fr", null, null, null, "owner");
        Assert.Throws<DomainException>(() => term.Validate("owner"));
        term.Update("École™", [], TermCategory.Method, "fr", null, null, "Reference", "owner");
        term.Validate("owner"); Assert.NotNull(term.VerifiedAt); Assert.Equal(TermStatus.Validated, term.Status);
        term.Update("École™", ["école"], TermCategory.Method, "fr", null, null, "Reference", "owner");
        Assert.Equal(TermStatus.Proposed, term.Status); Assert.Null(term.VerifiedAt);
        Assert.Equal("École™", term.Canonical); Assert.Equal("ÉCOLE™", term.SearchKey);
    }
}
