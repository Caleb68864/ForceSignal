using System.Collections.Immutable;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// A complete Dirtside rules profile for tests to play against.
/// </summary>
/// <remarks>
/// <para>
/// The numbers here are invented, and deliberately unlike any published game's: a basic sight rolls
/// the bottom of the ladder, the three sights are two rungs apart rather than one, the signature
/// table runs D10 down to D4 with a repeat at the bottom, and a posture is worth two rungs less than
/// the game it is modelled on. That is the point of them, and it is the same argument the Full
/// Thrust module's <c>TestRules</c> makes: the engine ships no dice of its own, so a test that
/// passes against these proves the code read the profile it was handed rather than remembering
/// something. A fixture copied out of a rulebook would prove neither, and would put the rulebook
/// back in the repository by the back door.
/// </para>
/// <para>
/// The one thing chosen for shape rather than for unlikeness: <see cref="Invented"/>'s basic sight
/// sits on the bottom rung, so a shot that steps down twice really does run out of ladder. That case
/// has to be reachable or the refusal it produces is untested.
/// </para>
/// </remarks>
internal static class TestDieTables
{
    /// <summary>The invented profile, complete enough to fight a whole game against.</summary>
    internal static DirtsideRulesProfile Invented { get; } = new(
        FireControlDice: ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create(FireControlLevel.Basic, QualityDie.D4),
            KeyValuePair.Create(FireControlLevel.Enhanced, QualityDie.D8),
            KeyValuePair.Create(FireControlLevel.Superior, QualityDie.D12),
        ]),
        PostureDice: ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create(DefensivePosture.SoftCover, QualityDie.D4),
            KeyValuePair.Create(DefensivePosture.Evading, QualityDie.D6),
            KeyValuePair.Create(DefensivePosture.HullDown, QualityDie.D8),
            KeyValuePair.Create(DefensivePosture.TurretDown, QualityDie.D10),
        ]),
        SignatureDice: ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create(1, QualityDie.D10),
            KeyValuePair.Create(2, QualityDie.D8),
            KeyValuePair.Create(3, QualityDie.D6),
            KeyValuePair.Create(4, QualityDie.D4),
            KeyValuePair.Create(5, QualityDie.D4),
        ]),
        SystemsDownRecoveryDie: QualityDie.D8,
        SystemsDownRecoveryRoll: 7,
        SystemsDownRecoveryRollWithBackup: 4,

        // Invented like the rest, and in nobody's units: the engine never compares this with
        // anything, because what an interception measures its reach against is one of the things
        // nobody has written down. It is here so that the tests about the entry being present and
        // the entry being absent are about the same profile.
        AreaDefenceReach: 9);

    /// <summary>
    /// A profile with nothing in it, for the tests about what a game does before anybody has entered
    /// their dice. Named rather than written out at each call site so those tests read as being about
    /// the same thing.
    /// </summary>
    internal static DirtsideRulesProfile Blank => DirtsideRulesProfile.Empty;

    /// <summary>The invented profile with the systems-down repair roll taken back out.</summary>
    internal static DirtsideRulesProfile WithNoRepairRoll { get; } = Invented with
    {
        SystemsDownRecoveryDie = null,
        SystemsDownRecoveryRoll = 0,
        SystemsDownRecoveryRollWithBackup = 0,
    };

    /// <summary>The invented profile with one signature row missing, for the refusal it produces.</summary>
    internal static DirtsideRulesProfile WithNoSignatureThree { get; } = Invented with
    {
        SignatureDice = Invented.SignatureDice.Remove(3),
    };
}
