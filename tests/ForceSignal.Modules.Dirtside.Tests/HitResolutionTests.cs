using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// Stage one asks only whether the shot connected. Every variable - the sight, the range, how quiet
/// the target is, what it is doing about being shot at - is baked into a die type rather than added
/// to a roll, so the whole question comes down to one comparison.
/// </summary>
/// <remarks>
/// Every die below comes off <see cref="TestDieTables.Invented"/> and none of them is the engine's.
/// The tables these tests used to assert against were written into <c>HitResolution</c> itself, so
/// the suite was checking the engine agreed with itself; now it checks the engine reads what it was
/// handed, which is the only thing about a die this app is entitled to have an opinion on.
/// </remarks>
public sealed class HitResolutionTests
{
    [Theory]
    // The invented profile puts the three sights two rungs apart, starting at the bottom.
    [InlineData(FireControlLevel.Basic, WeaponRangeBand.Close, QualityDie.D6)]
    [InlineData(FireControlLevel.Basic, WeaponRangeBand.Medium, QualityDie.D4)]
    [InlineData(FireControlLevel.Enhanced, WeaponRangeBand.Close, QualityDie.D10)]
    [InlineData(FireControlLevel.Enhanced, WeaponRangeBand.Medium, QualityDie.D8)]
    [InlineData(FireControlLevel.Enhanced, WeaponRangeBand.Long, QualityDie.D6)]
    [InlineData(FireControlLevel.Superior, WeaponRangeBand.Medium, QualityDie.D12)]
    [InlineData(FireControlLevel.Superior, WeaponRangeBand.Long, QualityDie.D10)]
    public void TheSightSetsTheDieAndTheBandMovesIt(
        FireControlLevel fireControl,
        WeaponRangeBand band,
        QualityDie expected)
    {
        var solution = HitResolution.Solve(TestDieTables.Invented, fireControl, band, targetSignature: 3);

        Assert.True(solution.CanFire);
        Assert.Equal(expected, solution.FirerDie);
    }

    [Fact]
    public void ADieAlreadyAtTheTopOfTheLadderStaysThere()
    {
        // A shift that runs off the top is capped, unlike one that runs off the bottom. The invented
        // profile's superior sight is already at the top, so a close shot has nowhere better to go.
        var solution = HitResolution.Solve(
            TestDieTables.Invented, FireControlLevel.Superior, WeaponRangeBand.Close, targetSignature: 3);

        Assert.True(solution.CanFire);
        Assert.Equal(QualityDie.D12, solution.FirerDie);
    }

    [Fact]
    public void ShootingOnTheMoveCostsOneMoreStep()
    {
        var stationary = HitResolution.Solve(
            TestDieTables.Invented, FireControlLevel.Enhanced, WeaponRangeBand.Medium, 3);
        var moving = HitResolution.Solve(
            TestDieTables.Invented, FireControlLevel.Enhanced, WeaponRangeBand.Medium, 3, firerMovedOverHalf: true);

        Assert.Equal(QualityDie.D8, stationary.FirerDie);
        Assert.Equal(QualityDie.D6, moving.FirerDie);
    }

    [Fact]
    public void ASightAtTheBottomOfTheLadderCannotTakeALongShotOnTheMoveAtAll()
    {
        // Running off the bottom of the ladder is not capped the way an ordinary shift is: there is
        // simply no die left to roll, so the action is refused rather than resolved as a miss.
        var solution = HitResolution.Solve(
            TestDieTables.Invented,
            FireControlLevel.Basic,
            WeaponRangeBand.Long,
            targetSignature: 3,
            firerMovedOverHalf: true);

        Assert.False(solution.CanFire);
        Assert.Contains("no die left", solution.Reason!, StringComparison.OrdinalIgnoreCase);

        // A rule about the shot, not a gap in the profile. The two refusals are distinguished
        // because only one of them is fixed by typing.
        Assert.False(solution.IsMissingFromProfile);
        Assert.Throws<InvalidOperationException>(() => HitResolution.Roll(solution, new QualityDiceRoller()));
    }

