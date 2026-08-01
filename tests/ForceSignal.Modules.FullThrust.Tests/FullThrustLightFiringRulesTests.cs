using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;

namespace ForceSignal.Modules.FullThrust.Tests;

public sealed class FullThrustLightFiringRulesTests
{
    private readonly FullThrustLightFiringRules _rules = new();

    [Theory]
    // Unscreened: 4 and 5 score one, 6 scores two, 1-3 miss.
    [InlineData(1, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(4, 0, 1)]
    [InlineData(5, 0, 1)]
    [InlineData(6, 0, 2)]
    // Level 1 ignores 4s.
    [InlineData(4, 1, 0)]
    [InlineData(5, 1, 1)]
    [InlineData(6, 1, 2)]
    // Level 2 caps every hit at one point.
    [InlineData(4, 2, 0)]
    [InlineData(5, 2, 1)]
    [InlineData(6, 2, 1)]
    // Level 3 ignores everything but a 6.
    [InlineData(4, 3, 0)]
    [InlineData(5, 3, 0)]
    [InlineData(6, 3, 1)]
    public void DieDamage_FollowsTheScreenDowngradeTable(int die, int screenLevel, int expected)
    {
        Assert.Equal(expected, FullThrustLightFiringRules.DieDamage(die, screenLevel));
    }

    [Fact]
    public void Resolve_RollsOneDiePerClassAndScoresTheRolledFaces()
    {
        var result = Dice(6, 4, 5).Resolve(new FiringSolution(
            new WeaponAttackProfile("Class-3 Beam", 3, 36, [FiringArc.Fore]),
            Range: 11,
            TargetScreenRating: 0,
            AttackerWeaponDamage: 0));

        Assert.Equal([6, 4, 5], result.DiceRolls);
        Assert.Equal(3, result.RawDice);
        Assert.Equal(0, result.RangePenalty);
        Assert.Equal(0, result.ScreenReduction);
        Assert.Equal(4, result.Damage); // 2 + 1 + 1
    }

    [Fact]
    public void Resolve_ScreensDowngradeDiceInsteadOfRemovingThem()
    {
        // The same roll against each screen level: dice are still rolled, only their effect drops.
        var unscreened = Dice(6, 4, 5).Resolve(Solution(0));
        var level1 = Dice(6, 4, 5).Resolve(Solution(1));
        var level2 = Dice(6, 4, 5).Resolve(Solution(2));
        var level3 = Dice(6, 4, 5).Resolve(Solution(3));

        Assert.Equal(4, unscreened.Damage);
        Assert.Equal(3, level1.Damage); // the 4 is ignored
        Assert.Equal(2, level2.Damage); // the 6 is capped at one
        Assert.Equal(1, level3.Damage); // only the 6 counts, for one

        // Every level rolled the full three dice: screens reduce damage, not dice.
        Assert.All(new[] { unscreened, level1, level2, level3 }, result => Assert.Equal(3, result.DiceRolls.Count));
        Assert.Equal(0, unscreened.ScreenReduction);
        Assert.Equal(1, level1.ScreenReduction);
        Assert.Equal(2, level2.ScreenReduction);
        Assert.Equal(3, level3.ScreenReduction);

        static FiringSolution Solution(int screens) => new(
            new WeaponAttackProfile("Class-3 Beam", 3, 36, [FiringArc.Fore]), 11, screens, 0);
    }

    [Fact]
    public void Resolve_ASingleDieMountCanStillHurtAScreenedTarget()
    {
        // The old subtraction model made this impossible: one die minus one screen was always zero.
        var result = Dice(6).Resolve(new FiringSolution(
            new WeaponAttackProfile("Class-1 Beam", 1, 12, [FiringArc.Fore]),
            Range: 9,
            TargetScreenRating: 1,
            AttackerWeaponDamage: 0));

        Assert.Equal(2, result.Damage);
    }

    [Theory]
    // A beam loses one die per full 12mu band, so band boundaries are inclusive.
    [InlineData(1, 0)]
    [InlineData(12, 0)]
    [InlineData(13, 1)]
    [InlineData(24, 1)]
    [InlineData(25, 2)]
    [InlineData(36, 2)]
    public void Resolve_LosesOneDiePerTwelveUnitBand(int range, int expectedPenalty)
    {
        var result = Dice(6, 6, 6).Resolve(new FiringSolution(
            new WeaponAttackProfile("Class-3 Beam", 3, 36, [FiringArc.Fore]),
            range,
            TargetScreenRating: 0,
            AttackerWeaponDamage: 0));

        Assert.Equal(expectedPenalty, result.RangePenalty);
        Assert.Equal(3 - expectedPenalty, result.DiceRolls.Count);
        Assert.Equal((3 - expectedPenalty) * 2, result.Damage);
    }

    [Fact]
    public void Resolve_WeaponDamageRemovesDiceBeforeTheyAreRolled()
    {
        var result = Dice(6, 6, 6).Resolve(new FiringSolution(
            new WeaponAttackProfile("Class-3 Beam", 3, 36, [FiringArc.Fore]),
            Range: 6,
            TargetScreenRating: 0,
            AttackerWeaponDamage: 2));

        Assert.Equal(2, result.SystemPenalty);
        Assert.Single(result.DiceRolls);
        Assert.Equal(2, result.Damage);
    }

    [Fact]
    public void Validate_RejectsOutOfRangeFire()
    {
        var result = _rules.Validate(new FiringSolution(
            new WeaponAttackProfile("Needle Beam", 1, 12, [FiringArc.Fore]),
            Range: 13,
            TargetScreenRating: 0,
            AttackerWeaponDamage: 0));

        Assert.False(result.IsValid);
        Assert.Contains("out of range", result.Errors[0]);
    }

    [Fact]
    public void Validate_RefusesToFireThroughTheAftBlindSpot()
    {
        // An all-round mount still cannot shoot dead astern: every weapon has that arc blacked out.
        var result = _rules.Validate(new FiringSolution(
            new WeaponAttackProfile("Class-2 Beam", 2, 24, [.. FiringArcs.Firable]),
            Range: 6,
            TargetScreenRating: 0,
            AttackerWeaponDamage: 0,
            TargetArc: FiringArc.Aft));

        Assert.False(result.IsValid);
        Assert.Contains("aft arc", result.Errors[0]);
    }

    [Fact]
    public void Validate_RefusesAnArcTheMountDoesNotBearThrough()
    {
        var result = _rules.Validate(new FiringSolution(
            new WeaponAttackProfile("Class-3 Beam", 3, 36, [FiringArc.Fore]),
            Range: 6,
            TargetScreenRating: 0,
            AttackerWeaponDamage: 0,
            TargetArc: FiringArc.AftPort));

        Assert.False(result.IsValid);
        Assert.Contains("does not bear", result.Errors[0]);
        Assert.Contains("aft port", result.Errors[0]);
    }

    [Fact]
    public void Validate_AcceptsAnyArcAMultiArcBatteryBearsThrough()
    {
        // A battery may bear through several adjacent arcs, which is how published ships are drawn.
        var battery = new WeaponAttackProfile(
            "Class-2 Beam", 2, 24, [FiringArc.ForePort, FiringArc.Fore, FiringArc.ForeStarboard]);

        Assert.All(
            new[] { FiringArc.ForePort, FiringArc.Fore, FiringArc.ForeStarboard },
            arc => Assert.True(_rules.Validate(new FiringSolution(battery, 6, 0, 0, arc)).IsValid));
        Assert.False(_rules.Validate(new FiringSolution(battery, 6, 0, 0, FiringArc.AftStarboard)).IsValid);
    }

    /// <summary>Firing rules fed a fixed sequence of die faces, cycling if more are needed.</summary>
    private static FullThrustLightFiringRules Dice(params int[] faces)
    {
        var index = 0;
        return new FullThrustLightFiringRules(() => faces[index++ % faces.Length]);
    }
}
