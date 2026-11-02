using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Game;

/// <summary>
/// The nerve commands: a confidence test, a reaction test and the declarations they read.
/// </summary>
/// <remarks>
/// The arithmetic is <see cref="ConfidenceLadder"/>'s and the effects table is
/// <see cref="DirtsideConfidence"/>'s. What this owns is calling them and writing down the answer.
/// When a test is owed stays the player's call, and so does its threat level, which comes off their
/// own table - the same shape StarGrunt's nerve commands have.
/// </remarks>
public sealed partial record DirtsideGame
{
    /// <summary>Takes a confidence test, the moment whatever caused it happens.</summary>
    /// <param name="unit">The platoon it happened to.</param>
    /// <param name="threatLevel">How serious it was, off the player's own table.</param>
    /// <param name="dice">Where the die comes from.</param>
    /// <returns>The game with the marker moved or not, or why the test could not be taken.</returns>
    /// <remarks>
    /// Not an action and not tied to an activation, so it spends nothing and opens nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    public GameOutcome<DirtsideGame> TakeConfidenceTest(UnitId unit, int threatLevel, IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (NerveBlocker(unit, threatLevel) is { } reason)
        {
            return GameOutcome.Refused<DirtsideGame>(reason);
        }

        var platoon = Unit(unit);
        var test = ConfidenceLadder.Test(
            Status(unit).Confidence, platoon.Quality!.Value, platoon.LeadershipValue!.Value, threatLevel, dice);

        var outcome = test.LevelsLost == 0
            ? $"held at {test.After}"
            : $"lost {test.LevelsLost} level(s) to {test.After}";

        return GameOutcome.Allowed(
            WithStatus(unit, status => status with { Confidence = test.After })
                .WithLog(
                    $"{platoon.Name} tested its nerve: rolled {test.Roll} against {test.ScoreToBeat} "
                    + $"(leadership {platoon.LeadershipValue} plus threat {threatLevel}) and {outcome}."));
    }

    /// <summary>Takes the reaction test a move is waiting on.</summary>
    /// <param name="unit">The platoon, which has to be the one activating.</param>
    /// <param name="threatLevel">
    /// How much it is being asked to swallow, off the player's own table. A platoon that owes the test
    /// twice over still rolls once, at the higher of the two; threats do not add.
    /// </param>
    /// <param name="dice">Where the die comes from.</param>
    /// <returns>The game with the test resolved, or why it could not be taken.</returns>
    /// <remarks>
    /// Failing costs the move and never a level, and the platoon's elements are left to do something
    /// else. It bars a second attempt for the rest of the activation, as a refused order does in
    /// StarGrunt, so a table cannot roll until the die agrees.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    public GameOutcome<DirtsideGame> TakeReactionTest(UnitId unit, int threatLevel, IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (WhyReactionTestIsRefused(unit, threatLevel) is { } reason)
        {
            return GameOutcome.Refused<DirtsideGame>(reason);
        }

        var platoon = Unit(unit);
        var test = ConfidenceLadder.React(platoon.Quality!.Value, platoon.LeadershipValue!.Value, threatLevel, dice);

        return GameOutcome.Allowed(
            WithStatus(unit, status => status with
            {
                ReactionTestCleared = test.Passed,
                ReactionTestFailed = !test.Passed,
            }).WithLog(
                $"{platoon.Name} was ordered to move: rolled {test.Roll} against {test.ScoreToBeat} and "
                + (test.Passed ? "will go." : "will not. It stays put, and may do something else with this activation.")));
    }

    /// <summary>Whether a platoon could take a reaction test now, and why not.</summary>
    /// <param name="unit">The platoon.</param>
    /// <param name="threatLevel">The threat level it would be taken at.</param>
    /// <returns>The refusal in words, or null when the test may be taken.</returns>
    public string? WhyReactionTestIsRefused(UnitId unit, int threatLevel = 0)
    {
        if (NerveBlocker(unit, threatLevel) is { } reason)
        {
            return reason;
        }

        var platoon = Unit(unit);
        if (Session.CurrentFrame is not { Kind: FrameKind.Activation } frame || frame.Unit != unit)
        {
            return $"{platoon.Name} is not activating, and a reaction test is taken for a move it is about to make.";
        }

        var status = Status(unit);
        if (status.ReactionTestFailed)
        {
            return $"{platoon.Name} has already refused that order this activation.";
        }

        if (status.ReactionTestCleared)
        {
            return $"{platoon.Name} has already found the nerve for this move.";
        }

        var owesForFire = UnderFire.OwesReactionTestToMove(status.IsUnderFire);
        var owesForAdvance = status.NextMoveAdvancesOnTheEnemy
            && DirtsideConfidence.NeedsReactionTestToAdvance(status.Confidence, platoon.Kind);

        return owesForFire || owesForAdvance
            ? null
            : $"{platoon.Name} has not been ordered to do anything it needs to steel itself for.";
    }

    /// <summary>Declares whether the platoon's next move takes it toward the enemy or out of cover.</summary>
    /// <param name="unit">The platoon.</param>
    /// <param name="advances">True when it does.</param>
    /// <returns>The game with the declaration recorded.</returns>
    /// <remarks>
    /// A declaration because it is an eyeball judgement at a real table. Changing it gives up any
    /// nerve already found, so a passed test cannot be banked against a different order.
    /// </remarks>
    public GameOutcome<DirtsideGame> SetNextMoveAdvances(UnitId unit, bool advances)
    {
        if (!HasUnit(unit))
        {
            return GameOutcome.Refused<DirtsideGame>($"There is no platoon called '{unit}' on the table.");
        }

        return GameOutcome.Allowed(WithStatus(unit, status => status with
        {
            NextMoveAdvancesOnTheEnemy = advances,
            ReactionTestCleared = advances && status.ReactionTestCleared,
        }));
    }

    /// <summary>Declares whether a platoon has come out of integrity, which the table measures.</summary>
    /// <param name="unit">The platoon.</param>
    /// <param name="isDisorganised">True when it has.</param>
    /// <returns>The game with the declaration recorded.</returns>
    public GameOutcome<DirtsideGame> SetDisorganised(UnitId unit, bool isDisorganised)
    {
        if (!HasUnit(unit))
        {
            return GameOutcome.Refused<DirtsideGame>($"There is no platoon called '{unit}' on the table.");
        }

        return GameOutcome.Allowed(WithStatus(unit, status => status with { IsDisorganised = isDisorganised }));
    }

    /// <summary>What stops a platoon rolling for its nerve at all. Checked before any die moves.</summary>
    private string? NerveBlocker(UnitId unit, int threatLevel)
    {
        if (!HasUnit(unit))
        {
            return $"There is no platoon called '{unit}' on the table.";
        }

        if (threatLevel < 0)
        {
            return "A threat level cannot be less than nothing.";
        }

        var platoon = Unit(unit);
        return platoon.IsCybertank
            ? $"{platoon.Name} is a cybertank and carries no confidence marker, so it takes no test."
            : ProfileBlocker(platoon);
    }
}
