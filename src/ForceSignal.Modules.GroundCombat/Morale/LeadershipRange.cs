using System.Globalization;

namespace ForceSignal.Modules.GroundCombat.Morale;

/// <summary>
/// Which numbers count as Leadership Values at this table, as the players entered them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Both engines used to disagree about what a Leadership Value was, and both
/// were wrong in their own direction: StarGrunt's service refused anything outside a bound written
/// into the source and said so by reciting the bound back at the player, and Dirtside's took whatever
/// arrived - a probe put 99 and -4 on a command marker and both were stored verbatim. The bound is a
/// reading off somebody's page, so it belongs to the players; and the two games have to agree about
/// it, because there is one <see cref="ConfidenceLadder"/> and both read a leadership number into it.
/// </para>
/// <para>
/// <b>Two numbers, and no third.</b> There is no entry here for which end is the good one. The
/// ordering is not a number: it falls out of the procedures the engines already own, in both games
/// and in the same direction. <see cref="ConfidenceLadder.ScoreToBeat"/> adds leadership to the
/// threat and the unit has to <em>exceed</em> the total, so a smaller leadership is a smaller score
/// and an easier test; Dirtside's fire-effectiveness check calls a roll <em>below</em> leadership
/// ineffective, so a smaller leadership is harder to fall under. Both already depend on it. Asking
/// the players to enter a direction as well would let a table's answer contradict arithmetic the
/// engine does not read it for, and this app has been bitten by a stored fact that could disagree
/// with the code that used it.
/// </para>
/// <para>
/// <b>Both ends or neither.</b> One bound on its own is not a range - it is half a table, which this
/// project has already settled is worse than an empty one - so a half-entered range is refused where
/// it is entered rather than half-honoured where it is read.
/// </para>
/// </remarks>
/// <param name="Lowest">The smallest number a card in this game may carry, or null when not entered.</param>
/// <param name="Highest">The largest, or null when not entered.</param>
public readonly record struct LeadershipRange(int? Lowest = null, int? Highest = null)
{
    /// <summary>The name this refusal calls the entry, so a player can go and find it.</summary>
    public const string EntryName = "the Leadership Values this game uses";

    /// <summary>A range nobody has entered. What a game with no rules profile plays on.</summary>
    public static LeadershipRange Unentered => default;

    /// <summary>True when both ends have been entered and the range can answer a question.</summary>
    public bool IsEntered => Lowest is not null && Highest is not null;

    /// <summary>True when nothing at all has been entered, as a blank profile reports.</summary>
    public bool IsBlank => Lowest is null && Highest is null;

    /// <summary>True when this number is one of the game's Leadership Values.</summary>
    /// <param name="value">The number on the card.</param>
    /// <returns>Whether the range holds it. Always false when the range is not entered.</returns>
    public bool Holds(int value) => IsEntered && value >= Lowest!.Value && value <= Highest!.Value;

    /// <summary>
    /// Why this number cannot go on a card, or null when it may.
    /// </summary>
    /// <param name="value">The number the player entered.</param>
    /// <param name="subject">What the number is being put on, for a refusal a player can act on.</param>
    /// <returns>The refusal in words, or null.</returns>
    /// <remarks>
    /// The unentered case names the entry and stops there; it must not describe the entry's contents,
    /// because this app does not know them and the message it replaced did - it recited a published
    /// page's bound back at the player every time somebody typed a number outside it. The entered
    /// case does quote the bounds, because by then they are the players' own numbers being read back.
    /// </remarks>
    public string? WhyRefused(int value, string subject)
    {
        if (Holds(value))
        {
            return null;
        }

        var number = value.ToString(CultureInfo.InvariantCulture);
        return IsEntered
            ? $"{subject} cannot have a Leadership Value of {number}: this game's Leadership Values run "
                + $"from {Lowest!.Value.ToString(CultureInfo.InvariantCulture)} to "
                + $"{Highest!.Value.ToString(CultureInfo.InvariantCulture)}, which is what was entered for it."
            : $"{subject} cannot have a Leadership Value of {number} until somebody has entered "
                + $"{EntryName}. This app ships no leadership ratings of its own, so enter the lowest and "
                + "the highest from your own rulebook on the game's rules profile.";
    }
}
