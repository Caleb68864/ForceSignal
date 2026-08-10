using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// A die source that hands back a written-down sequence, so an outcome can be asserted rather than
/// hoped for. Matches the die source the Full Thrust tests inject for the same reason.
/// </summary>
/// <param name="rolls">The results to hand back, in order.</param>
internal sealed class ScriptedDice(params int[] rolls) : IQualityDiceRoller
{
    private readonly Queue<int> _rolls = new(rolls);

    /// <summary>How much of the script is left.</summary>
    public int Remaining => _rolls.Count;

    /// <inheritdoc />
    public int Roll(QualityDie die) => _rolls.Count > 0
        ? _rolls.Dequeue()
        : throw new InvalidOperationException($"The script ran out while rolling a {die}.");
}
