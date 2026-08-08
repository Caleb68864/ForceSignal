using System.Collections.ObjectModel;
using ForceSignal.Modules.Dirtside.Chits;

namespace ForceSignal.Modules.Dirtside.Combat;

/// <summary>One handful of chits, scored but not yet interpreted.</summary>
/// <param name="Draw">Every chit that left the pot, in order, with what each was worth.</param>
/// <param name="ValidTotal">The total of the counting chits, after the value scale.</param>
public readonly record struct ChitTally(IReadOnlyList<DrawnChit> Draw, int ValidTotal)
{
    /// <summary>Whether a given special was drawn <em>and</em> counted.</summary>
    /// <param name="special">The special to look for.</param>
    /// <returns>True when it is in the draw and this target is one that specials apply to.</returns>
    public bool Counted(ChitSpecial special) =>
        Draw.Any(d => d.Counts && d.Chit.Special == special);

    /// <summary>How many chits were drawn and did nothing. The mechanism, in one number.</summary>
    public int WastedSlots => Draw.Count(d => d.WastedTheSlot);
}

/// <summary>
/// Taking a handful of chits out of the pot and scoring it.
/// </summary>
/// <remarks>
/// Extracted because three different rules now draw chits and compare a total to a number - vehicle
/// damage against armour, infantry casualties against a kill total, and the close-assault exchange -
/// and they differ only in what they compare against. The <em>drawing</em> must not differ at all.
/// In particular, "an invalid chit consumes its slot and is never replaced" has to be one piece of
/// code rather than three, because it is the property that would drift silently and take every
/// probability in the game with it.
/// </remarks>
public static class ChitDraw
{
    /// <summary>Draws and scores one handful.</summary>
    /// <param name="chitCount">How many chits to draw.</param>
    /// <param name="validity">What the drawn chits are allowed to count.</param>
    /// <param name="pot">The pot to draw from. It is whole again when this returns.</param>
    /// <returns>Every chit drawn, and the total of the ones that counted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="validity"/> or <paramref name="pot"/> is null.</exception>
    public static ChitTally From(int chitCount, ChitValidity validity, IChitPot pot)
    {
        ArgumentNullException.ThrowIfNull(validity);
        ArgumentNullException.ThrowIfNull(pot);

        var drawn = pot.Draw(chitCount);
        var resolved = new List<DrawnChit>(drawn.Count);
        var total = 0;

        foreach (var chit in drawn)
        {
            // Every chit is recorded, counting or not. The wasted ones are the mechanism, not noise.
            var counts = validity.Counts(chit);
            var value = validity.ValueOf(chit);
            total += value;
            resolved.Add(new DrawnChit(chit, counts, value));
        }

        return new ChitTally(new ReadOnlyCollection<DrawnChit>(resolved), total);
    }

    /// <summary>An empty draw, for the cases that short-circuit before touching the pot.</summary>
    public static ChitTally Nothing { get; } = new(Array.Empty<DrawnChit>(), 0);
}
