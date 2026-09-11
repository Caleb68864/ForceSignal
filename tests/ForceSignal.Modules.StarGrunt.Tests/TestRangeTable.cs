using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.StarGrunt.Combat;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// A StarGrunt range table for tests to play against.
/// </summary>
/// <remarks>
/// <para>
/// Invented, and deliberately unlike any published game's, for the reason Dirtside's
/// <c>TestDieTables</c> and Full Thrust's <c>TestRules</c> both give: this app ships no numbers, so a
/// test that passes against these proves the code read the profile it was handed rather than
/// remembering something, and a fixture copied out of a rulebook would put the rulebook back in the
/// repository by the back door.
/// </para>
/// <para>
/// So nothing here is a walk. Band widths are odd numbers that are no die's face count; the range
/// table repeats its bottom row and skips a rung, which no one-rung-per-band walk could produce; the
/// reach is four bands; and every cover shift is a different size from the others and from anything
/// printed.
/// </para>
/// <para>
/// Chosen for shape where it matters: a D8 squad's band is seven inches, so nine inches is the second
/// band, whose row is a D4, and soft cover's two rungs make it a D8 - the die the worked firefight's
/// arithmetic divides by. Hard cover and a settled position together come to seven rungs, which runs
/// off the ladder from any row, so the "beyond reach at any distance" case is reachable too.
/// </para>
/// </remarks>
internal static class TestRangeTable
{
    /// <summary>The invented table, complete enough to fight a whole game against.</summary>
    internal static StarGruntRulesProfile Invented { get; } = new(
        BandInches: ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create(QualityDie.D4, 3),
            KeyValuePair.Create(QualityDie.D6, 5),
            KeyValuePair.Create(QualityDie.D8, 7),
            KeyValuePair.Create(QualityDie.D10, 9),
            KeyValuePair.Create(QualityDie.D12, 11),
        ]),
        RangeDice: ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create(1, QualityDie.D4),
            KeyValuePair.Create(2, QualityDie.D4),
            KeyValuePair.Create(3, QualityDie.D8),
            KeyValuePair.Create(4, QualityDie.D10),
        ]),
        EffectiveBands: 4,
        SoftCoverShift: 2,
        HardCoverShift: 4,
        InPositionShift: 3);

    /// <summary>
    /// A table with nothing in it, for what a game does before anybody has entered their range page.
    /// </summary>
    internal static StarGruntRulesProfile Blank => StarGruntRulesProfile.Empty;
}
