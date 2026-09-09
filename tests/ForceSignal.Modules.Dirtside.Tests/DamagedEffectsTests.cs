using ForceSignal.Modules.Dirtside.Combat;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// What a DMG marker costs. Two effects only, and neither of them stacks - a damaged vehicle that is
/// damaged again is still just damaged.
/// </summary>
public sealed class DamagedEffectsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(6, 3)]
    // Rounding towards zero is a stated choice, not a rule: half of seven had to be something.
    [InlineData(7, 3)]
    public void MovementIsHalvedAndRoundsTowardsZeroByAStatedChoice(int baseMovement, int expected) =>
        Assert.Equal(expected, DamagedEffects.Movement(baseMovement));

    [Fact]
    public void HalvingMovementIsNotCumulative()
    {
        // Being damaged is a flag, not a counter, so the effect is a function of the undamaged
        // vehicle. Applying it twice would be the bug.
        Assert.Equal(4, DamagedEffects.Movement(8));
        Assert.NotEqual(DamagedEffects.Movement(DamagedEffects.Movement(8)), DamagedEffects.Movement(8));
    }

    [Theory]
    [InlineData(WeaponRangeBand.Close, WeaponRangeBand.Medium)]
    [InlineData(WeaponRangeBand.Medium, WeaponRangeBand.Long)]
    public void EveryBandCountsOneWorse(WeaponRangeBand band, WeaponRangeBand expected) =>
        Assert.Equal(expected, DamagedEffects.Band(band));

    [Fact]
    public void ADamagedVehicleCannotTakeALongShotAtAll()
    {
        // Not "harder" - gone. The band itself moves, and there is no band past long for it to move
        // into, so the shot disappears rather than becoming unlikely.
        Assert.Null(DamagedEffects.Band(WeaponRangeBand.Long));

        // Asked the way the resolver asks it. DamagedEffects used to carry a CanFireAt predicate
        // saying the same thing a second way; nothing but this test called it, while the rule itself
        // reaches DirectFire through here, so the wrapper went and the coverage stayed.
        Assert.False(EffectiveRangeBand.For(WeaponRangeBand.Long, firerIsDamaged: true).CanFire);
        Assert.True(EffectiveRangeBand.For(WeaponRangeBand.Close, firerIsDamaged: true).CanFire);
        Assert.True(EffectiveRangeBand.For(WeaponRangeBand.Medium, firerIsDamaged: true).CanFire);
    }

    [Fact]
    public void ANegativeMovementIsRefusedRatherThanHalved() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DamagedEffects.Movement(-1));
}
