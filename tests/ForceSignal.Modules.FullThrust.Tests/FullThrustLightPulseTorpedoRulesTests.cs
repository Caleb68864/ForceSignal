using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;

namespace ForceSignal.Modules.FullThrust.Tests;

public sealed class FullThrustLightPulseTorpedoRulesTests
{
    [Theory]
    // 2+ inside 6mu, then a point worse for every further 6mu, out to a 6 at 30mu.
    [InlineData(1, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(12, 3)]
    [InlineData(13, 4)]
    [InlineData(18, 4)]
    [InlineData(19, 5)]
    [InlineData(24, 5)]
    [InlineData(25, 6)]
    [InlineData(30, 6)]
    public void ToHitNumber_WorsensEverySixUnitsOfRange(int range, int expected)
    {
        Assert.Equal(expected, FullThrustLightPulseTorpedoRules.ToHitNumber(range));
    }

    [Fact]
    public void Resolve_OnAHitTakesTheDamageDieFaceAsDamage()
    {
        // Needs 4+ at 15mu: rolls a 4 to hit, then a 5 for damage.
        var result = Dice(4, 5).Resolve(Solution(range: 15));

        Assert.True(result.IsHit);
        Assert.Equal(4, result.ToHitNumber);
        Assert.Equal([4, 5], result.DiceRolls);
        Assert.Equal(5, result.Damage);
    }

    [Fact]
    public void Resolve_OnAMissRollsNoDamageDie()
    {
        // Needs 5+ at 20mu and rolls a 4.
        var result = Dice(4, 6).Resolve(Solution(range: 20));

        Assert.False(result.IsHit);
        Assert.Equal(5, result.ToHitNumber);
        Assert.Equal([4], result.DiceRolls);
        Assert.Equal(0, result.Damage);
    }

    [Fact]
    public void Resolve_IgnoresScreensEntirely()
    {
        // The same roll against every screen level does the same damage: a torpedo punches through.
        foreach (var screens in new[] { 0, 1, 2, 3 })
        {
            var result = Dice(6, 6).Resolve(Solution(range: 10, screens));

            Assert.Equal(6, result.Damage);
            Assert.Equal(0, result.ScreenReduction);
        }
    }

    [Fact]
    public void Resolve_DoesNotLoseDiceToRangeOrWeaponDamage()
    {
        // A launcher fires one shot: range worsens the number needed rather than removing dice, and
        // the attacker's weapon damage does not thin a torpedo salvo either.
        var result = Dice(6, 3).Resolve(new FiringSolution(
            new WeaponAttackProfile("Torpedo Tube", 1, 30, [FiringArc.Fore], WeaponKind.PulseTorpedo),
            Range: 28,
            TargetScreenRating: 0,
            AttackerWeaponDamage: 3));

        Assert.Equal(1, result.RawDice);
        Assert.Equal(0, result.RangePenalty);
        Assert.Equal(0, result.SystemPenalty);
        Assert.Equal(3, result.Damage);
    }

    [Fact]
    public void Validate_RejectsFireBeyondThirtyUnits()
    {
        var result = new FullThrustLightPulseTorpedoRules().Validate(new FiringSolution(
            new WeaponAttackProfile("Torpedo Tube", 1, 36, [FiringArc.Fore], WeaponKind.PulseTorpedo),
            Range: 31,
            TargetScreenRating: 0,
            AttackerWeaponDamage: 0));

        Assert.False(result.IsValid);
        Assert.Contains("out of range", result.Errors[0]);
    }

    [Fact]
    public void Validate_HoldsATorpedoToItsArcsAndTheAftBlindSpot()
    {
        var rules = new FullThrustLightPulseTorpedoRules();
        var profile = new WeaponAttackProfile("Torpedo Tube", 1, 30, [FiringArc.Fore], WeaponKind.PulseTorpedo);

        Assert.False(rules.Validate(new FiringSolution(profile, 10, 0, 0, FiringArc.AftPort)).IsValid);
        Assert.False(rules.Validate(new FiringSolution(profile, 10, 0, 0, FiringArc.Aft)).IsValid);
        Assert.True(rules.Validate(new FiringSolution(profile, 10, 0, 0, FiringArc.Fore)).IsValid);
    }

    private static FiringSolution Solution(int range, int screens = 0) => new(
        new WeaponAttackProfile("Torpedo Tube", 1, 30, [FiringArc.Fore], WeaponKind.PulseTorpedo),
        range,
        screens,
        AttackerWeaponDamage: 0);

    /// <summary>Torpedo rules fed a fixed sequence of die faces, cycling if more are needed.</summary>
    private static FullThrustLightPulseTorpedoRules Dice(params int[] faces)
    {
        var index = 0;
        return new FullThrustLightPulseTorpedoRules(() => faces[index++ % faces.Length]);
    }
}
