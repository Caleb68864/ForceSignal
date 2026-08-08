using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// The band both stages read. It exists so the degradation is worked out once rather than twice, and
/// it keeps the measured band alongside the resolved one so a log can explain the difference.
/// </summary>
public sealed class EffectiveRangeBandTests
{
    [Theory]
    [InlineData(WeaponRangeBand.Close)]
    [InlineData(WeaponRangeBand.Medium)]
    [InlineData(WeaponRangeBand.Long)]
    public void AnUndamagedFirerResolvesAtTheBandItMeasured(WeaponRangeBand band)
    {
        var effective = EffectiveRangeBand.For(band, firerIsDamaged: false);

        Assert.True(effective.CanFire);
        Assert.Equal(band, effective.Band);
        Assert.False(effective.WasDegraded);
    }

    [Theory]
    [InlineData(WeaponRangeBand.Close, WeaponRangeBand.Medium)]
    [InlineData(WeaponRangeBand.Medium, WeaponRangeBand.Long)]
    public void ADamagedFirerResolvesOneBandWorse(WeaponRangeBand measured, WeaponRangeBand expected)
    {
        var effective = EffectiveRangeBand.For(measured, firerIsDamaged: true);

        Assert.Equal(expected, effective.Band);
        Assert.True(effective.WasDegraded);

        // The measured band survives, because "he shot at close range and it resolved as medium" is
        // what the log has to be able to say.
        Assert.Equal(measured, effective.Measured);
        Assert.True(effective.FirerIsDamaged);
    }

    [Fact]
    public void ADamagedFirerHasNoBandLeftAtLongRange()
    {
        var effective = EffectiveRangeBand.For(WeaponRangeBand.Long, firerIsDamaged: true);

        Assert.False(effective.CanFire);
        Assert.Null(effective.Resolved);
        Assert.Throws<InvalidOperationException>(() => effective.Band);
    }

    [Fact]
    public void DegradationIsNotCumulativeBecauseDamageIsAFlag()
    {
        // Feeding the resolved band back in would shift it twice. Being damaged twice is still just
        // being damaged, which is why the shift is a function of the measured band rather than
        // something applied to a running value.
        var once = EffectiveRangeBand.For(WeaponRangeBand.Close, firerIsDamaged: true);
        var again = EffectiveRangeBand.For(once.Band, firerIsDamaged: true);

        Assert.Equal(WeaponRangeBand.Medium, once.Band);
        Assert.NotEqual(again.Band, once.Band);
    }

    [Fact]
    public void ABandOffTheChartIsRefusedRatherThanTreatedAsUnreachable() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectiveRangeBand.For((WeaponRangeBand)99, false));

    [Fact]
    public void TheValidityCardIsKeyedByBandSoTheResolvedBandPicksTheRow()
    {
        var card = new WeaponValidityCard(
            new ChitValidity(ChitColours.All),
            new ChitValidity(ChitColours.Red),
            ChitValidity.Ineffective);

        Assert.Equal(ChitColours.All, card.At(WeaponRangeBand.Close).ValidColours);
        Assert.Equal(ChitColours.Red, card.At(WeaponRangeBand.Medium).ValidColours);
        Assert.True(card.At(WeaponRangeBand.Long).IsIneffective);
        Assert.Throws<ArgumentOutOfRangeException>(() => card.At((WeaponRangeBand)99));
    }

    [Fact]
    public void AFlatCardAnswersTheSameAtEveryBand()
    {
        // Some weapons have one effective range rather than three bands, and some gate on the
        // target's armour type instead of on distance. Both want one row written once.
        var card = WeaponValidityCard.Flat(new ChitValidity(ChitColours.Yellow));

        Assert.Equal(card.At(WeaponRangeBand.Close), card.At(WeaponRangeBand.Long));
        Assert.True(WeaponValidityCard.Ineffective.At(WeaponRangeBand.Close).IsIneffective);
    }
}
