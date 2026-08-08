using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// The four tests around the exchange: going in, standing to receive it, deciding after a round
/// whether to keep at it, and following through. Every threat level here is a parameter the caller
/// supplies from its own rules.
/// </summary>
public sealed class CloseAssaultNerveTests
{
    private static AssaultantProfile Unit(
        ConfidenceLevel confidence = ConfidenceLevel.Confident,
        DirtsideUnitKind kind = DirtsideUnitKind.DismountedInfantry) =>
        new(QualityDie.D8, LeadershipValue: 2, confidence, kind);

    private static QualityDiceRoller Rolls(int roll) => new QualityDiceRoller(_ => roll);

    private static QualityDiceRoller Rolls(params int[] sequence)
    {
        var next = 0;
        return new QualityDiceRoller(_ => sequence[next++]);
    }

    private static AssaultRound Round(int attackerStands, int attackerLosses, int defenderLosses) =>
        new(
            1,
            new AssaultSideResult("att", [], attackerStands, attackerLosses),
            new AssaultSideResult("def", [], 4, defenderLosses),
            DefenderCoverStillCounted: true);

    [Fact]
    public void GoingInIsAReactionTestSoFailingCostsTheActionAndNotALevel()
    {
        // Troops that will not charge have not lost their nerve. They have lost their combat action,
        // and may be ordered forward again next activation.
        var refused = CloseAssault.Launch(Unit(), threatLevel: 1, Rolls(2));
        var willing = CloseAssault.Launch(Unit(), threatLevel: 1, Rolls(5));

        Assert.False(refused.Passed);
        Assert.True(willing.Passed);
        Assert.Equal(3, refused.ScoreToBeat);
    }

    [Theory]
    [InlineData(ConfidenceLevel.Broken)]
    [InlineData(ConfidenceLevel.Routed)]
    public void AUnitWhoseNerveHasGoneIsRefusedOutrightRatherThanByABadRoll(ConfidenceLevel level)
    {
        // No die is thrown at all: this is not a test that can be passed on a good roll.
        Assert.False(CloseAssault.MayLaunch(Unit(level)));
        Assert.Throws<InvalidOperationException>(() =>
            CloseAssault.Launch(Unit(level), threatLevel: 0, Rolls(8)));
    }

    [Fact]
    public void ShakenArmourWillNotChargeEitherThoughShakenFootWill()
    {
        Assert.False(CloseAssault.MayLaunch(Unit(ConfidenceLevel.Shaken, DirtsideUnitKind.Armour)));
        Assert.True(CloseAssault.MayLaunch(Unit(ConfidenceLevel.Shaken)));
    }

    [Fact]
    public void TheDefendersTestIsAConfidenceTestSoBeingDrivenOffCostsMoraleToo()
    {
        // A defender can be pushed off a position without a shot being exchanged, and arrives at the
        // next position worse than it left this one.
        var driven = CloseAssault.StandOrWithdraw(
            Unit(ConfidenceLevel.Steady), threatLevel: 2, Rolls(2));

        Assert.True(driven.Withdraws);
        Assert.Equal(ConfidenceLevel.Broken, driven.Confidence);
        Assert.NotNull(driven.Test);

        var held = CloseAssault.StandOrWithdraw(Unit(ConfidenceLevel.Steady), threatLevel: 2, Rolls(6));

        Assert.True(held.Stands);
        Assert.Equal(ConfidenceLevel.Steady, held.Confidence);
    }

    [Fact]
    public void ADefenderWhoseNerveHadAlreadyGoneNeverGetsAsFarAsTheRoll()
    {
        // Shaken armour breaks outright, and the null test is how a caller tells a defender that
        // failed from one that never tried.
        var crew = CloseAssault.StandOrWithdraw(
            Unit(ConfidenceLevel.Shaken, DirtsideUnitKind.Armour), threatLevel: 0, Rolls(8));

        Assert.True(crew.Withdraws);
        Assert.Null(crew.Test);
        Assert.Equal(ConfidenceLevel.Routed, crew.Confidence);
    }

    [Fact]
    public void EachSideTestsAtItsOwnCasualtyShareAfterARound()
    {
        var round = Round(attackerStands: 4, attackerLosses: 1, defenderLosses: 2);

        Assert.Equal(1, CloseAssault.ThreatAfterRound(round.Attacker, 1, 3));
        Assert.Equal(3, CloseAssault.ThreatAfterRound(round.Defender, 1, 3));
    }

