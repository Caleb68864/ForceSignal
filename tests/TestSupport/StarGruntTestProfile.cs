using ForceSignal.Contracts.Ground;

namespace ForceSignal.TestSupport;

/// <summary>
/// The StarGrunt range table a service-level or HTTP-level test plays against, as it arrives on the
/// wire.
/// </summary>
/// <remarks>
/// <para>
/// Invented, and deliberately unlike any published game's, for the reason every fixture of this kind
/// in the repository gives: this app ships no numbers, so a test that passes against a table copied
/// out of a rulebook proves neither that the code read the profile nor that the rulebook stayed out.
/// Band widths are odd numbers that are no die's face count, the range table repeats its bottom row
/// and skips a rung, the reach is four bands, and no cover shift is the size of anything printed.
/// </para>
/// <para>
/// The same numbers as the module tests' <c>TestRangeTable</c>, so a volley scripted there comes out
/// the same here: a D8 squad's band is seven inches, nine inches is the second band's D4, and soft
/// cover's two rungs make it the D8 the worked firefight divides by. One file, linked into both test
/// projects that talk to the service, rather than typed out twice.
/// </para>
/// </remarks>
internal static class StarGruntTestProfile
{
    /// <summary>The invented table, complete enough to fight a whole game against.</summary>
    internal static StarGruntRulesProfileDto Invented { get; } = new(
        BandWidths:
        [
            new StarGruntBandWidthDto(4, 3),
            new StarGruntBandWidthDto(6, 5),
            new StarGruntBandWidthDto(8, 7),
            new StarGruntBandWidthDto(10, 9),
            new StarGruntBandWidthDto(12, 11),
        ],
        RangeDice:
        [
            new StarGruntRangeDieDto(1, 4),
            new StarGruntRangeDieDto(2, 4),
            new StarGruntRangeDieDto(3, 8),
            new StarGruntRangeDieDto(4, 10),
        ],
        EffectiveBands: 4,
        SoftCoverShift: 2,
        HardCoverShift: 4,
        InPositionShift: 3,
        MeleeCoverShift: 2,

        // Invented, and chosen to be the opposite of the bound that used to be written into the
        // service: under 2 to 5 a card marked 1 is refused and a card marked 4 goes on the table,
        // which is exactly backwards from a hardcoded 1-to-3. A test that passed against 1 to 3
        // could not tell a profile being read from a rulebook being recited.
        LowestLeadershipValue: 2,
        HighestLeadershipValue: 5);

    /// <summary>The invented table with the Leadership Values taken back out.</summary>
    /// <remarks>For the refusal that names the entry when nobody has said what the values are.</remarks>
    internal static StarGruntRulesProfileDto WithNoLeadershipValues { get; } = Invented with
    {
        LowestLeadershipValue = null,
        HighestLeadershipValue = null,
    };

    /// <summary>
    /// Nothing but the invented Leadership Values: no band widths, no range dice, no shifts.
    /// </summary>
    /// <remarks>
    /// What a test meaning "this game has no range table" hands in, now that the set of Leadership
    /// Values is on the same profile. A StarGrunt record card has to carry a Leadership Value, so a
    /// profile that says nothing about them is a game no unit can be put on the table in - which is
    /// the entered-nothing case, tested on its own, and not what those tests are about.
    /// </remarks>
    internal static StarGruntRulesProfileDto LeadershipValuesOnly { get; } = new(
        LowestLeadershipValue: 2,
        HighestLeadershipValue: 5);

    /// <summary>A create request carrying the invented table and nothing else.</summary>
    /// <param name="name">What to call the game.</param>
    /// <returns>The request.</returns>
    internal static CreateStarGruntGameRequest CreateGame(string name) => new(name, Invented);

    /// <summary>A create request whose profile says nothing about Leadership Values.</summary>
    /// <param name="name">What to call the game.</param>
    /// <returns>The request.</returns>
    internal static CreateStarGruntGameRequest CreateGameWithNoLeadershipValues(string name) =>
        new(name, WithNoLeadershipValues);
}
