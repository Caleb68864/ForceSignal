using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;

namespace ForceSignal.Modules.FullThrust.Tests;

/// <summary>
/// Beam fire, played against <see cref="TestRules.Invented"/> - an eight-sided die, ten-mu bands,
/// and a screen table that is nobody's published one. Every expected number below is read off that
/// profile, so these tests fail if the engine ever starts remembering a table of its own.
/// </summary>
public sealed class FullThrustLightFiringRulesTests
{
    private readonly FullThrustLightFiringRules _rules = new();
    private static readonly RulesProfile Rules = TestRules.Invented;

    [Theory]
    // Unscreened: the top two faces score two, the two below them one, the rest nothing.
    [InlineData(1, 0, 0)]
    [InlineData(4, 0, 0)]
    [InlineData(5, 0, 1)]
    [InlineData(6, 0, 1)]
    [InlineData(7, 0, 2)]
    [InlineData(8, 0, 2)]
    // One level of screening flattens every hit to a single point and stops the 5 entirely.
    [InlineData(5, 1, 0)]
    [InlineData(6, 1, 1)]
    [InlineData(8, 1, 1)]
    // Two levels stop everything but the top face.
    [InlineData(6, 2, 0)]
    [InlineData(7, 2, 0)]
    [InlineData(8, 2, 1)]
    public void ADieScoresWhatTheProfileSaysItScores(int die, int screenLevel, int expected) =>
        Assert.Equal(expected, Rules.BeamDamageFor(die, screenLevel));

    [Fact]
    public void AScreenLevelAboveTheProfilesCeilingIsHeldToIt()
    {
        // The profile stops at two, so a ship somehow carrying three is read as carrying two rather
        // than falling off the end of the table into scoring nothing.
        Assert.Equal(Rules.BeamDamageFor(8, 2), Rules.BeamDamageFor(8, 3));
    }

    [Fact]
    public void OneDieIsRolledPerClassAndTheFacesAreScored()
    {
        var result = Dice(8, 6, 5).Resolve(
            new FiringSolution(
                new WeaponAttackProfile("Class-3 Beam", 3, 36, [FiringArc.Fore]),
                Range: 9,
                TargetScreenRating: 0,
                AttackerWeaponDamage: 0),
            Rules);

        Assert.Equal([8, 6, 5], result.DiceRolls);
        Assert.Equal(3, result.RawDice);
        Assert.Equal(0, result.RangePenalty);
        Assert.Equal(0, result.ScreenReduction);
        Assert.Equal(4, result.Damage); // 2 + 1 + 1
    }

    [Fact]
    public void ScreensDowngradeDiceInsteadOfRemovingThem()
    {
        // The same roll against each screen level: the dice are still rolled, only their effect drops.
        var unscreened = Dice(8, 6, 5).Resolve(Solution(0), Rules);
        var level1 = Dice(8, 6, 5).Resolve(Solution(1), Rules);
        var level2 = Dice(8, 6, 5).Resolve(Solution(2), Rules);

        Assert.Equal(4, unscreened.Damage);
        Assert.Equal(2, level1.Damage); // the 8 is flattened to one and the 5 stopped
        Assert.Equal(1, level2.Damage); // only the 8 gets through, for one

        // Every level rolled the full three dice: screens reduce damage, not dice.
        Assert.All(new[] { unscreened, level1, level2 }, result => Assert.Equal(3, result.DiceRolls.Count));
        Assert.Equal(0, unscreened.ScreenReduction);
        Assert.Equal(2, level1.ScreenReduction);
        Assert.Equal(3, level2.ScreenReduction);

        static FiringSolution Solution(int screens) => new(
            new WeaponAttackProfile("Class-3 Beam", 3, 36, [FiringArc.Fore]), 9, screens, 0);
    }

    [Fact]
    public void ASingleDieMountCanStillHurtAScreenedTarget()
    {
        // The old subtraction model made this impossible: one die minus one screen was always zero.
        var result = Dice(8).Resolve(
            new FiringSolution(
                new WeaponAttackProfile("Class-1 Beam", 1, 12, [FiringArc.Fore]),
                Range: 9,
                TargetScreenRating: 1,
                AttackerWeaponDamage: 0),
            Rules);

        Assert.Equal(1, result.Damage);
    }

