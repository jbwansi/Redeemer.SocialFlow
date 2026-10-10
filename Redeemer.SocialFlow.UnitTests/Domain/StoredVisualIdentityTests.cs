using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Exceptions;
using Redeemer.SocialFlow.Application.AI;
using Xunit;
namespace Redeemer.SocialFlow.UnitTests.Domain;
public sealed class StoredVisualIdentityTests
{
    private static StoredVisualIdentity Identity() => StoredVisualIdentity.Create("brand", "Brand", "Simple", ["#aaBBcc"], null, "No text", DateTimeOffset.UtcNow);
    [Fact] public void NormalizesPaletteAndCodeAndAdvancesRevision()
    {
        var identity = Identity();
        Assert.Equal("BRAND", identity.IdentityId); Assert.Contains("#AABBCC", identity.ColorsJson);
        Assert.Equal(1, identity.Revision); identity.SetDefault(true);
        identity.Update("BRAND", "New", "Flat", ["#000000"], null, null, false);
        Assert.False(identity.IsDefault); Assert.Equal(3, identity.Revision);
        Assert.Throws<DomainException>(() => identity.SetDefault(true));
    }
    [Theory] [InlineData(0)] [InlineData(9)]
    public void RejectsPaletteCount(int count) => Assert.Throws<DomainException>(() =>
        StoredVisualIdentity.Create("B", "Brand", "Flat", Enumerable.Repeat("#000000", count).ToArray(), null, null, DateTimeOffset.UtcNow));
    [Theory] [InlineData("#123")] [InlineData("#GG0000")] [InlineData("000000")]
    public void RejectsInvalidColor(string color) => Assert.Throws<DomainException>(() =>
        StoredVisualIdentity.Create("B", "Brand", "Flat", [color], null, null, DateTimeOffset.UtcNow));
    [Fact] public void DeletedIdentityCannotBeReactivatedOrEdited()
    {
        var identity = Identity(); identity.Delete();
        Assert.Throws<DomainException>(() => identity.SetLogo(Guid.NewGuid()));
        Assert.Throws<DomainException>(() => identity.Update("B", "Brand", "Flat", ["#000000"], null, null, true));
    }
    [Fact] public void EffectiveSnapshotCopiesColorsAndIncludesInstructionsAndLogo()
    {
        var colors = new[] { "#000000" }; var logo = Guid.NewGuid(); var source = Guid.NewGuid();
        var identity = new VisualIdentity("B", "Brand", "Flat", colors, "Slogan", "No text", source, 2, logo);
        colors[0] = "#FFFFFF";
        var snapshot = identity.Snapshot();
        Assert.Equal("#000000", snapshot.Colors[0]); Assert.Equal("No text", snapshot.Instructions);
        Assert.Equal(source, snapshot.LibraryId); Assert.Equal(logo, snapshot.LogoId); Assert.Equal(2, snapshot.Revision);
    }
}