    [Theory]
    [InlineData(1, QualityDie.D10)]
    [InlineData(2, QualityDie.D8)]
    [InlineData(3, QualityDie.D6)]
    [InlineData(4, QualityDie.D4)]
    [InlineData(5, QualityDie.D4)]
    public void HowLoudTheTargetIsSetsItsPrimaryDie(int signature, QualityDie expected)
    {
        var solution = HitResolution.Solve(
            TestDieTables.Invented, FireControlLevel.Enhanced, WeaponRangeBand.Medium, signature);

        Assert.True(solution.CanFire);
        Assert.Equal(expected, solution.TargetPrimaryDie);
    }

    [Theory]
    [InlineData(DefensivePosture.None, null)]
    [InlineData(DefensivePosture.SoftCover, QualityDie.D4)]
    [InlineData(DefensivePosture.Evading, QualityDie.D6)]
    [InlineData(DefensivePosture.HullDown, QualityDie.D8)]
    [InlineData(DefensivePosture.TurretDown, QualityDie.D10)]
    public void WhatTheTargetIsDoingSetsItsSecondaryDie(DefensivePosture posture, QualityDie? expected)
    {
        var solution = HitResolution.Solve(
            TestDieTables.Invented, FireControlLevel.Enhanced, WeaponRangeBand.Medium, 3, posture);

        Assert.True(solution.CanFire);
        Assert.Equal(expected, solution.TargetSecondaryDie);
    }

    [Fact]
    public void AProfileWithNothingInItRefusesTheShotAndNamesWhatItWants()
    {
        // The heart of the change. A game with no die tables does not fall back to anything; it
        // stops, and it says which row would have let it go on.
        var solution = HitResolution.Solve(
            TestDieTables.Blank, FireControlLevel.Superior, WeaponRangeBand.Medium, targetSignature: 2);

        Assert.False(solution.CanFire);
        Assert.True(solution.IsMissingFromProfile);
        Assert.Contains("Superior", solution.Reason!, StringComparison.Ordinal);
        Assert.Contains("rules profile", solution.Reason!, StringComparison.Ordinal);

        // And no die is quietly handed back in place of the ones it could not find.
        Assert.Equal(default, solution.FirerDie);
        Assert.Equal(default, solution.TargetPrimaryDie);
        Assert.Null(solution.TargetSecondaryDie);
    }

    [Fact]
    public void OnlyTheRowsThisShotReadsHaveToBeThere()
    {
        // The control that must be accepted, and the one this project has twice paid for leaving
        // out: a profile that is missing rows a particular shot never looks at resolves that shot
        // normally. Demanding a complete profile would refuse a table whose vehicles are all one
        // gunnery grade and never go to ground.
        var sparse = new DirtsideRulesProfile(
            TestDieTables.Invented.FireControlDice.Remove(FireControlLevel.Superior),
            TestDieTables.Invented.PostureDice.Remove(DefensivePosture.TurretDown),
            TestDieTables.Invented.SignatureDice.Remove(5));

        var solution = HitResolution.Solve(
            sparse, FireControlLevel.Basic, WeaponRangeBand.Close, targetSignature: 2, DefensivePosture.HullDown);

        Assert.True(solution.CanFire);
        Assert.Null(solution.Reason);
    }

