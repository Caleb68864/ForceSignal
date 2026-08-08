using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.StarGrunt.Morale;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Morale is what most fire actually achieves in this game, so the ladder and the tests that move
/// a unit down it matter more than the casualty rules do.
/// </summary>
public sealed class MoraleTests
{
    [Theory]
    [InlineData(FatigueLevel.Fresh, ConfidenceLevel.Confident)]
    [InlineData(FatigueLevel.Tired, ConfidenceLevel.Steady)]
    [InlineData(FatigueLevel.Exhausted, ConfidenceLevel.Shaken)]
    public void HowWornAForceIsSetsWhereItStarts(FatigueLevel fatigue, ConfidenceLevel expected) =>
        Assert.Equal(expected, Confidence.StartingLevel(fatigue));

    [Fact]
    public void TheWorkedConfidenceTestFromTheRulesNotesComesOutAsWritten()
    {
        // A steady regular unit with leadership 2 takes its first casualty, threat level 2 at
        // medium motivation. The score to beat is 4. Five or better holds; three or four costs one
        // level; two or less is half the score and costs two.
        Assert.Equal(0, Run(5).LevelsLost);
        Assert.Equal(ConfidenceLevel.Steady, Run(5).After);

        Assert.Equal(1, Run(4).LevelsLost);
        Assert.Equal(ConfidenceLevel.Shaken, Run(4).After);
        Assert.Equal(1, Run(3).LevelsLost);

        Assert.Equal(2, Run(2).LevelsLost);
        Assert.Equal(ConfidenceLevel.Broken, Run(2).After);
        Assert.Equal(2, Run(1).LevelsLost);

        static ConfidenceTest Run(int roll) => Confidence.Test(
            ConfidenceLevel.Steady,
            QualityDie.D8,
            leadershipValue: 2,
            threatLevel: 2,
            new QualityDiceRoller(_ => roll));
    }

    [Fact]
    public void MatchingTheScoreIsAFailure()
    {
        // The rule that runs through the whole game: a roll must exceed, never equal.
        var test = Confidence.Test(
            ConfidenceLevel.Confident, QualityDie.D8, 2, 2, new QualityDiceRoller(_ => 4));

        Assert.False(test.Passed);
        Assert.Equal(1, test.LevelsLost);
    }

    [Fact]
    public void AScoreTheDieCannotBeatIsStillRolledToSeeHowBadlyItGoes()
    {
        // A green unit rolling a d6 against a score of 8 cannot pass. The question that remains is
        // whether it loses one level or two, so the roll is still made.
        var onlyJust = Confidence.Test(
            ConfidenceLevel.Steady, QualityDie.D6, 4, 4, new QualityDiceRoller(_ => 5));
        Assert.Equal(1, onlyJust.LevelsLost);

        var badly = Confidence.Test(
            ConfidenceLevel.Steady, QualityDie.D6, 4, 4, new QualityDiceRoller(_ => 3));
        Assert.Equal(2, badly.LevelsLost);
    }

    [Theory]
    [InlineData(ConfidenceLevel.Confident, 1, ConfidenceLevel.Steady)]
    [InlineData(ConfidenceLevel.Confident, 2, ConfidenceLevel.Shaken)]
    [InlineData(ConfidenceLevel.Broken, 2, ConfidenceLevel.Routed)]
    [InlineData(ConfidenceLevel.Routed, 2, ConfidenceLevel.Routed)]
    public void TheLadderStopsAtTheBottom(ConfidenceLevel from, int levels, ConfidenceLevel expected) =>
        Assert.Equal(expected, Confidence.Drop(from, levels));

    [Fact]
    public void AUnitThatStartedTiredCanNeverBeRalliedBackPastWhereItBegan()
    {
        // Fatigue is a ceiling as well as a starting point: a force that took the field already
        // worn does not recover its edge during the battle.
        Assert.Equal(ConfidenceLevel.Steady, Confidence.Rally(ConfidenceLevel.Broken, 3, FatigueLevel.Tired));
        Assert.Equal(ConfidenceLevel.Shaken, Confidence.Rally(ConfidenceLevel.Routed, 4, FatigueLevel.Exhausted));
        Assert.Equal(ConfidenceLevel.Confident, Confidence.Rally(ConfidenceLevel.Broken, 3, FatigueLevel.Fresh));
    }

