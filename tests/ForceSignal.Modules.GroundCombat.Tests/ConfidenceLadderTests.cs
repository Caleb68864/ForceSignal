using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;

namespace ForceSignal.Modules.GroundCombat.Tests;

/// <summary>
/// The five levels and the one test procedure both games share. What a level stops a unit doing is
/// not here on purpose - the two games answer that differently and each keeps its own table.
/// </summary>
public sealed class ConfidenceLadderTests
{
    [Fact]
    public void TheTestHasToExceedTheScoreRatherThanMatchIt()
    {
        // The rule that runs through both games. Against a score of 4, a 4 is a failure.
        Assert.False(Test(QualityDie.D8, 2, 2, roll: 4).Passed);
        Assert.True(Test(QualityDie.D8, 2, 2, roll: 5).Passed);
    }

    [Theory]
    // Score of 4: five or better holds, three or four costs one, two or less costs two.
    [InlineData(5, 0)]
    [InlineData(4, 1)]
    [InlineData(3, 1)]
    [InlineData(2, 2)]
    [InlineData(1, 2)]
    public void FailingBadlyCostsTwoLevelsRatherThanOne(int roll, int expected) =>
        Assert.Equal(expected, Test(QualityDie.D8, 2, 2, roll).LevelsLost);

    [Fact]
    public void HalvingRoundsDownSoAnOddScoreIsHarderToFailBadly()
    {
        // Against a score of 5 it is a 2 or less that costs two levels, not a 2.5.
        Assert.Equal(2, Test(QualityDie.D8, 3, 2, roll: 2).LevelsLost);
        Assert.Equal(1, Test(QualityDie.D8, 3, 2, roll: 3).LevelsLost);
    }

    [Fact]
    public void AScoreTheDieCannotBeatIsStillRolled()
    {
        // The unit cannot pass, but whether it loses one level or two is still an open question.
        Assert.Equal(1, Test(QualityDie.D6, 4, 4, roll: 5).LevelsLost);
        Assert.Equal(2, Test(QualityDie.D6, 4, 4, roll: 3).LevelsLost);
    }

    [Fact]
    public void ThreatLevelsAreNeverCumulative()
    {
        // A unit that loses its leader while taking heavy casualties tests once, against the worse of
        // the two. Adding them would roughly double how fast morale collapses.
        Assert.Equal(3, ConfidenceLadder.Threat(1, 3, 2));
        Assert.Equal(3, ConfidenceLadder.Threat(3));
        Assert.Equal(0, ConfidenceLadder.Threat());
    }

    [Fact]
    public void TheScoreIsLeadershipPlusTheSingleHighestThreat() =>
        Assert.Equal(
            5,
            ConfidenceLadder.ScoreToBeat(leadershipValue: 2, ConfidenceLadder.Threat(1, 3)));

    [Theory]
    [InlineData(ConfidenceLevel.Confident, 1, ConfidenceLevel.Steady)]
    [InlineData(ConfidenceLevel.Confident, 2, ConfidenceLevel.Shaken)]
    [InlineData(ConfidenceLevel.Broken, 2, ConfidenceLevel.Routed)]
    [InlineData(ConfidenceLevel.Routed, 2, ConfidenceLevel.Routed)]
    [InlineData(ConfidenceLevel.Steady, 0, ConfidenceLevel.Steady)]
    public void TheLadderStopsAtTheBottom(ConfidenceLevel from, int levels, ConfidenceLevel expected) =>
        Assert.Equal(expected, ConfidenceLadder.Drop(from, levels));

    [Fact]
    public void RallyingIsHeldToWhateverCeilingTheGameSupplies()
    {
        // The ceiling is a parameter because one game caps a rally by how tired the force started and
        // the other does not cap it at all.
        Assert.Equal(
            ConfidenceLevel.Steady,
            ConfidenceLadder.Raise(ConfidenceLevel.Broken, 3, ConfidenceLevel.Steady));
        Assert.Equal(
            ConfidenceLevel.Confident,
            ConfidenceLadder.Raise(ConfidenceLevel.Broken, 3, ConfidenceLadder.Best));
    }

    [Fact]
    public void RallyingNeverDragsAUnitBackDown() =>
        Assert.Equal(
            ConfidenceLevel.Steady,
            ConfidenceLadder.Raise(ConfidenceLevel.Steady, 0, ConfidenceLadder.Best));

