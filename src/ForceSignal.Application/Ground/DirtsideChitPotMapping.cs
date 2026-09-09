using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.Dirtside.Chits;

namespace ForceSignal.Application.Ground;

/// <summary>
/// Reads a chit pot off the wire, and reports one back.
/// </summary>
/// <remarks>
/// <para>
/// The pot is the player's, and this is the only door it comes through. Every number here was
/// counted off a counter sheet the server has never seen; nothing in this assembly supplies one,
/// with the single exception of <see cref="ChitPotComposition.Default"/>, which is kept for one
/// release, is labelled a guess everywhere it can be, and is on its way out.
/// </para>
/// <para>
/// Counts rather than a list of chits, because that is how a sheet is counted. The module wants the
/// flat multiset - a draw is a sequence of individual counters - so the expansion happens here,
/// behind a ceiling: the count arrives off an unauthenticated create route and becomes the length of
/// an array, so an uncapped one is a machine at a table running out of memory because a client sent
/// a number with too many zeros on it. The same reasoning as every other ceiling in
/// <see cref="GroundGameGuards"/>.
/// </para>
/// </remarks>
internal static class DirtsideChitPotMapping
{
    /// <summary>
    /// Chits one pot may hold. Far above any real sheet - the published one is a bit over a hundred -
    /// and here only so that the array this builds cannot be sized by a stranger.
    /// </summary>
    public const int MaxChitsInPot = 5000;

    /// <summary>
    /// The largest number a numerical chit may carry. Not a rule: it is a ceiling on a value that is
    /// summed and compared against armour, and a sheet with a chit past this on it is a typo.
    /// </summary>
    public const int MaxChitValue = 99;

    /// <summary>Reads a pot off a create request, or falls back to the built-in default.</summary>
    /// <param name="dto">The counts the player sent, or null when they sent none.</param>
    /// <returns>The composition, and whether it is the built-in guess rather than theirs.</returns>
    /// <exception cref="InvalidOperationException">The counts do not describe a pot that can be drawn from.</exception>
    public static (ChitPotComposition Composition, bool IsBuiltInDefault) FromRequest(DirtsideChitPotDto? dto)
    {
        if (dto is null)
        {
            return (ChitPotComposition.Default, true);
        }

        return (FromDto(dto), false);
    }

    /// <summary>Reads a pot off the wire, or off a stored row, which is the same shape.</summary>
    /// <param name="dto">The counts.</param>
    /// <returns>The composition.</returns>
    /// <exception cref="InvalidOperationException">The counts do not describe a pot that can be drawn from.</exception>
    public static ChitPotComposition FromDto(DirtsideChitPotDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var counts = new Dictionary<DamageChit, int>();
        var total = 0;

        foreach (var entry in dto.Numericals ?? [])
        {
            if (entry is null)
            {
                throw new InvalidOperationException("A chit pot cannot hold a row that says nothing.");
            }

            var colour = Enum.TryParse<ChitColour>(entry.Colour, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"'{entry.Colour}' is not a chit colour ({string.Join(", ", DirtsideWire.ChitColours)}).");

            if (entry.Value < 0 || entry.Value > MaxChitValue)
            {
                throw new InvalidOperationException(
                    $"A chit numbered {entry.Value} is not one this server will hold; the numbers run 0 to {MaxChitValue}.");
            }

            Add(counts, DamageChit.Numerical(colour, entry.Value), entry.Count, ref total);
        }

        foreach (var entry in dto.Specials ?? [])
        {
            if (entry is null)
            {
                throw new InvalidOperationException("A chit pot cannot hold a row that says nothing.");
            }

            var special = Enum.TryParse<ChitSpecial>(entry.Special, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"'{entry.Special}' is not a special chit ({string.Join(", ", DirtsideWire.ChitSpecials)}).");

            Add(counts, DamageChit.Of(special), entry.Count, ref total);
        }

        if (total == 0)
        {
            throw new InvalidOperationException("A pot with nothing in it cannot resolve damage; count your chits onto it.");
        }

        return ChitPotComposition.FromCounts(counts);
    }

    /// <summary>Reports a pot back the way it was counted.</summary>
    /// <param name="composition">The pot.</param>
    /// <param name="isBuiltInDefault">Whether these counts are the built-in guess rather than the player's.</param>
    /// <returns>The counts, numericals ordered by colour then number and specials in the wire's order.</returns>
    public static DirtsideChitPotDto ToDto(ChitPotComposition composition, bool isBuiltInDefault)
    {
        ArgumentNullException.ThrowIfNull(composition);

        var numericals = composition.Chits
            .Where(chit => !chit.IsSpecial)
            .GroupBy(chit => (Colour: chit.Colour!.Value, chit.Value))
            .OrderBy(group => group.Key.Colour)
            .ThenBy(group => group.Key.Value)
            .Select(group => new DirtsideNumericalChitsDto(group.Key.Colour.ToString(), group.Key.Value, group.Count()))
            .ToArray();

        var specials = Enum.GetValues<ChitSpecial>()
            .Select(special => new DirtsideSpecialChitsDto(special.ToString(), composition.CountOf(DamageChit.Of(special))))
            .Where(entry => entry.Count > 0)
            .ToArray();

        return new DirtsideChitPotDto(numericals, specials, isBuiltInDefault);
    }

    /// <summary>
    /// Folds one row into the tally. Rows are added rather than replaced, so a sheet counted in two
    /// passes - "Red 0: twelve" and later "Red 0: one more" - totals rather than losing the first.
    /// </summary>
    private static void Add(Dictionary<DamageChit, int> counts, DamageChit chit, int count, ref int total)
    {
        if (count < 0)
        {
            throw new InvalidOperationException($"A pot cannot hold {count} of {chit}.");
        }

        total += count;
        if (total > MaxChitsInPot)
        {
            throw new InvalidOperationException(
                $"That pot holds more than {MaxChitsInPot} chits, which is more than this server will shuffle.");
        }

        counts[chit] = counts.GetValueOrDefault(chit) + count;
    }
}