    [Theory]
    [InlineData(FireControlLevel.Superior, 3, DefensivePosture.None, "Superior")]
    [InlineData(FireControlLevel.Enhanced, 3, DefensivePosture.TurretDown, "TurretDown")]
    public void EachMissingRowIsNamedByItself(
        FireControlLevel fireControl,
        int signature,
        DefensivePosture posture,
        string expected)
    {
        // The refusal names the row rather than saying the profile is incomplete, so a table knows
        // which line to go and fill in.
        var sparse = new DirtsideRulesProfile(
            TestDieTables.Invented.FireControlDice.Remove(FireControlLevel.Superior),
            TestDieTables.Invented.PostureDice.Remove(DefensivePosture.TurretDown),
            TestDieTables.Invented.SignatureDice);

        var solution = HitResolution.Solve(sparse, fireControl, WeaponRangeBand.Medium, signature, posture);

        Assert.True(solution.IsMissingFromProfile);
        Assert.Contains(expected, solution.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingSignatureRowIsNamedByItsNumber()
    {
        var solution = HitResolution.Solve(
            TestDieTables.WithNoSignatureThree, FireControlLevel.Enhanced, WeaponRangeBand.Medium, 3);

        Assert.True(solution.IsMissingFromProfile);
        Assert.Contains("signature 3", solution.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTargetKeepsItsBetterDieRatherThanAddingTheTwo()
    {
        // The whole point of the second die: a good posture sets a floor on how hard the target is
        // to hit, rather than stacking with how quiet it is. Adding them would make a covered
        // vehicle nearly unhittable.
        var solution = HitResolution.Solve(
            TestDieTables.Invented,
            FireControlLevel.Enhanced,
            WeaponRangeBand.Medium,
            targetSignature: 4,
            posture: DefensivePosture.HullDown);

        // Firer 7, signature die 2, posture die 6. The target keeps the 6, not 8.
        var attempt = HitResolution.Roll(solution, new ScriptedDice(7, 2, 6));

        Assert.Equal(6, attempt.TargetScore);
        Assert.True(attempt.IsHit);
    }

    [Fact]
    public void TheWorkedShotComesOutAsTheProfileSaysItShould()
    {
        // An enhanced sight at medium range rolls the profile's D8. The target is signature 4, so a
        // D4 primary, hull down for a D8 secondary. Firer 7 against 2 and 6: the target keeps the 6,
        // and 7 beats it.
        var solution = HitResolution.Solve(
            TestDieTables.Invented,
            FireControlLevel.Enhanced,
            WeaponRangeBand.Medium,
            targetSignature: 4,
            posture: DefensivePosture.HullDown);

        Assert.Equal(QualityDie.D8, solution.FirerDie);
        Assert.Equal(QualityDie.D4, solution.TargetPrimaryDie);
        Assert.Equal(QualityDie.D8, solution.TargetSecondaryDie);

        var attempt = HitResolution.Roll(solution, new ScriptedDice(7, 2, 6));
        Assert.True(attempt.IsHit);
    }

    [Fact]
    public void MatchingTheTargetIsAMiss()
    {
        // Ties go to the defender, as everywhere else in the family. This has to stay a strict
        // comparison: the air-defence rules have a separate exact-tie branch that depends on it.
        var solution = HitResolution.Solve(
            TestDieTables.Invented, FireControlLevel.Enhanced, WeaponRangeBand.Medium, 3);

        Assert.False(HitResolution.Roll(solution, new ScriptedDice(5, 5)).IsHit);
        Assert.True(HitResolution.Roll(solution, new ScriptedDice(6, 5)).IsHit);
    }

    [Fact]
    public void ATargetDoingNothingRollsOnlyItsSignatureDie()
    {
        var solution = HitResolution.Solve(
            TestDieTables.Invented, FireControlLevel.Enhanced, WeaponRangeBand.Medium, 3);
        Assert.Null(solution.TargetSecondaryDie);

        var attempt = HitResolution.Roll(solution, new ScriptedDice(4, 3));
        Assert.Null(attempt.TargetSecondaryRoll);
        Assert.Equal(3, attempt.TargetScore);
    }

    [Fact]
    public void ATargetOutInTheOpenNeedsNoPostureRow()
    {
        // None is not a row on anybody's card, so a profile with an empty posture table still
        // settles a shot at a target that is doing nothing. Otherwise the very first shot of every
        // game would demand a table of dice that shot never reads.
        var noPostures = TestDieTables.Invented with
        {
            PostureDice = TestDieTables.Invented.PostureDice.Clear(),
        };

        var solution = HitResolution.Solve(
            noPostures, FireControlLevel.Enhanced, WeaponRangeBand.Medium, 3);

        Assert.True(solution.CanFire);
        Assert.Null(solution.TargetSecondaryDie);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void ASignatureOffTheChartIsRefusedRatherThanGuessedAt(int signature) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HitResolution.Solve(
                TestDieTables.Invented, FireControlLevel.Enhanced, WeaponRangeBand.Medium, signature));

    private sealed class ScriptedDice(params int[] rolls) : IQualityDiceRoller
    {
        private readonly Queue<int> _rolls = new(rolls);

        public int Roll(QualityDie die) => _rolls.Count > 0
            ? _rolls.Dequeue()
            : throw new InvalidOperationException($"The script ran out while rolling a {die}.");
    }
}
