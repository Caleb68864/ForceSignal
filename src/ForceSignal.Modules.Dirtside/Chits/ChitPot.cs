namespace ForceSignal.Modules.Dirtside.Chits;

/// <summary>
/// Where a damage resolution gets its chits.
/// </summary>
/// <remarks>
/// Injectable for the same reason the die source is: a test wants to script a draw and a replay
/// wants to reproduce a game exactly.
/// </remarks>
public interface IChitPot
{
    /// <summary>
    /// Draws a handful of chits for one damage resolution, and restores the pot afterwards.
    /// </summary>
    /// <param name="count">How many chits to draw.</param>
    /// <returns>The chits, in the order they came out.</returns>
    IReadOnlyList<DamageChit> Draw(int count);
}

/// <summary>
/// The opaque pot the chits are pulled from, blind.
/// </summary>
/// <remarks>
/// <para>
/// The whole subtlety of this class is in one sentence: a draw is without replacement *within one
/// resolution*, and the pot is whole again before the next one. A twin mount that scores two hits
/// makes two draws of its own size with a restore in between, not one draw of twice the size, and
/// the two are genuinely different distributions - the second is far more likely to sweep up a
/// scarce special. Players notice.
/// </para>
/// <para>
/// So restoring is not an operation a caller can perform, or forget to perform: every call to
/// <see cref="Draw"/> shuffles a fresh copy of the whole composition. The only way to draw without
/// replacement here is within a single call, which is exactly the scope the rule has.
/// </para>
/// </remarks>
/// <param name="composition">What is in the pot. Defaults to the documented default composition.</param>
/// <param name="nextIndex">
/// Optional replacement for the shuffle's randomness, taking an exclusive upper bound and returning
/// a value from zero up to it. Supplied by tests and replays; production leaves it null.
/// </param>
public sealed class ChitPot(ChitPotComposition? composition = null, Func<int, int>? nextIndex = null) : IChitPot
{
    private readonly ChitPotComposition _composition = composition ?? ChitPotComposition.Default;
    private readonly Func<int, int> _nextIndex = nextIndex ?? Random.Shared.Next;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// The count is negative, or larger than the pot holds - a hand bigger than the pot is a caller
    /// bug, not something to silently satisfy by drawing a chit twice.
    /// </exception>
    public IReadOnlyList<DamageChit> Draw(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _composition.Count);

        // A fresh copy every time: this is the restore, made structural rather than remembered.
        var working = _composition.Chits.ToArray();
        var drawn = new DamageChit[count];

        // Partial Fisher-Yates. Only the first `count` positions get settled, so a five-chit draw
        // from a hundred-odd chit pot costs five swaps rather than a full shuffle.
        for (var i = 0; i < count; i++)
        {
            var j = i + Bounded(_nextIndex(working.Length - i), working.Length - i);
            (working[i], working[j]) = (working[j], working[i]);
            drawn[i] = working[i];
        }

        return drawn;
    }

    /// <summary>
    /// A source handing back an index off the end is a bug in the source, not something the rules
    /// above should have to reason about, so it is corrected here - the same bargain the die source
    /// strikes.
    /// </summary>
    private static int Bounded(int value, int exclusiveUpper) =>
        Math.Clamp(value, 0, Math.Max(0, exclusiveUpper - 1));
}
