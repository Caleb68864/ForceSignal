namespace ForceSignal.Modules.GroundCombat.Dice;

/// <summary>
/// Where the engine gets its die results. Injectable so a test can script a sequence and a replay
/// can reproduce a game exactly, matching how the Full Thrust module takes its die source.
/// </summary>
public interface IQualityDiceRoller
{
    /// <summary>Rolls one die of the given type.</summary>
    /// <param name="die">The die to roll.</param>
    /// <returns>A result from 1 to the die's face count, inclusive.</returns>
    int Roll(QualityDie die);
}

/// <summary>
/// The ordinary die source, backed by the shared pseudo-random generator.
/// </summary>
/// <param name="next">
/// Optional replacement for the underlying draw, taking a face count and returning a result from
/// 1 to that count. Supplied by tests; production leaves it null.
/// </param>
public sealed class QualityDiceRoller(Func<int, int>? next = null) : IQualityDiceRoller
{
    private readonly Func<int, int> _next = next ?? (faces => Random.Shared.Next(1, faces + 1));

    /// <inheritdoc />
    public int Roll(QualityDie die)
    {
        var faces = QualityDice.Faces(die);

        // A die source that hands back something off the face is a bug in the source, not
        // something the rules above should have to reason about, so it is corrected here.
        return Math.Clamp(_next(faces), 1, faces);
    }

    /// <summary>Rolls a set of dice, keeping them in the order given.</summary>
    /// <param name="dice">The dice to roll.</param>
    /// <returns>One result per die.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    public IReadOnlyList<int> RollAll(IEnumerable<QualityDie> dice)
    {
        ArgumentNullException.ThrowIfNull(dice);
        return [.. dice.Select(Roll)];
    }
}
