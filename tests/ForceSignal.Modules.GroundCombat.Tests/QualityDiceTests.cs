using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.GroundCombat.Tests;

/// <summary>
/// The quality ladder is the arithmetic the whole engine rests on: every circumstance that would
/// be a plus or a minus in another game is a move along it here. These pin down where a shift
/// lands, what happens at the two ends, and the crossing-over that makes an open shift different
/// from a closed one.
/// </summary>
public sealed class QualityDiceTests
{
    [Theory]
    [InlineData(QualityDie.D4, 4)]
    [InlineData(QualityDie.D6, 6)]
    [InlineData(QualityDie.D8, 8)]
    [InlineData(QualityDie.D10, 10)]
    [InlineData(QualityDie.D12, 12)]
    public void ADieKnowsItsFaceCount(QualityDie die, int expected) =>
        Assert.Equal(expected, QualityDice.Faces(die));

    [Fact]
    public void TheLadderRunsWorstToBestWithNoD20Anywhere()
    {
        Assert.Equal(
            [QualityDie.D4, QualityDie.D6, QualityDie.D8, QualityDie.D10, QualityDie.D12],
            QualityDice.Ladder);
        Assert.All(QualityDice.Ladder, die => Assert.NotEqual(20, QualityDice.Faces(die)));
    }

    [Theory]
    [InlineData(QualityDie.D8, 1, QualityDie.D10)]
    [InlineData(QualityDie.D8, 2, QualityDie.D12)]
    [InlineData(QualityDie.D8, -1, QualityDie.D6)]
    [InlineData(QualityDie.D8, -2, QualityDie.D4)]
    [InlineData(QualityDie.D8, 0, QualityDie.D8)]
    public void AShiftMovesAlongTheLadderRatherThanAddingToTheRoll(
        QualityDie die,
        int steps,
        QualityDie expected) =>
        Assert.Equal(expected, QualityDice.ShiftClosed(die, steps));

    [Theory]
    // A closed shift simply stops at the end of the ladder and loses the rest.
    [InlineData(QualityDie.D10, 5, QualityDie.D12)]
    [InlineData(QualityDie.D12, 1, QualityDie.D12)]
    [InlineData(QualityDie.D6, -9, QualityDie.D4)]
    [InlineData(QualityDie.D4, -1, QualityDie.D4)]
    public void AClosedShiftIsCappedAtWhicheverEndItRunsInto(
        QualityDie die,
        int steps,
        QualityDie expected) =>
        Assert.Equal(expected, QualityDice.ShiftClosed(die, steps));

    [Theory]
    [InlineData(QualityDie.D8, 3, QualityDie.D12, 1)]
    [InlineData(QualityDie.D10, 4, QualityDie.D12, 3)]
    [InlineData(QualityDie.D6, -3, QualityDie.D4, -2)]
    [InlineData(QualityDie.D8, 2, QualityDie.D12, 0)]
    public void AShiftReportsWhatWouldNotFit(
        QualityDie die,
        int steps,
        QualityDie expectedDie,
        int expectedOverflow)
    {
        var shift = QualityDice.Shift(die, steps);

        Assert.Equal(expectedDie, shift.Die);
        Assert.Equal(expectedOverflow, shift.Overflow);
        Assert.Equal(expectedOverflow == 0, shift.IsClosed);
    }

    [Fact]
    public void TheWorkedExampleFromTheRulesNotesComesOutAsWritten()
    {
        // Troops in armour of D8 are caught by a D10 burst while in hard cover and in position:
        // three shifts to the armour. D8 cannot climb three rungs, so it caps at D12 and, because
        // this is an open shift, the one leftover step drops the incoming weapon from D10 to D8.
        var settled = QualityDice.ShiftOpposed(
            actor: QualityDie.D10,
            actorSteps: 0,
            opponent: QualityDie.D8,
            opponentSteps: 3);

        Assert.Equal(QualityDie.D12, settled.Opponent);
        Assert.Equal(QualityDie.D8, settled.Actor);
    }

    [Fact]
    public void AnOpenShiftThatFitsOnTheLadderLeavesTheOpponentAlone()
    {
        var settled = QualityDice.ShiftOpposed(
            actor: QualityDie.D10,
            actorSteps: 0,
            opponent: QualityDie.D6,
            opponentSteps: 2);

        Assert.Equal(QualityDie.D10, settled.Actor);
        Assert.Equal(QualityDie.D10, settled.Opponent);
    }

    [Fact]
    public void BothSidesCanOverflowAtOnceAndNeitherOrderingChangesTheAnswer()
    {
        // Each leftover is read from the first pass, so applying one cannot alter the other.
        var settled = QualityDice.ShiftOpposed(
            actor: QualityDie.D12,
            actorSteps: 2,
            opponent: QualityDie.D12,
            opponentSteps: 1);

        // The actor's two leftover steps push the opponent down two rungs from D12; the opponent's
        // one leftover step pushes the actor down one.
        Assert.Equal(QualityDie.D10, settled.Actor);
        Assert.Equal(QualityDie.D8, settled.Opponent);
    }

    [Fact]
    public void ACrossedOverLeftoverDoesNotBounceBackAgain()
    {
        // The excess is handed over once. A rally between the two dice is not what the rules
        // describe, and would not terminate in the general case.
        var settled = QualityDice.ShiftOpposed(
            actor: QualityDie.D4,
            actorSteps: 0,
            opponent: QualityDie.D12,
            opponentSteps: 6);

        // Six steps of which none fit: the actor takes all six downward and floors at D4.
        Assert.Equal(QualityDie.D4, settled.Actor);
        Assert.Equal(QualityDie.D12, settled.Opponent);
    }

    [Fact]
    public void ADieOffTheLadderIsRefusedRatherThanGuessedAt() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => QualityDice.Rung((QualityDie)7));
}