    [Fact]
    public void ATestRemembersWhereTheUnitStoodAndWhereItEndedUp()
    {
        var test = Test(QualityDie.D8, 2, 2, roll: 2, ConfidenceLevel.Steady);

        Assert.Equal(ConfidenceLevel.Steady, test.Before);
        Assert.Equal(ConfidenceLevel.Broken, test.After);
        Assert.Equal(4, test.ScoreToBeat);
        Assert.Equal(2, test.Roll);
    }

    [Fact]
    public void AReactionTestIsTheSameArithmeticAndCostsTheActionRatherThanALevel()
    {
        // Same score, same exceed-don't-match rule. What is missing is the badly-failed tier: there
        // is no level at stake, so how far short the roll fell changes nothing at all.
        var failed = ConfidenceLadder.React(QualityDie.D8, 2, 2, new QualityDiceRoller(_ => 1));
        var passed = ConfidenceLadder.React(QualityDie.D8, 2, 2, new QualityDiceRoller(_ => 5));

        Assert.False(failed.Passed);
        Assert.True(passed.Passed);
        Assert.Equal(4, failed.ScoreToBeat);
        Assert.Equal(failed.ScoreToBeat, passed.ScoreToBeat);
    }

    [Fact]
    public void AReactionTestMatchingTheScoreFailsJustLikeEverythingElse() =>
        Assert.False(ConfidenceLadder.React(QualityDie.D8, 2, 2, new QualityDiceRoller(_ => 4)).Passed);

    [Fact]
    public void NeitherTestWillRunWithoutADieSource()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ConfidenceLadder.Test(ConfidenceLevel.Steady, QualityDie.D8, 2, 2, null!));
        Assert.Throws<ArgumentNullException>(() =>
            ConfidenceLadder.React(QualityDie.D8, 2, 2, null!));
        Assert.Throws<ArgumentNullException>(() => ConfidenceLadder.Threat(null!));
    }

    private static ConfidenceTest Test(
        QualityDie quality,
        int leadership,
        int threat,
        int roll,
        ConfidenceLevel from = ConfidenceLevel.Confident) =>
        ConfidenceLadder.Test(from, quality, leadership, threat, new QualityDiceRoller(_ => roll));
}

/// <summary>
/// The grade-to-die lookup. It is a lookup rather than an enum because the two games do not have the
/// same number of grades, and the mapping itself is the user's to fill in from their own rules.
/// </summary>
public sealed class QualityGradesTests
{
    private enum ThreeGrades
    {
        Low,
        Middle,
        High,
    }

    [Fact]
    public void AGameWithThreeGradesAndAGameWithFiveUseTheSameLadder()
    {
        // The whole point of the seam. Neither table is shipped; both are the caller's.
        var small = new QualityGrades<ThreeGrades>(
        [
            new(ThreeGrades.Low, QualityDie.D6),
            new(ThreeGrades.Middle, QualityDie.D8),
            new(ThreeGrades.High, QualityDie.D10),
        ]);

        var large = new QualityGrades<string>(
        [
            new("worst", QualityDie.D4),
            new("worse", QualityDie.D6),
            new("middle", QualityDie.D8),
            new("better", QualityDie.D10),
            new("best", QualityDie.D12),
        ]);

        Assert.Equal(3, small.Count);
        Assert.Equal(5, large.Count);
        Assert.Equal(QualityDie.D8, small.DieFor(ThreeGrades.Middle));
        Assert.Equal(QualityDie.D12, large.DieFor("best"));
    }

    [Fact]
    public void AGradeThisGameDoesNotHaveIsRefusedRatherThanGuessedAt()
    {
        var grades = new QualityGrades<string>([new("only", QualityDie.D8)]);

        Assert.Throws<ArgumentException>(() => grades.DieFor("elite"));
        Assert.False(grades.TryDieFor("elite", out _));
        Assert.True(grades.TryDieFor("only", out var die));
        Assert.Equal(QualityDie.D8, die);
        Assert.Equal(["only"], grades.Grades);
    }

    [Fact]
    public void ATableIsCheckedWhileTheForceIsBeingBuiltRatherThanMidTest()
    {
        // A die that is not on the ladder, or a grade listed twice, is a typo in the caller's table -
        // and the moment to find it is now, not in the middle of a test nobody can re-roll.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QualityGrades<string>([new("odd", (QualityDie)7)]));

        Assert.Throws<ArgumentException>(() =>
            new QualityGrades<string>([new("same", QualityDie.D6), new("same", QualityDie.D8)]));

        Assert.Throws<ArgumentNullException>(() => new QualityGrades<string>(null!));
    }
}
