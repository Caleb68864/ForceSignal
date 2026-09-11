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
    private readonly List<QualityDie> _rolled = [];

    /// <summary>How much of the script is left.</summary>
    public int Remaining => _rolls.Count;

    /// <summary>
    /// Which die each roll was asked of, in order.
    /// </summary>
    /// <remarks>
    /// A scripted 12 is a 12 whatever die it was handed out for, so asserting on the rolls alone cannot
    /// show which die a shift produced. This can - which matters now that the shifts are the players'
    /// numbers and the thing under test is that they were read.
    /// </remarks>
    public IReadOnlyList<QualityDie> Rolled => _rolled;

    /// <inheritdoc />
    public int Roll(QualityDie die)
    {
        _rolled.Add(die);
        return _rolls.Count > 0
            ? _rolls.Dequeue()
            : throw new InvalidOperationException($"The script ran out while rolling a {die}.");
    }
}
