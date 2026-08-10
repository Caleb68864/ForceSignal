using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A quality-die source a test can script, for the ground-combat games.
/// </summary>
/// <remarks>
/// Separate from <see cref="ScriptedDice"/>, which feeds Full Thrust's six-siders through a
/// <c>Func&lt;int&gt;</c>. The ground games roll dice of different sizes through
/// <see cref="IQualityDiceRoller"/>, so they need a source that is handed the die it is rolling.
/// </remarks>
/// <param name="rolls">The results to hand back, in order.</param>
internal sealed class ScriptedQualityDice(params int[] rolls) : IQualityDiceRoller
{
    private readonly Queue<int> _rolls = new(rolls);

    /// <summary>
    /// Face returned once the script runs out. Low on purpose: an unscripted roll should fail to do
    /// anything rather than quietly succeed and leave a test passing for a reason nobody wrote down.
    /// </summary>
    public int Fallback { get; init; } = 1;

    /// <inheritdoc />
    public int Roll(QualityDie die) => _rolls.Count > 0 ? _rolls.Dequeue() : Fallback;
}
