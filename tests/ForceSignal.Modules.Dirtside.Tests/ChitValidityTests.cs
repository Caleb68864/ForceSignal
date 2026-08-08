using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// Validity is a row off the player's own record card, not a table shipped with the app. What is
/// tested here is that the four fields stay independent of one another - collapsing any pair of them
/// is where the subtle rules bugs live.
/// </summary>
public sealed class ChitValidityTests
{
    private static DamageChit Red(int value) => DamageChit.Numerical(ChitColour.Red, value);

    [Theory]
    [InlineData(ChitColours.Red, ChitColour.Red, true)]
    [InlineData(ChitColours.Red, ChitColour.Yellow, false)]
    [InlineData(ChitColours.All, ChitColour.Green, true)]
    [InlineData(ChitColours.None, ChitColour.Red, false)]
    public void ColourValidityIsASetRatherThanASingleColour(
        ChitColours valid, ChitColour drawn, bool expected) =>
        Assert.Equal(expected, new ChitValidity(valid).Counts(DamageChit.Numerical(drawn, 2)));

    [Fact]
    public void SpecialsAreNotColourGated()
    {
        // The whole reason "ineffective" cannot be modelled as an empty colour set.
        var noColours = new ChitValidity(ChitColours.None);

        Assert.True(noColours.Counts(DamageChit.Of(ChitSpecial.Boom)));
        Assert.False(ChitValidity.Ineffective.Counts(DamageChit.Of(ChitSpecial.Boom)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 6)]
    public void DoublingIsExactlyWhatItSounds(int value, int expected) =>
        Assert.Equal(expected, new ChitValidity(ChitColours.All, ChitValueScale.Doubled).ValueOf(Red(value)));

    [Theory]
    // Rounding towards zero, which is a choice rather than a rule - see the named constant. A chit
    // worth 1 therefore contributes nothing at all once halved.
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    public void HalvingRoundsTowardsZeroByAStatedChoice(int value, int expected) =>
        Assert.Equal(expected, new ChitValidity(ChitColours.All, ChitValueScale.Halved).ValueOf(Red(value)));

    [Fact]
    public void HalvingHappensPerChitRatherThanOnTheTotal()
    {
        // Two chits of 1 come to nothing halved per chit, and to 1 halved on the total. The rule
        // talks about chits, so it is per chit - but the two really are different answers, which is
        // why it is worth pinning.
        var validity = new ChitValidity(ChitColours.All, ChitValueScale.Halved);

        var outcome = DamageResolution.Resolve(2, validity, armourValue: 1, new FixedPot(Red(1), Red(1)));

        Assert.Equal(0, outcome.ValidTotal);
        Assert.Equal(NumericalDamage.None, outcome.Numerical);
    }

    [Fact]
    public void TheScaleAppliesOnlyToChitsTheWeaponMayCount()
    {
        // Doubling an invalid chit would hand the wasted slot back, which is the one thing the
        // engine must never do.
        var validity = new ChitValidity(ChitColours.Red, ChitValueScale.Doubled);

        Assert.Equal(0, validity.ValueOf(DamageChit.Numerical(ChitColour.Green, 3)));
        Assert.Equal(6, validity.ValueOf(Red(3)));
    }

    [Fact]
    public void ASpecialHasNoNumericalValueWhateverTheScale()
    {
        var validity = new ChitValidity(ChitColours.All, ChitValueScale.Doubled);

        Assert.Equal(0, validity.ValueOf(DamageChit.Of(ChitSpecial.Boom)));
        Assert.True(validity.Counts(DamageChit.Of(ChitSpecial.Boom)));
    }

    [Fact]
    public void CloseRangeDoublingAndLongRangeHalvingTurnTheSameDrawIntoDifferentShots()
    {
        // The one weapon family that scales values instead of gating colours: the same three chits
        // bounce at long range and destroy at close range.
        var draw = new DamageChit[] { Red(1), Red(1), Red(1) };

        var close = DamageResolution.Resolve(
            3, new ChitValidity(ChitColours.All, ChitValueScale.Doubled), 4, new FixedPot(draw));
        var medium = DamageResolution.Resolve(
            3, new ChitValidity(ChitColours.All), 4, new FixedPot(draw));
        var far = DamageResolution.Resolve(
            3, new ChitValidity(ChitColours.All, ChitValueScale.Halved), 4, new FixedPot(draw));

        Assert.Equal(NumericalDamage.KnockedOut, close.Numerical);
        Assert.Equal(NumericalDamage.None, medium.Numerical);
        Assert.Equal(0, far.ValidTotal);
    }

    [Fact]
    public void IneffectiveIsAFieldOfItsOwnRatherThanAnEmptyColourSet()
    {
        Assert.True(ChitValidity.Ineffective.IsIneffective);
        Assert.False(new ChitValidity(ChitColours.None).IsIneffective);
    }

    private sealed class FixedPot(params DamageChit[] chits) : IChitPot
    {
        public IReadOnlyList<DamageChit> Draw(int count) => [.. chits.Take(count)];
    }
}