    [Fact]
    public void TheDefenderTestsFirstAndAnAttackerWhoWinsIsNeverAsked()
    {
        // The order is a rule, not an implementation detail. An attacker cut to pieces still takes
        // the position if the defender broke first, and keeps whatever nerve he had left.
        var aftermath = CloseAssault.ResolveAftermath(
            Round(attackerStands: 4, attackerLosses: 3, defenderLosses: 3),
            Unit(),
            Unit(),
            lightCasualtyThreat: 1,
            heavyCasualtyThreat: 3,
            Rolls(1));

        Assert.Equal(AssaultOutcome.AttackerTakesThePosition, aftermath.Outcome);
        Assert.Null(aftermath.AttackerTest);
        Assert.True(aftermath.DefenderLeftUnderFire);
        Assert.False(aftermath.Continues);
    }

    [Fact]
    public void AnAttackerWhoBreaksAfterTheDefenderHeldFallsBack()
    {
        // Defender rolls first and holds; attacker rolls second and does not.
        var aftermath = CloseAssault.ResolveAftermath(
            Round(attackerStands: 4, attackerLosses: 1, defenderLosses: 1),
            Unit(),
            Unit(),
            lightCasualtyThreat: 1,
            heavyCasualtyThreat: 3,
            Rolls(6, 1));

        Assert.Equal(AssaultOutcome.AttackerFallsBack, aftermath.Outcome);
        Assert.True(aftermath.DefenderTest.Passed);
        Assert.False(aftermath.AttackerTest!.Value.Passed);
        Assert.True(aftermath.AttackerLeftUnderFire);
    }

    [Fact]
    public void TwoUnitsThatBothHoldGoAgain()
    {
        var aftermath = CloseAssault.ResolveAftermath(
            Round(attackerStands: 4, attackerLosses: 1, defenderLosses: 1),
            Unit(),
            Unit(),
            lightCasualtyThreat: 1,
            heavyCasualtyThreat: 3,
            Rolls(6, 7));

        Assert.Equal(AssaultOutcome.AnotherRound, aftermath.Outcome);
        Assert.True(aftermath.Continues);
        Assert.False(aftermath.AttackerLeftUnderFire);
        Assert.False(aftermath.DefenderLeftUnderFire);
    }

    [Fact]
    public void AHeavilyCutUpSideTestsAgainstTheHarderNumber()
    {
        // Half the stands gone is the heavy threat, and against a leadership of 2 that is a score of
        // 5 rather than 3 - so a roll of 4 holds in one case and fails in the other.
        var light = CloseAssault.ResolveAftermath(
            Round(attackerStands: 4, attackerLosses: 0, defenderLosses: 1),
            Unit(),
            Unit(),
            lightCasualtyThreat: 1,
            heavyCasualtyThreat: 3,
            Rolls(4, 8));

        var heavy = CloseAssault.ResolveAftermath(
            Round(attackerStands: 4, attackerLosses: 0, defenderLosses: 2),
            Unit(),
            Unit(),
            lightCasualtyThreat: 1,
            heavyCasualtyThreat: 3,
            Rolls(4, 8));

        Assert.Equal(AssaultOutcome.AnotherRound, light.Outcome);
        Assert.Equal(AssaultOutcome.AttackerTakesThePosition, heavy.Outcome);
    }

    [Fact]
    public void FollowingThroughIsATestForAnExtraActivationAndCostsNothingToFail()
    {
        var pressed = CloseAssault.FollowThrough(Unit(), threatLevel: 1, Rolls(6));
        var stopped = CloseAssault.FollowThrough(Unit(), threatLevel: 1, Rolls(3));

        Assert.True(pressed.Passed);
        Assert.False(stopped.Passed);
        Assert.Equal(3, stopped.ScoreToBeat);
    }

    [Fact]
    public void NoneOfTheTestsWillRunOnMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => CloseAssault.MayLaunch(null!));
        Assert.Throws<ArgumentNullException>(() => CloseAssault.Launch(Unit(), 0, null!));
        Assert.Throws<ArgumentNullException>(() => CloseAssault.StandOrWithdraw(null!, 0, Rolls(4)));
        Assert.Throws<ArgumentNullException>(() => CloseAssault.ThreatAfterRound(null!, 1, 3));
        Assert.Throws<ArgumentNullException>(() => CloseAssault.FollowThrough(Unit(), 0, null!));
        Assert.Throws<ArgumentNullException>(() =>
            CloseAssault.ResolveAftermath(null!, Unit(), Unit(), 1, 3, Rolls(4)));
    }
}
