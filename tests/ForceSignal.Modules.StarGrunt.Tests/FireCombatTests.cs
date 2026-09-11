using ForceSignal.Modules.StarGrunt.Combat;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// The fire engine end to end, against the invented range table in <see cref="TestRangeTable"/>. The
/// firefight's arithmetic is pinned to a case worked out by hand, because the step that turns a fire
/// total into hits is unusual enough to be worth it; which die the target throws is the table's.
/// </summary>
public sealed class FireCombatTests
{
    private static FireCombat Combat(ScriptedDice dice) => new(TestRangeTable.Invented, dice);

    [Theory]
    // Armour that matches the impact stops it: a roll must exceed, never equal.
    [InlineData(4, 4, HitEffect.Stopped)]
    [InlineData(3, 5, HitEffect.Stopped)]
    [InlineData(5, 4, HitEffect.Wound)]
    [InlineData(8, 4, HitEffect.Wound)]
    // More than twice the armour, not merely twice it.
    [InlineData(9, 4, HitEffect.Kill)]
    [InlineData(3, 1, HitEffect.Kill)]
    [InlineData(2, 1, HitEffect.Wound)]
    public void AHitIsReadAgainstArmourWithNoModifiersAtAll(int impact, int armour, HitEffect expected) =>
        Assert.Equal(expected, FireCombat.Compare(impact, armour));

    [Fact]
    public void TheWorkedFirefightComesOutAsWritten()
    {
        // A D8 squad with rifles and a support weapon fires at infantry in soft cover nine inches
        // away. On the invented table that is the second band, whose row is a D4, and soft cover's
        // two rungs make it a D8.
        // The firer rolls 6, 7 and 5 against the target's 4 - three dice through, fully effective.
        // The total of 18 divided by the D8's type is two hits, remainder two; the target rolls a 5
        // for the leftover, which is more than two, so no third hit.
        // The two hits are D10 impact against D6 armour moved two rungs to D10 by the same cover:
        // 3 vs 5 is stopped, 9 vs 4 is more than twice and kills.
        var dice = new ScriptedDice(6, 7, 5, 4, 5, 3, 5, 9, 4);

        var outcome = Combat(dice).Resolve(new FireAttempt(
            FirerQuality: QualityDie.D8,
            FirepowerDie: QualityDie.D10,
            SupportDice: [QualityDie.D8],
            ImpactDie: QualityDie.D10,
            TargetArmourDie: QualityDie.D6,
            DistanceInches: 9,
            TargetPosture: new TargetPosture(CoverLevel.Soft)));

        Assert.Equal(QualityDie.D8, outcome.Range.RangeDie);
        Assert.Equal(3, outcome.Opposed.Beats);
        Assert.Equal(OpposedResult.Effective, outcome.Opposed.Result);
        Assert.Equal(18, outcome.Opposed.ActorTotal);
        Assert.Equal(5, outcome.RemainderRoll);
        Assert.Equal(2, outcome.PotentialHits);
        Assert.Equal(1, outcome.Kills);
        Assert.Equal(0, outcome.Wounds);
        Assert.True(outcome.Suppresses);
    }

    [Fact]
    public void TheLeftoverPointsBuyAnExtraHitWhenTheTargetRollsLowEnough()
    {
        // Same shape as above, but the target rolls a 2 for the leftover of 2 - equal to it, which
        // the rules say lands. This is the one comparison in the game that is not strictly
        // greater-than, so it is worth its own case.
        var dice = new ScriptedDice(6, 7, 5, 4, 2, 3, 5, 9, 4, 7, 2);

        var outcome = Combat(dice).Resolve(new FireAttempt(
            QualityDie.D8, QualityDie.D10, [QualityDie.D8],
            QualityDie.D10, QualityDie.D6, 9, new TargetPosture(CoverLevel.Soft)));

        Assert.Equal(2, outcome.RemainderRoll);
        Assert.Equal(3, outcome.PotentialHits);
        Assert.Equal(3, outcome.Hits.Count);
    }

    [Fact]
    public void NoDiceThroughIsNothingAtAll()
    {
        // Every firer die under the target's: no suppression, no hits, nothing.
        var dice = new ScriptedDice(1, 2, 3, 7);

        var outcome = Combat(dice).Resolve(new FireAttempt(
            QualityDie.D8, QualityDie.D10, [QualityDie.D8],
            QualityDie.D10, QualityDie.D6, 9, new TargetPosture(CoverLevel.Soft)));

        Assert.Equal(OpposedResult.Failed, outcome.Opposed.Result);
        Assert.False(outcome.Suppresses);
        Assert.Equal(0, outcome.PotentialHits);
        Assert.Empty(outcome.Hits);
    }

    [Fact]
    public void OneDieThroughPinsHeadsDownWithoutHurtingAnyone()
    {
        // Suppression, not casualties, is the ordinary currency of a firefight.
        var dice = new ScriptedDice(8, 2, 3, 7);

        var outcome = Combat(dice).Resolve(new FireAttempt(
            QualityDie.D8, QualityDie.D10, [QualityDie.D8],
            QualityDie.D10, QualityDie.D6, 9, new TargetPosture(CoverLevel.Soft)));

        Assert.Equal(OpposedResult.Suppressed, outcome.Opposed.Result);
        Assert.True(outcome.Suppresses);
        Assert.Equal(0, outcome.PotentialHits);
        Assert.Empty(outcome.Hits);
    }

    [Fact]
    public void ATieDoesNotCountAsADieGettingThrough()
    {
        var dice = new ScriptedDice(7, 7, 7, 7);

        var outcome = Combat(dice).Resolve(new FireAttempt(
            QualityDie.D8, QualityDie.D10, [QualityDie.D8],
            QualityDie.D10, QualityDie.D6, 9, new TargetPosture(CoverLevel.Soft)));

        Assert.Equal(0, outcome.Opposed.Beats);
        Assert.False(outcome.Suppresses);
    }

