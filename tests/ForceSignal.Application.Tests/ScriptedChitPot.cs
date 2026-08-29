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

    /// <inheritdoc />
    public IReadOnlyList<DamageChit> Draw(int count) =>
        _chits.Length == 0
            ? []
            : [.. Enumerable.Range(0, Math.Max(0, count)).Select(_ => _chits[_index++ % _chits.Length])];
}
