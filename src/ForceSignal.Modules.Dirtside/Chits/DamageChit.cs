namespace ForceSignal.Modules.Dirtside.Chits;

/// <summary>
/// The colour printed on a numerical damage chit.
/// </summary>
/// <remarks>
/// The colours do not differ in severity - each one carries the same spread of values, so each one
/// has the same mean. What differs is how many of each are in the pot, and which of them a given
/// weapon is allowed to count. That is the whole reason a colour exists.
/// </remarks>
public enum ChitColour
{
    /// <summary>The commonest colour in the pot, and the one most weapons can always count.</summary>
    Red = 0,

    /// <summary>A scarcer colour.</summary>
    Yellow = 1,

    /// <summary>A scarcer colour.</summary>
    Green = 2,
}

/// <summary>
/// A set of chit colours a weapon may count. A set rather than a single value because validity is
/// recorded per weapon and per range on the target's own card, and most entries name two colours.
/// </summary>
[Flags]
public enum ChitColours
{
    /// <summary>No colour counts. Not the same thing as the weapon being ineffective.</summary>
    None = 0,

    /// <summary>Red counts.</summary>
    Red = 1,

    /// <summary>Yellow counts.</summary>
    Yellow = 2,

    /// <summary>Green counts.</summary>
    Green = 4,

    /// <summary>Every colour counts.</summary>
    All = Red | Yellow | Green,
}

/// <summary>
/// The non-numerical chits. These are not colour-gated: a weapon's colour validity has nothing to
/// say about them, which is exactly why "no valid colours" and "ineffective" have to stay separate.
/// </summary>
public enum ChitSpecial
{
    /// <summary>Track blown, skirt holed or powerplant dead: it never moves again, but it may fire.</summary>
    Mobility = 0,

    /// <summary>The target's sensors and electronics go: it may move, but it may not act.</summary>
    SystemsDownTarget = 1,

    /// <summary>
    /// The firer's own systems go instead. The shot is treated as never fired, so this one rewrites
    /// the whole resolution rather than adding to it.
    /// </summary>
    SystemsDownFirer = 2,

    /// <summary>The round that finds the magazine. Total destruction, whatever the armour.</summary>
    Boom = 3,
}

/// <summary>
/// One chit out of the pot: either a colour with a number on it, or one of the specials.
/// </summary>
/// <remarks>
/// A single type rather than a hierarchy because the pot is one bag and a draw is one sequence -
/// splitting the two kinds apart would force the pot to interleave two collections and would make it
/// far too easy to draw "three numbers and any specials", which is not the mechanism.
/// </remarks>
public readonly record struct DamageChit
{
    private DamageChit(ChitColour? colour, int value, ChitSpecial? special)
    {
        Colour = colour;
        Value = value;
        Special = special;
    }

    /// <summary>The chit's colour, or null when it is a special.</summary>
    public ChitColour? Colour { get; }

    /// <summary>The number printed on the chit, or zero when it is a special.</summary>
    public int Value { get; }

    /// <summary>Which special this is, or null when it is a numerical chit.</summary>
    public ChitSpecial? Special { get; }

    /// <summary>True when this chit is one of the specials rather than a number.</summary>
    public bool IsSpecial => Special is not null;

    /// <summary>
    /// True when this is a numerical chit printed with a zero.
    /// </summary>
    /// <remarks>
    /// Worth being able to ask, because a zero is a perfectly valid draw that happens to add
    /// nothing, and confusing it with an invalid chit would blur the one mechanism that separates
    /// weapons from each other.
    /// </remarks>
    public bool IsZero => !IsSpecial && Value == 0;

    /// <summary>Makes a numerical chit.</summary>
    /// <param name="colour">The colour printed on it.</param>
    /// <param name="value">The number printed on it. Zero is a real chit, not an absence.</param>
    /// <returns>The chit.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public static DamageChit Numerical(ChitColour colour, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new DamageChit(colour, value, null);
    }

    /// <summary>Makes a special chit.</summary>
    /// <param name="special">Which special.</param>
    /// <returns>The chit.</returns>
    public static DamageChit Of(ChitSpecial special) => new(null, 0, special);

    /// <summary>Renders the chit the way it reads on the counter.</summary>
    /// <returns>Either the special's name or a colour and a number.</returns>
    public override string ToString() => Special is { } s ? s.ToString() : $"{Colour} {Value}";
}

/// <summary>Which colours a colour set contains, as a question about one colour.</summary>
public static class ChitColourSets
{
    /// <summary>Whether a set contains a given colour.</summary>
    /// <param name="colours">The set.</param>
    /// <param name="colour">The colour to look for.</param>
    /// <returns>True when the colour is in the set.</returns>
    public static bool Contains(this ChitColours colours, ChitColour colour) =>
        (colours & ToFlag(colour)) != ChitColours.None;

    /// <summary>The single-colour set for one colour.</summary>
    /// <param name="colour">The colour.</param>
    /// <returns>A set containing exactly that colour.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The colour is not one of the three.</exception>
    public static ChitColours ToFlag(ChitColour colour) => colour switch
    {
        ChitColour.Red => ChitColours.Red,
        ChitColour.Yellow => ChitColours.Yellow,
        ChitColour.Green => ChitColours.Green,
        _ => throw new ArgumentOutOfRangeException(nameof(colour)),
    };
}