    [Fact]
    public void ShotsBeyondEffectiveRangeAreNotRolledAtAll()
    {
        // Forty-five inches is the seventh band, and the table gives small arms four.
        var dice = new ScriptedDice(12, 12, 12, 1);

        var outcome = Combat(dice).Resolve(new FireAttempt(
            QualityDie.D8, QualityDie.D10, [QualityDie.D8],
            QualityDie.D10, QualityDie.D6, 45, new TargetPosture(CoverLevel.None)));

        Assert.False(outcome.Range.CanFireEffectively);
        Assert.False(outcome.Suppresses);
        Assert.Empty(outcome.Hits);
        // Nothing was rolled, so the whole script is still waiting.
        Assert.Equal(4, dice.Remaining);
    }

    [Fact]
    public void AShotTheTableCannotSettleIsNotRolledAndSaysWhy()
    {
        // The engine's own answer for a gap in the table: nothing thrown, and the range solution
        // carries the refusal so a caller can tell it from a shot that was merely too long. The game
        // refuses before it gets here; this is what it would find if it did not.
        var dice = new ScriptedDice(6, 7, 5, 4);

        var outcome = new FireCombat(TestRangeTable.Blank, dice).Resolve(new FireAttempt(
            QualityDie.D8, QualityDie.D10, [QualityDie.D8],
            QualityDie.D10, QualityDie.D6, 9, new TargetPosture(CoverLevel.Soft)));

        Assert.True(outcome.Range.IsMissingFromProfile);
        Assert.Empty(outcome.Hits);
        Assert.Equal(4, dice.Remaining);
    }

    [Fact]
    public void CoverShiftsTheArmourDieAsWellAsTheRangeDie()
    {
        // Soft cover is two rungs on this table, and it goes on the armour as well as the range die:
        // D4 armour is thrown as a D8. Asserted on the die handed to the roller, because a scripted
        // roll proves nothing about which die it was scripted for.
        // Firer 5 and 4 against a range roll of 3: both through, total 9. The range die is a D4 moved
        // two rungs to a D8, so that is one hit and a leftover of 1, which the leftover roll of 8 does
        // not claim. Then impact 9 against armour 7.
        var dice = new ScriptedDice(5, 4, 3, 8, 9, 7);

        var outcome = Combat(dice).Resolve(new FireAttempt(
            FirerQuality: QualityDie.D8,
            FirepowerDie: QualityDie.D8,
            SupportDice: [],
            ImpactDie: QualityDie.D10,
            TargetArmourDie: QualityDie.D4,
            DistanceInches: 4,
            TargetPosture: new TargetPosture(CoverLevel.Soft)));

        var hit = Assert.Single(outcome.Hits);
        Assert.Equal(QualityDie.D8, dice.Rolled[^1]);
        Assert.Equal(QualityDie.D10, dice.Rolled[^2]);
        Assert.Equal(HitEffect.Wound, hit.Effect);
    }

    [Fact]
    public void AVolleyWithNoSupportWeaponsStillThrowsTwoDice()
    {
        // Quality and firepower only: two dice, never one. Firer 5 and 4 against a range roll of 3
        // is a total of 9 over a D4 range die, so two hits and a leftover of 1 the roll of 4 misses.
        var dice = new ScriptedDice(5, 4, 3, 4, 1, 1, 1, 1);

        var outcome = Combat(dice).Resolve(new FireAttempt(
            QualityDie.D8, QualityDie.D8, [],
            QualityDie.D10, QualityDie.D6, 4, new TargetPosture(CoverLevel.None)));

        Assert.Equal(2, outcome.Opposed.ActorRolls.Count);
        Assert.Equal(OpposedResult.Effective, outcome.Opposed.Result);
        Assert.Equal(2, outcome.PotentialHits);
    }

    [Fact]
    public void ANullAttemptIsRefused() =>
        Assert.Throws<ArgumentNullException>(() => Combat(new ScriptedDice()).Resolve(null!));

    [Fact]
    public void AFireEngineWithNoTableIsRefusedRatherThanGivenOne() =>
        Assert.Throws<ArgumentNullException>(() => new FireCombat(null!));

    [Fact]
    public void ArmourAlreadyAtTheTopOfTheLadderDegradesTheWeaponInstead()
    {
        // Impact against armour is an OPEN shift, which only shows at the top of the ladder. Armour
        // of D10 behind hard cover's four rungs cannot climb four: it caps at D12, and the three
        // leftover steps come off the incoming weapon, dropping a D12 impact to a D6. Treating this
        // as an ordinary capped shift would have thrown that protection away.
        // Firer 5 and 4 against a range roll of 3 is a total of 9 over a D12 range die (the bottom
        // row moved four rungs): no whole hit, a leftover of 9, and the leftover roll of 8 claims it.
        var dice = new ScriptedDice(5, 4, 3, 8, 6, 12);

        var outcome = Combat(dice).Resolve(new FireAttempt(
            FirerQuality: QualityDie.D8,
            FirepowerDie: QualityDie.D8,
            SupportDice: [],
            ImpactDie: QualityDie.D12,
            TargetArmourDie: QualityDie.D10,
            DistanceInches: 4,
            TargetPosture: new TargetPosture(CoverLevel.Hard)));

        var hit = Assert.Single(outcome.Hits);
        Assert.Equal(QualityDie.D6, dice.Rolled[^2]);
        Assert.Equal(QualityDie.D12, dice.Rolled[^1]);
        Assert.Equal(HitEffect.Stopped, hit.Effect);
    }
}
