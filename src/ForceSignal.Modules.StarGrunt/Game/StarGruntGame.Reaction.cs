using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Morale;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Game;

public sealed partial record StarGruntGame
{
    /// <summary>
    /// Declares that this unit's next move would take it out of cover or on to a located enemy.
    /// </summary>
    /// <param name="unit">The unit about to be ordered.</param>
    /// <param name="leavesCover">True when the move is the risky sort.</param>
    /// <returns>The game with the declaration recorded.</returns>
    /// <remarks>
    /// A declaration rather than a calculation, which is what the state it sets has always said in
    /// its own documentation: whether a given move counts as leaving cover is an eyeball judgement
    /// at a real table. Clearing it also clears any nerve already found, so a unit cannot bank a
    /// passed test against a different order later.
    /// </remarks>
    public GameOutcome<StarGruntGame> SetNextMoveLeavesCover(UnitId unit, bool leavesCover)
    {
        if (!HasUnit(unit))
        {
            return GameOutcome.Refused<StarGruntGame>($"There is no unit called '{unit}' on the table.");
        }

        return GameOutcome.Allowed(WithStatus(unit, status => status with
        {
            NextMoveLeavesCover = leavesCover,
            ReactionTestCleared = leavesCover && status.ReactionTestCleared,
        }));
    }

    /// <summary>
    /// Rolls to see whether troops have the nerve to carry out a risky order.
    /// </summary>
    /// <param name="unit">The unit being asked.</param>
    /// <param name="threatLevel">
    /// How much they are being asked to swallow, read off the player's own table. Mission motivation
    /// does not scale a reaction test, which is the player's business rather than this engine's.
    /// </param>
    /// <param name="dice">Where the die result comes from.</param>
    /// <returns>The game with the test resolved, or why it could not be taken.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// The same roll as a confidence test with one difference that decides everything: failing costs
    /// the <em>action</em> and never a confidence level. The troops simply decline, and may be asked
    /// again next turn.
    /// </para>
    /// <para>
    /// Passing spends nothing by itself - the action is spent by the move that follows. Failing
    /// spends one, and bars a second attempt at the same order in this activation, so the unit has
    /// to do something else with what it has left.
    /// </para>
    /// </remarks>
    public GameOutcome<StarGruntGame> TakeReactionTest(UnitId unit, int threatLevel, IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (!HasUnit(unit))
        {
            return GameOutcome.Refused<StarGruntGame>($"There is no unit called '{unit}' on the table.");
        }

        if (threatLevel < 0)
        {
            return GameOutcome.Refused<StarGruntGame>("A threat level cannot be less than nothing.");
        }

        var definition = Unit(unit);
        var status = Status(unit);

        if (!status.NextMoveLeavesCover)
        {
            return GameOutcome.Refused<StarGruntGame>(
                $"{definition.Name} has not been ordered to do anything it needs to steel itself for.");
        }

        if (Session.CurrentFrame?.Steps.Any(step => StarGruntSteps.ActionOf(step) == StarGruntAction.RefusedOrder) == true)
        {
            return GameOutcome.Refused<StarGruntGame>(
                $"{definition.Name} has already refused that order: it may be asked again next turn.");
        }

        // Before the roll, and so before the refusal below can spend the action on the unit's
        // behalf: a gap in what the players typed is not the troops declining an order.
        if (LeadershipBlocker(definition) is { } missing)
        {
            return GameOutcome.Refused<StarGruntGame>(missing);
        }

        var test = Confidence.React(definition.QualityDie, definition.LeadershipValue!.Value, threatLevel, dice);

        if (test.Passed)
        {
            return GameOutcome.Allowed(
                WithStatus(unit, current => current with { ReactionTestCleared = true })
                    .WithLog(
                        $"{definition.Name} steeled its nerve: rolled {test.Roll} against {test.ScoreToBeat} "
                        + "and will go."));
        }

        // The refusal is what the action was spent on, so it goes into the frame as a step.
        var spent = TakeStep(StarGruntSteps.Simple(StarGruntAction.RefusedOrder));
        if (!spent.IsAllowed)
        {
            return spent;
        }

        return GameOutcome.Allowed(
            spent.Value!.WithLog(
                $"{definition.Name} lost its nerve: rolled {test.Roll} against {test.ScoreToBeat} "
                + "and stayed put, losing the action."));
    }
}
