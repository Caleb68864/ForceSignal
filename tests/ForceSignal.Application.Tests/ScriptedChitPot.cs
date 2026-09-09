using ForceSignal.Modules.Dirtside.Chits;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A chit pot that hands out exactly the chits a test asks for, in order, cycling when it runs out.
/// </summary>
/// <remarks>
/// The same double the Dirtside module tests keep privately. Shared here rather than borrowed,
/// because this project has no business reaching into another test assembly for a fixture.
/// </remarks>
/// <param name="chits">The chits, in the order they should come out.</param>
internal sealed class ScriptedChitPot(params DamageChit[] chits) : IChitPot
{
    private readonly DamageChit[] _chits = chits;
    private int _index;

    /// <summary>
    /// The service's pot factory, handing every game the same scripted pot whatever it counted.
    /// </summary>
    /// <remarks>
    /// The service builds a pot per game from that game's composition, because the composition is the
    /// players'. A test scripting a draw does not care what the composition says - it cares which
    /// chit comes out - so it ignores the argument. A test that does care about the composition
    /// reaching the pot passes a factory that reads it.
    /// </remarks>
    /// <param name="chits">The chits, in the order they should come out.</param>
    /// <returns>A factory the service will call once per game.</returns>
    public static Func<ChitPotComposition, IChitPot> Handing(params DamageChit[] chits) =>
        _ => new ScriptedChitPot(chits);

    /// <inheritdoc />
    public IReadOnlyList<DamageChit> Draw(int count) =>
        _chits.Length == 0
            ? []
            : [.. Enumerable.Range(0, Math.Max(0, count)).Select(_ => _chits[_index++ % _chits.Length])];
}
