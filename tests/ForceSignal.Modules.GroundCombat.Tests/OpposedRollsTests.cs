using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.GroundCombat.Tests;

/// <summary>
/// Every roll in the game is one of three shapes, and all three share one rule: the roll has to
/// exceed the number, not match it. A tie is a failure, which is the easiest thing to get wrong.
/// </summary>
public sealed class OpposedRollsTests
{
    [Theory]
    [InlineData(5, 4, true)]
    [InlineData(5, 5, false)]
    [InlineData(4, 5, false)]
    public void ARollAgainstAFixedNumberHasToExceedIt(int roll, int target, bool expected) =>
        Assert.Equal(expected, OpposedRolls.BeatsTarget(roll, target));

    [Theory]
    [InlineData(6, 5, true)]
    [InlineData(5, 5, false)]
    [InlineData(1, 12, false)]
    public void ASingleOpposedRollTiesInTheOpponentsFavour(int actor, int opponent, bool expected) =>
        Assert.Equal(expected, OpposedRolls.Wins(actor, opponent));

    [Theory]
    // None beat the target: nothing at all happens.
    [InlineData(new[] { 1, 2 }, 5, 0, OpposedResult.Failed)]
    // A tie is not a beat.
    [InlineData(new[] { 5, 3 }, 5, 0, OpposedResult.Failed)]
    // One through: enough to pin heads down, not enough to hurt anyone.
    [InlineData(new[] { 6, 2 }, 5, 1, OpposedResult.Suppressed)]
    // Two or more through: the attack lands properly.
    [InlineData(new[] { 6, 7 }, 5, 2, OpposedResult.Effective)]
    [InlineData(new[] { 6, 7, 8, 9 }, 5, 4, OpposedResult.Effective)]
    public void AMultipleOpposedRollIsScoredByHowManyDiceGetThrough(
        int[] actorRolls,
        int opponentRoll,
        int expectedBeats,
        OpposedResult expectedResult)
    {
        var resolved = OpposedRolls.Resolve(actorRolls, opponentRoll);

        Assert.Equal(expectedBeats, resolved.Beats);
        Assert.Equal(expectedResult, resolved.Result);
        Assert.Equal(opponentRoll, resolved.OpponentRoll);
    }

    [Fact]
    public void TheActorTotalCountsTheDiceThatLostAsWellAsTheOnesThatWon()
    {
        // The fire engine divides this total to work out how many hits landed, and it counts the
        // losing dice on purpose: a volley that mostly missed still put rounds downrange.
        var resolved = OpposedRolls.Resolve([9, 1, 2], 5);

        Assert.Equal(1, resolved.Beats);
        Assert.Equal(12, resolved.ActorTotal);
    }

    [Fact]
    public void AnEmptyHandOfDiceFailsRatherThanThrowing()
    {
        var resolved = OpposedRolls.Resolve([], 3);

        Assert.Equal(0, resolved.Beats);
        Assert.Equal(OpposedResult.Failed, resolved.Result);
        Assert.Equal(0, resolved.ActorTotal);
    }

    [Fact]
    public void ANullHandOfDiceIsRefused() =>
        Assert.Throws<ArgumentNullException>(() => OpposedRolls.Resolve(null!, 3));

    [Fact]
    public void ADiceSourceOnlyEverReturnsResultsThatAreOnTheDie()
    {
        var roller = new QualityDiceRoller();

        foreach (var die in QualityDice.Ladder)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var roll = roller.Roll(die);
                Assert.InRange(roll, 1, QualityDice.Faces(die));
            }
        }
    }

    [Fact]
    public void ADiceSourceThatMisbehavesIsCorrectedRatherThanTrusted()
    {
        // A source handing back something off the face is a bug in the source; the rules above
        // should not have to reason about it.
        var tooHigh = new QualityDiceRoller(_ => 99);
        var tooLow = new QualityDiceRoller(_ => -5);

        Assert.Equal(8, tooHigh.Roll(QualityDie.D8));
        Assert.Equal(1, tooLow.Roll(QualityDie.D8));
    }

    [Fact]
    public void AScriptedSourceRollsAHandInTheOrderItWasGiven()
    {
        var scripted = new Queue<int>([3, 7, 2]);
        var roller = new QualityDiceRoller(_ => scripted.Dequeue());

        var hand = roller.RollAll([QualityDie.D8, QualityDie.D10, QualityDie.D6]);

        Assert.Equal([3, 7, 2], hand);
    }
}