    [Fact]
    public void RallyingNeverDragsAUnitBackDown() =>
        Assert.Equal(ConfidenceLevel.Steady, Confidence.Rally(ConfidenceLevel.Steady, 0, FatigueLevel.Tired));

    [Theory]
    [InlineData(ConfidenceLevel.Confident, false)]
    [InlineData(ConfidenceLevel.Steady, false)]
    [InlineData(ConfidenceLevel.Shaken, true)]
    [InlineData(ConfidenceLevel.Broken, true)]
    public void RestrictionsAreCumulativeDownTheLadder(ConfidenceLevel level, bool needsTest) =>
        Assert.Equal(needsTest, Confidence.NeedsReactionTestToAdvance(level));

    [Fact]
    public void ABrokenUnitOnlyShootsBackAndARoutedOneDoesNotShootAtAll()
    {
        Assert.True(Confidence.FiresOnlyAtWhoeverFiredOnIt(ConfidenceLevel.Broken));
        Assert.False(Confidence.FiresOnlyAtWhoeverFiredOnIt(ConfidenceLevel.Steady));
        Assert.True(Confidence.HasStoppedFighting(ConfidenceLevel.Routed));
        Assert.False(Confidence.HasStoppedFighting(ConfidenceLevel.Broken));
    }

    [Fact]
    public void ABrokenUnitThatIsChargedGoesStraightToTheBottomWithoutTesting()
    {
        Assert.Equal(ConfidenceLevel.Routed, Confidence.OnCloseAssaulted(ConfidenceLevel.Broken));
        Assert.Equal(ConfidenceLevel.Steady, Confidence.OnCloseAssaulted(ConfidenceLevel.Steady));
    }

    [Fact]
    public void SuppressionStacksToACeilingAndNoFurther()
    {
        var markers = 0;
        for (var fire = 0; fire < 6; fire++)
        {
            markers = Suppression.Add(markers);
        }

        Assert.Equal(Suppression.MaxMarkers, markers);
        Assert.True(Suppression.IsSuppressed(markers));
        Assert.False(Suppression.IsSuppressed(0));
    }

    [Fact]
    public void ClearingSuppressionTakesOneRollForOneMarker()
    {
        // Slow on purpose: a unit caught under sustained fire stays out of the fight for several
        // turns without anyone being hurt.
        var passed = Suppression.TryClear(3, QualityDie.D8, leadershipValue: 2, new QualityDiceRoller(_ => 5));
        Assert.True(passed.Cleared);
        Assert.Equal(2, passed.Remaining);

        var failed = Suppression.TryClear(3, QualityDie.D8, leadershipValue: 2, new QualityDiceRoller(_ => 2));
        Assert.False(failed.Cleared);
        Assert.Equal(3, failed.Remaining);
    }

    [Fact]
    public void ClearingSuppressionAlsoNeedsToExceedRatherThanMatch()
    {
        var onTheNose = Suppression.TryClear(1, QualityDie.D8, leadershipValue: 3, new QualityDiceRoller(_ => 3));
        Assert.False(onTheNose.Cleared);
    }

    [Fact]
    public void AUnitThatIsNotSuppressedHasNothingToClear() =>
        Assert.Throws<InvalidOperationException>(() =>
            Suppression.TryClear(0, QualityDie.D8, 2, new QualityDiceRoller()));

    [Theory]
    [InlineData(SuppressedAction.Move, false, false)]
    [InlineData(SuppressedAction.Fire, false, false)]
    [InlineData(SuppressedAction.Observe, false, true)]
    [InlineData(SuppressedAction.Communicate, false, true)]
    [InlineData(SuppressedAction.RemoveSuppression, false, true)]
    // Sorting a unit out needs something to do it behind.
    [InlineData(SuppressedAction.Reorganise, false, false)]
    [InlineData(SuppressedAction.Reorganise, true, true)]
    public void APinnedUnitMayLookTalkAndTryToGetItsHeadUpAndLittleElse(
        SuppressedAction action,
        bool isInCover,
        bool expected) =>
        Assert.Equal(expected, Suppression.Allows(1, action, isInCover));

    [Fact]
    public void AUnitWithNoMarkersMayDoAnythingAtAll() =>
        Assert.All(
            Enum.GetValues<SuppressedAction>(),
            action => Assert.True(Suppression.Allows(0, action)));
}
