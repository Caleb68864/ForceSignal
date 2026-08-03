using ForceSignal.Domain.Rules;

namespace ForceSignal.Domain.Tests;

public sealed class RulesProfileTests
{
    [Fact]
    public void LightCinematic_IsTheDefaultAndCarriesTheSecondEditionNumbers()
    {
        var profile = RulesProfile.LightCinematic;

        Assert.Equal(RulesLayer.LightCinematic, profile.Layer);
        Assert.Equal(3, profile.MaxScreenLevel);
        Assert.Equal(12, profile.FighterMoveAllowance);
        Assert.Equal(9, profile.NeedleBeamRange);
    }

    [Fact]
    public void FleetBook_DropsLevelThreeScreensAndStretchesFightersAndNeedles()
    {
        var profile = RulesProfile.FleetBook;

        Assert.Equal(2, profile.MaxScreenLevel);
        Assert.Equal(24, profile.FighterMoveAllowance);
        Assert.Equal(12, profile.NeedleBeamRange);
    }

    [Theory]
    [InlineData("FleetBook", RulesLayer.FleetBook)]
    [InlineData("fleet book", RulesLayer.FleetBook)]
    [InlineData("fb1", RulesLayer.FleetBook)]
    [InlineData("LightCinematic", RulesLayer.LightCinematic)]
    // An unknown or missing name falls back rather than failing, so an old snapshot still restores.
    [InlineData("something else", RulesLayer.LightCinematic)]
    [InlineData(null, RulesLayer.LightCinematic)]
    public void Parse_ReadsALayerNameAndFallsBackToTheDefault(string? name, RulesLayer expected)
    {
        Assert.Equal(expected, RulesProfile.Parse(name).Layer);
    }
}
