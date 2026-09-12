using ForceSignal.Contracts.Ground;

namespace ForceSignal.TestSupport;

/// <summary>
/// The Dirtside die tables a service-level or HTTP-level test plays against, as they arrive on the
/// wire.
/// </summary>
/// <remarks>
/// <para>
/// Invented, and deliberately unlike any published game's, for the reason the module's own
/// <c>TestDieTables</c> and Full Thrust's <c>TestRules</c> both give: this app ships no dice, so a
/// test that passes against a fixture copied out of a rulebook proves neither that the code read the
/// profile nor that the rulebook stayed out of the repository.
/// </para>
/// <para>
/// One file, linked into both test projects rather than typed out twice. Two copies of a fixture
/// that is supposed to be the single thing the tests agree about is how a vocabulary came to exist in
/// three spellings in this codebase once already.
/// </para>
/// </remarks>
internal static class DirtsideTestProfile
{
    /// <summary>The invented tables, complete enough to fight a whole game against.</summary>
    internal static DirtsideRulesProfileDto Invented { get; } = new(
        FireControl:
        [
            new DirtsideDieRowDto("Basic", "D4"),
            new DirtsideDieRowDto("Enhanced", "D8"),
            new DirtsideDieRowDto("Superior", "D12"),
        ],
        Posture:
        [
            new DirtsideDieRowDto("SoftCover", "D4"),
            new DirtsideDieRowDto("Evading", "D6"),
            new DirtsideDieRowDto("HullDown", "D8"),
            new DirtsideDieRowDto("TurretDown", "D10"),
        ],
        Signature:
        [
            new DirtsideDieRowDto("1", "D10"),
            new DirtsideDieRowDto("2", "D8"),
            new DirtsideDieRowDto("3", "D6"),
            new DirtsideDieRowDto("4", "D4"),
            new DirtsideDieRowDto("5", "D4"),
        ],
        SystemsDownRecoveryDie: "D8",
        SystemsDownRecoveryRoll: 7,
        SystemsDownRecoveryRollWithBackup: 4,

        // Invented like the rest, and in nobody's units. Nothing compares it with a distance,
        // because what an interception measures its reach against is one of the rules nobody has
        // written down; entering it is what makes an element eligible to answer.
        AreaDefenceReach: 9);

    /// <summary>The invented tables with the area-defence reach taken back out.</summary>
    /// <remarks>
    /// For the refusal that names the entry. A table that has switched its sensors on but never read
    /// a reach off their rulebook is the case this app must not fill in for them.
    /// </remarks>
    internal static DirtsideRulesProfileDto WithNoAreaDefenceReach { get; } = Invented with
    {
        AreaDefenceReach = 0,
    };

    /// <summary>A create request carrying the invented tables and nothing else.</summary>
    /// <param name="name">What to call the game.</param>
    /// <returns>The request.</returns>
    internal static CreateDirtsideGameRequest CreateGame(string name) => new(name, null, Invented);

    /// <summary>A create request whose profile carries everything but the area-defence reach.</summary>
    /// <param name="name">What to call the game.</param>
    /// <returns>The request.</returns>
    internal static CreateDirtsideGameRequest CreateGameWithNoAreaDefenceReach(string name) =>
        new(name, null, WithNoAreaDefenceReach);
}
