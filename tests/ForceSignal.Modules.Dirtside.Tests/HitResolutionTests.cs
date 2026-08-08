using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// Stage one asks only whether the shot connected. Every variable - the sight, the range, how quiet
/// the target is, what it is doing about being shot at - is baked into a die type rather than added
/// to a roll, so the whole question comes down to one comparison.
/// </summary>
public sealed class HitResolutionTests
{
    [Theory]
    [InlineData(FireControlLevel.Basic, WeaponRangeBand.Close, QualityDie.D8)]
    [InlineData(FireControlLevel.Basic, WeaponRangeBand.Medium, QualityDie.D6)]
    [InlineData(FireControlLevel.Basic, WeaponRangeBand.Long, QualityDie.D4)]
    [InlineData(FireControlLevel.Enhanced, WeaponRangeBand.Close, QualityDie.D10)]
    [InlineData(FireControlLevel.Enhanced, WeaponRangeBand.Medium, QualityDie.D8)]
    [InlineData(FireControlLevel.Enhanced, WeaponRangeBand.Long, QualityDie.D6)]
    [InlineData(FireControlLevel.Superior, WeaponRangeBand.Close, QualityDie.D12)]
    [InlineData(FireControlLevel.Superior, WeaponRangeBand.Medium, QualityDie.D10)]
    [InlineData(FireControlLevel.Superior, WeaponRangeBand.Long, QualityDie.D8)]
    public void TheSightSetsTheDieAndTheBandMovesIt(
        FireControlLevel fireControl,
        WeaponRangeBand band,
        QualityDie expected)
    {
        var solution = HitResolution.Solve(fireControl, band, targetSignature: 3);

        Assert.True(solution.CanFire);
        Assert.Equal(expected, solution.FirerDie);
    }

    [Fact]
    public void ShootingOnTheMoveCostsOneMoreStep()
    {
        var stationary = HitResolution.Solve(FireControlLevel.Enhanced, WeaponRangeBand.Medium, 3);
        var moving = HitResolution.Solve(FireControlLevel.Enhanced, WeaponRangeBand.Medium, 3, firerMovedOverHalf: true);

        Assert.Equal(QualityDie.D8, stationary.FirerDie);
        Assert.Equal(QualityDie.D6, moving.FirerDie);
    }

    [Fact]
    public void ABasicSightCannotTakeALongShotOnTheMoveAtAll()
    {
        // Running off the bottom of the ladder is not capped the way an ordinary shift is: there is
        // simply no die left to roll, so the action is refused rather than resolved as a miss.
        var solution = HitResolution.Solve(
            FireControlLevel.Basic,
            WeaponRangeBand.Long,
            targetSignature: 3,
            firerMovedOverHalf: true);

        Assert.False(solution.CanFire);
        Assert.Contains("no die left", solution.Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => HitResolution.Roll(solution, new QualityDiceRoller()));
    }

    [Theory]
    // A big vehicle is easy to see, so a low signature number means a large defensive die.
    [InlineData(1, QualityDie.D12)]
    [InlineData(2, QualityDie.D10)]
    [InlineData(3, QualityDie.D8)]
    [InlineData(4, QualityDie.D6)]
    [InlineData(5, QualityDie.D4)]
    public void HowLoudTheTargetIsSetsItsPrimaryDie(int signature, QualityDie expected) =>
        Assert.Equal(expected, HitResolution.SignatureDie(signature));

    [Theory]
    [InlineData(DefensivePosture.None, null)]
    [InlineData(DefensivePosture.SoftCover, QualityDie.D6)]
    [InlineData(DefensivePosture.Evading, QualityDie.D8)]
    [InlineData(DefensivePosture.HullDown, QualityDie.D10)]
    [InlineData(DefensivePosture.TurretDown, QualityDie.D12)]
    public void WhatTheTargetIsDoingSetsItsSecondaryDie(DefensivePosture posture, QualityDie? expected) =>
        Assert.Equal(expected, HitResolution.PostureDie(posture));

    [Fact]
    public void TheTargetKeepsItsBetterDieRatherThanAddingTheTwo()
    {
        // The whole point of the second die: a good posture sets a floor on how hard the target is
        // to hit, rather than stacking with how quiet it is. Adding them would make a covered
        // vehicle nearly unhittable.
        var solution = HitResolution.Solve(
            FireControlLevel.Enhanced,
            WeaponRangeBand.Medium,
            targetSignature: 4,
            posture: DefensivePosture.SoftCover);

        // Firer 7, signature die 2, posture die 6. The target keeps the 6, not 8.
        var attempt = HitResolution.Roll(solution, new ScriptedDice(7, 2, 6));

        Assert.Equal(6, attempt.TargetScore);
        Assert.True(attempt.IsHit);
    }

    [Fact]
    public void TheWorkedShotFromTheRulesNotesComesOutAsWritten()
    {
        // An enhanced sight at medium range rolls a D8. The target is a large vehicle, signature 4,
        // so a D6 primary, sitting in soft cover for a D6 secondary. Firer 7 against 2 and 6: the
        // target keeps the 6, and 7 beats it.
        var solution = HitResolution.Solve(
            FireControlLevel.Enhanced,
            WeaponRangeBand.Medium,
            targetSignature: 4,
            posture: DefensivePosture.SoftCover);

        Assert.Equal(QualityDie.D8, solution.FirerDie);
        Assert.Equal(QualityDie.D6, solution.TargetPrimaryDie);
        Assert.Equal(QualityDie.D6, solution.TargetSecondaryDie);

        var attempt = HitResolution.Roll(solution, new ScriptedDice(7, 2, 6));
        Assert.True(attempt.IsHit);
    }

    [Fact]
    public void MatchingTheTargetIsAMiss()
    {
        // Ties go to the defender, as everywhere else in the family. This has to stay a strict
        // comparison: the air-defence rules have a separate exact-tie branch that depends on it.
        var solution = HitResolution.Solve(FireControlLevel.Enhanced, WeaponRangeBand.Medium, 3);

        Assert.False(HitResolution.Roll(solution, new ScriptedDice(5, 5)).IsHit);
        Assert.True(HitResolution.Roll(solution, new ScriptedDice(6, 5)).IsHit);
    }

    [Fact]
    public void ATargetDoingNothingRollsOnlyItsSignatureDie()
    {
        var solution = HitResolution.Solve(FireControlLevel.Enhanced, WeaponRangeBand.Medium, 3);
        Assert.Null(solution.TargetSecondaryDie);

        var attempt = HitResolution.Roll(solution, new ScriptedDice(4, 3));
        Assert.Null(attempt.TargetSecondaryRoll);
        Assert.Equal(3, attempt.TargetScore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void ASignatureOffTheChartIsRefusedRatherThanGuessedAt(int signature) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HitResolution.Solve(FireControlLevel.Enhanced, WeaponRangeBand.Medium, signature));

    private sealed class ScriptedDice(params int[] rolls) : IQualityDiceRoller
    {
        private readonly Queue<int> _rolls = new(rolls);

        public int Roll(QualityDie die) => _rolls.Count > 0
            ? _rolls.Dequeue()
            : throw new InvalidOperationException($"The script ran out while rolling a {die}.");
    }
}
