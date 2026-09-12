using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Morale;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Game;

public sealed partial record StarGruntGame
{
    /// <summary>
    /// Spends an action trying to get a pinned unit's head back up.
    /// </summary>
    /// <param name="unit">The unit trying.</param>
    /// <param name="dice">Where the die result comes from.</param>
    /// <returns>The game with the attempt resolved, or why it could not be made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// A command of its own rather than a step, because it rolls. The generic step route takes no
    /// die source on purpose - the same reason firing has its own command.
    /// </para>
    /// <para>
    /// The action is spent before the roll, and stays spent whether or not the marker comes off.
    /// Trying and failing is how a pinned unit loses its turn, and it is most of what suppression
    /// costs: a unit under sustained fire is out of the fight for several turns without ever taking
    /// a casualty.
    /// </para>
    /// </remarks>
    public GameOutcome<StarGruntGame> RemoveSuppression(UnitId unit, IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (!HasUnit(unit))
        {
            return GameOutcome.Refused<StarGruntGame>($"There is no unit called '{unit}' on the table.");
        }

        var definition = Unit(unit);
        var status = Status(unit);
        if (!Suppression.IsSuppressed(status.SuppressionMarkers))
        {
            return GameOutcome.Refused<StarGruntGame>($"{definition.Name} is not suppressed.");
        }

        // Before the action is spent, below. Trying and failing is how a pinned unit loses its turn,
        // so a refusal landing after the spend would be indistinguishable from a failed attempt.
        if (LeadershipBlocker(definition) is { } missing)
        {
            return GameOutcome.Refused<StarGruntGame>(missing);
        }

        // Spend the action first. A refusal here - not this unit's activation, no activation open,
        // both actions already spent - means nothing has been rolled and nothing has changed.
        var spent = TakeStep(StarGruntSteps.Simple(StarGruntAction.RemoveSuppression));
        if (!spent.IsAllowed)
        {
            return spent;
        }

        var relief = Suppression.TryClear(
            status.SuppressionMarkers,
            definition.QualityDie,
            definition.LeadershipValue!.Value,
            dice);

        var outcome = relief.Cleared
            ? $"shook one off ({relief.Remaining} left)"
            : "stayed pinned";

        return GameOutcome.Allowed(
            spent.Value!
                .WithStatus(unit, current => current with { SuppressionMarkers = relief.Remaining })
                .WithLog(
                    $"{definition.Name} tried to get its head up: rolled {relief.Roll} against "
                    + $"leadership {relief.ScoreToBeat} and {outcome}."));
    }
}
