using ForceSignal.Modules.StarGrunt.Combat;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// The fire engine end to end. The two worked examples from the rules notes run here as fixtures,
/// because the step that turns a fire total into hits is unusual enough to be worth pinning to a
/// case someone has already worked out by hand.
/// </summary>
public sealed class FireCombatTests
{
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
    public void TheWorkedFirefightFromTheRulesNotesComesOutAsWritten()
    {
        // A regular squad with rifles and a SAW fires at infantry in soft cover just over one band
        // away. The target's die is a D8: D4 base, one rung for the second band, one for the cover.
        // The firer rolls 6, 7 and 5 against the target's 4 - three dice through, fully effective.
        // The total of 18 divided by the D8's type is two hits, remainder two; the target rolls a 5
        // for the leftover, which is more than two, so no third hit.
        // The two hits are D10 impact against D6 armour shifted to D8 by the soft cover: 3 vs 5 is
        // stopped, 9 vs 4 is more than twice and kills.
        var dice = new ScriptedDice(6, 7, 5, 4, 5, 3, 5, 9, 4);
        var combat = new FireCombat(dice);

        var outcome = combat.Resolve(new FireAttempt(
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
        var combat = new FireCombat(dice);

        var outcome = combat.Resolve(new FireAttempt(
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
        var combat = new FireCombat(dice);

        var outcome = combat.Resolve(new FireAttempt(
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
        var combat = new FireCombat(dice);

        var outcome = combat.Resolve(new FireAttempt(
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
        var combat = new FireCombat(dice);

        var outcome = combat.Resolve(new FireAttempt(
            QualityDie.D8, QualityDie.D10, [QualityDie.D8],
            QualityDie.D10, QualityDie.D6, 9, new TargetPosture(CoverLevel.Soft)));

        Assert.Equal(0, outcome.Opposed.Beats);
        Assert.False(outcome.Suppresses);
    }

    [Fact]
    public void ShotsBeyondEffectiveRangeAreNotRolledAtAll()
    {
        var dice = new ScriptedDice(12, 12, 12, 1);
        var combat = new FireCombat(dice);

        var outcome = combat.Resolve(new FireAttempt(
            QualityDie.D8, QualityDie.D10, [QualityDie.D8],
            QualityDie.D10, QualityDie.D6, 45, new TargetPosture(CoverLevel.None)));

        Assert.False(outcome.Range.CanFireEffectively);
        Assert.False(outcome.Suppresses);
        Assert.Empty(outcome.Hits);
        // Nothing was rolled, so the whole script is still waiting.
        Assert.Equal(4, dice.Remaining);
    }

    [Fact]
    public void CoverShiftsTheArmourDieAsWellAsTheRangeDie()
    {
        // Armour of D4 in hard cover rolls as a D8, so the armour roll of 7 below is only possible
        // if the shift was applied. Without it the same fire would have killed someone.
        // Firer 5 and 4 against a range roll of 3: both through, total 9. The range die is a D8, so
        // that is one hit and a leftover of 1, which the leftover roll of 8 does not claim.
        var dice = new ScriptedDice(5, 4, 3, 8, 9, 7);
        var combat = new FireCombat(dice);

        var outcome = combat.Resolve(new FireAttempt(
            FirerQuality: QualityDie.D8,
            FirepowerDie: QualityDie.D8,
            SupportDice: [],
            ImpactDie: QualityDie.D10,
            TargetArmourDie: QualityDie.D4,
            DistanceInches: 4,
            TargetPosture: new TargetPosture(CoverLevel.Hard)));

        var hit = Assert.Single(outcome.Hits);
        Assert.Equal(7, hit.ArmourRoll);
        Assert.Equal(HitEffect.Wound, hit.Effect);
    }

    [Fact]
    public void AVolleyWithNoSupportWeaponsStillThrowsTwoDice()
    {
        // Quality and firepower only: two dice, never one. Firer 5 and 4 against a range roll of 3
        // is a total of 9 over a D4 range die, so two hits and a leftover of 1 the roll of 4 misses.
        var dice = new ScriptedDice(5, 4, 3, 4, 1, 1, 1, 1);
        var combat = new FireCombat(dice);

        var outcome = combat.Resolve(new FireAttempt(
            QualityDie.D8, QualityDie.D8, [],
            QualityDie.D10, QualityDie.D6, 4, new TargetPosture(CoverLevel.None)));

        Assert.Equal(2, outcome.Opposed.ActorRolls.Count);
        Assert.Equal(OpposedResult.Effective, outcome.Opposed.Result);
        Assert.Equal(2, outcome.PotentialHits);
    }

    [Fact]
    public void ANullAttemptIsRefused() =>
        Assert.Throws<ArgumentNullException>(() => new FireCombat().Resolve(null!));

    [Fact]
    public void ArmourAlreadyAtTheTopOfTheLadderDegradesTheWeaponInstead()
    {
        // Impact against armour is an OPEN shift, which only shows at the top of the ladder. Armour
        // of D10 with three shifts of cover cannot climb three rungs: it caps at D12 and the two
        // leftover steps come off the incoming weapon, dropping a D12 impact to D8. Treating this
        // as an ordinary capped shift would have thrown that protection away.
        // Firer 5 and 4 against a range roll of 3 is a total of 9 over a D8 range die: one hit,
        // leftover 1, and the leftover roll of 8 does not claim a second.
        var dice = new ScriptedDice(5, 4, 3, 8, 8, 12);
        var combat = new FireCombat(dice);

        var outcome = combat.Resolve(new FireAttempt(
            FirerQuality: QualityDie.D8,
            FirepowerDie: QualityDie.D8,
            SupportDice: [],
            ImpactDie: QualityDie.D12,
            TargetArmourDie: QualityDie.D10,
            DistanceInches: 4,
            TargetPosture: new TargetPosture(CoverLevel.Hard, InPosition: true)));

        var hit = Assert.Single(outcome.Hits);
        // The armour rolled a 12, which only a D12 can produce; the impact rolled an 8, which is
        // all a degraded D8 can produce. Armour matched or beat it, so the round was stopped.
        Assert.Equal(12, hit.ArmourRoll);
        Assert.Equal(8, hit.ImpactRoll);
        Assert.Equal(HitEffect.Stopped, hit.Effect);
    }

    /// <summary>A die source that hands out a fixed script, so a worked example can be replayed.</summary>
    private sealed class ScriptedDice(params int[] rolls) : IQualityDiceRoller
    {
        private readonly Queue<int> _rolls = new(rolls);

        public int Remaining => _rolls.Count;

        public int Roll(QualityDie die) => _rolls.Count > 0
            ? _rolls.Dequeue()
            : throw new InvalidOperationException($"The script ran out while rolling a {die}.");
    }
}