    [Theory]
    // The profile bands every ten mu, and a range on a band edge belongs to the nearer band.
    [InlineData(1, 0)]
    [InlineData(10, 0)]
    [InlineData(11, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    [InlineData(30, 2)]
    public void ADieIsLostPerFullBandTheProfileSets(int range, int expectedPenalty)
    {
        var result = Dice(8, 8, 8).Resolve(
            new FiringSolution(
                new WeaponAttackProfile("Class-3 Beam", 3, 36, [FiringArc.Fore]),
                range,
                TargetScreenRating: 0,
                AttackerWeaponDamage: 0),
            Rules);

        Assert.Equal(expectedPenalty, result.RangePenalty);
        Assert.Equal(3 - expectedPenalty, result.DiceRolls.Count);
        Assert.Equal((3 - expectedPenalty) * 2, result.Damage);
    }

    [Fact]
    public void ABandWidthFromADifferentProfileMovesTheBoundary()
    {
        // Nothing about twelve, or ten, is written into the engine: hand it a different width and
        // the same range falls in a different band.
        var narrow = Rules with { BeamRangeBandWidth = 5 };

        var result = Dice(8, 8, 8).Resolve(
            new FiringSolution(
                new WeaponAttackProfile("Class-3 Beam", 3, 36, [FiringArc.Fore]),
                Range: 9,
                TargetScreenRating: 0,
                AttackerWeaponDamage: 0),
            narrow);

        Assert.Equal(1, result.RangePenalty);
    }

    [Fact]
    public void WeaponDamageRemovesDiceBeforeTheyAreRolled()
    {
        var result = Dice(8, 8, 8).Resolve(
            new FiringSolution(
                new WeaponAttackProfile("Class-3 Beam", 3, 36, [FiringArc.Fore]),
                Range: 6,
                TargetScreenRating: 0,
                AttackerWeaponDamage: 2),
            Rules);

        Assert.Equal(2, result.SystemPenalty);
        Assert.Single(result.DiceRolls);
        Assert.Equal(2, result.Damage);
    }

    [Fact]
    public void OutOfRangeFireIsRejected()
    {
        var result = _rules.Validate(
            new FiringSolution(
                new WeaponAttackProfile("Needle Beam", 1, 12, [FiringArc.Fore]),
                Range: 13,
                TargetScreenRating: 0,
                AttackerWeaponDamage: 0),
            Rules);

        Assert.False(result.IsValid);
        Assert.Contains("out of range", result.Errors[0]);
    }

    [Fact]
    public void NothingFiresThroughTheAftBlindSpot()
    {
        // An all-round mount still cannot shoot dead astern: every weapon has that arc blacked out.
        var result = _rules.Validate(
            new FiringSolution(
                new WeaponAttackProfile("Class-2 Beam", 2, 24, [.. FiringArcs.Firable]),
                Range: 6,
                TargetScreenRating: 0,
                AttackerWeaponDamage: 0,
                TargetArc: FiringArc.Aft),
            Rules);

        Assert.False(result.IsValid);
        Assert.Contains("aft arc", result.Errors[0]);
    }

    [Fact]
    public void AnArcTheMountDoesNotBearThroughIsRefused()
    {
        var result = _rules.Validate(
            new FiringSolution(
                new WeaponAttackProfile("Class-3 Beam", 3, 36, [FiringArc.Fore]),
                Range: 6,
                TargetScreenRating: 0,
                AttackerWeaponDamage: 0,
                TargetArc: FiringArc.AftPort),
            Rules);

        Assert.False(result.IsValid);
        Assert.Contains("does not bear", result.Errors[0]);
        Assert.Contains("aft port", result.Errors[0]);
    }

    [Fact]
    public void AnyArcAMultiArcBatteryBearsThroughIsAccepted()
    {
        // A battery may bear through several adjacent arcs, which is how ships are usually drawn.
        var battery = new WeaponAttackProfile(
            "Class-2 Beam", 2, 24, [FiringArc.ForePort, FiringArc.Fore, FiringArc.ForeStarboard]);

        Assert.All(
            new[] { FiringArc.ForePort, FiringArc.Fore, FiringArc.ForeStarboard },
            arc => Assert.True(_rules.Validate(new FiringSolution(battery, 6, 0, 0, arc), Rules).IsValid));
        Assert.False(_rules.Validate(new FiringSolution(battery, 6, 0, 0, FiringArc.AftStarboard), Rules).IsValid);
    }

    /// <summary>Firing rules fed a fixed sequence of die faces, cycling if more are needed.</summary>
    private static FullThrustLightFiringRules Dice(params int[] faces)
    {
        var index = 0;
        return new FullThrustLightFiringRules(() => faces[index++ % faces.Length]);
    }
}
