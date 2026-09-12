using System.Globalization;
using ForceSignal.Modules.GroundCombat.Morale;

namespace ForceSignal.Application.Ground;

/// <summary>
/// Reads the set of Leadership Values a game plays with off the wire, for both ground games.
/// </summary>
/// <remarks>
/// One copy, linked into both mappings, because the two engines are supposed to behave alike about
/// this and used not to: StarGrunt's service carried the bound in its source and recited it back at
/// the player, and Dirtside's carried none at all. Two mappings that each grew their own would be the
/// same divergence again, one layer up.
/// </remarks>
internal static class LeadershipRangeMapping
{
    /// <summary>
    /// Reads the two ends off a profile body.
    /// </summary>
    /// <param name="lowest">The smallest Leadership Value entered, or null.</param>
    /// <param name="highest">The largest, or null.</param>
    /// <returns>The range, unentered when neither end was given.</returns>
    /// <exception cref="InvalidOperationException">
    /// Only one end was entered, or the two ends are the wrong way round.
    /// </exception>
    public static LeadershipRange FromDto(int? lowest, int? highest)
    {
        if (lowest is null && highest is null)
        {
            return LeadershipRange.Unentered;
        }

        // Half a range is worse than none: it would leave every number on one side of the entered
        // bound accepted on no authority at all. Named where it is entered, so the player fixes the
        // profile rather than meeting this halfway through a firefight.
        if (lowest is null || highest is null)
        {
            var missing = lowest is null ? "lowest" : "highest";
            throw new InvalidOperationException(
                $"A game's Leadership Values need both ends, and the {missing} has not been entered. "
                + "Enter both from your own rulebook, or neither.");
        }

        if (lowest > highest)
        {
            throw new InvalidOperationException(
                $"This game's lowest Leadership Value ({lowest.Value.ToString(CultureInfo.InvariantCulture)}) "
                + $"is above its highest ({highest.Value.ToString(CultureInfo.InvariantCulture)}).");
        }

        return new LeadershipRange(lowest, highest);
    }
}
