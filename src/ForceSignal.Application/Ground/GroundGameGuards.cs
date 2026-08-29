using System.Security.Cryptography;
using System.Text;

namespace ForceSignal.Application.Ground;

/// <summary>
/// The ceilings and the checks the two ground-combat services share.
/// </summary>
/// <remarks>
/// <para>
/// None of these is a rules limit; every one is far above any game anyone will set up. They exist
/// because a request needs no credentials to create a game and only a game token to fill one, and
/// a machine at a table cannot be allowed to run out of memory because a client sent a number with
/// too many zeros on it. The Full Thrust service carries the same discipline privately; this is the
/// same idea, shared, because two engines that each needed it were about to write it twice.
/// </para>
/// <para>
/// Strings are truncated rather than refused, as they are for Full Thrust: a name pasted from a
/// force file with a tail on it should lose the tail, not the import.
/// </para>
/// </remarks>
internal static class GroundGameGuards
{
    /// <summary>Games a service holds at once. Reached only if that many are opened inside the retention window.</summary>
    public const int MaxConcurrentGames = 200;

    /// <summary>Units - squads, platoons - one game may hold.</summary>
    public const int MaxUnitsPerGame = 200;

    /// <summary>Figures or elements one unit may hold.</summary>
    public const int MaxMembersPerUnit = 50;

    /// <summary>Weapons one unit, or one element, may carry.</summary>
    public const int MaxWeaponsPerUnit = 40;

    /// <summary>Melee pairings one round may name. A pairing is two figures, and a unit holds fifty.</summary>
    public const int MaxMeleePairings = 100;

    /// <summary>
    /// Log entries a game keeps. The whole log ships inside every snapshot and the whole game is
    /// rewritten to disk on every command, so an uncapped log makes each activation of a long
    /// game slower than the last. The oldest lines go first.
    /// </summary>
    public const int MaxLogEntries = 2000;

    /// <summary>Longest caller-supplied string kept. Longer text is truncated, not refused.</summary>
    public const int MaxDisplayTextLength = 120;

    /// <summary>
    /// How long a game nobody has touched is kept. Long enough that no real table hits it; short
    /// enough that a server left running does not hold every game anyone ever opened on it.
    /// </summary>
    public static readonly TimeSpan IdleGameRetention = TimeSpan.FromHours(24);

    /// <summary>Refuses one more of something when a game is already holding its ceiling.</summary>
    /// <param name="current">How many there are.</param>
    /// <param name="max">How many there may be.</param>
    /// <param name="what">What they are, plural, for the message.</param>
    public static void RequireRoom(int current, int max, string what)
    {
        if (current >= max)
        {
            throw new InvalidOperationException($"This game already holds {max} {what}, which is as many as it tracks.");
        }
    }

    /// <summary>Refuses a request that carries more of something than one unit may have.</summary>
    /// <param name="count">How many came in.</param>
    /// <param name="max">How many there may be.</param>
    /// <param name="what">What they are, plural, for the message.</param>
    public static void RequireAtMost(int count, int max, string what)
    {
        if (count > max)
        {
            throw new InvalidOperationException($"That names {count} {what}, past the {max} a unit can carry.");
        }
    }

    /// <summary>Trims a caller-supplied string and cuts it to the display ceiling.</summary>
    /// <param name="value">The string as it arrived.</param>
    /// <returns>The string as it is kept.</returns>
    public static string Truncate(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= MaxDisplayTextLength ? trimmed : trimmed[..MaxDisplayTextLength];
    }

    /// <summary>Trims an optional string, leaving null alone.</summary>
    /// <param name="value">The string as it arrived, if it did.</param>
    /// <returns>The string as it is kept, or null.</returns>
    public static string? TruncateOptional(string? value) => value is null ? null : Truncate(value);

    /// <summary>
    /// Mints a game token: 256 bits from the platform's cryptographic source, as the Full Thrust
    /// participant tokens are. Handed back once, when the game is created, and required in a
    /// header on everything after.
    /// </summary>
    /// <returns>Sixty-four lower-case hex characters.</returns>
    public static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>
    /// Compares a token in time that does not depend on how much of it is right, so the length of
    /// the matching prefix cannot be read off a stopwatch. The same reasoning, and the same call,
    /// as the Full Thrust participant tokens.
    /// </summary>
    /// <param name="stored">The game's token.</param>
    /// <param name="supplied">The one the caller presented.</param>
    /// <returns>True when they are the same.</returns>
    public static bool TokensMatch(string stored, string supplied) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(supplied));
}
