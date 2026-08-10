namespace ForceSignal.Modules.StarGrunt.Game;

/// <summary>
/// Chooses which figure in a squad a hit lands on.
/// </summary>
/// <remarks>
/// <para>
/// The rules allocate wounds and kills <em>randomly</em> across the squad's figures, and that
/// randomness is load-bearing rather than decorative: whether two wounds land on the same trooper
/// decides whether anybody dies. Pairing them off deterministically, which is what this code used
/// to do, kills about eight times more men than the rules do on a full squad.
/// </para>
/// <para>
/// Separate from the die source because it is not a die roll. At a table you would grab whatever
/// die fits the number of figures still standing; the engine only needs an even choice among them,
/// and a test needs to be able to say exactly where each hit went.
/// </para>
/// </remarks>
public interface IFigureAllocator
{
    /// <summary>Picks one figure, as a zero-based index below the count given.</summary>
    /// <param name="figureCount">How many figures are there to hit.</param>
    /// <returns>The figure's index, from zero up to one less than the count.</returns>
    int Pick(int figureCount);
}

/// <summary>The ordinary allocator, backed by the shared pseudo-random generator.</summary>
/// <param name="next">
/// Optional replacement for the underlying draw, taking a count and returning an index below it.
/// Supplied by tests; production leaves it null.
/// </param>
public sealed class FigureAllocator(Func<int, int>? next = null) : IFigureAllocator
{
    private readonly Func<int, int> _next = next ?? Random.Shared.Next;

    /// <inheritdoc />
    public int Pick(int figureCount) =>
        figureCount <= 0 ? 0 : Math.Clamp(_next(figureCount), 0, figureCount - 1);
}
