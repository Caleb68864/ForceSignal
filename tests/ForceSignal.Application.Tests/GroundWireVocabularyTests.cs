using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Game;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Holds the wire vocabularies to the engine enums they stand for.
/// </summary>
/// <remarks>
/// <para>
/// The contracts assembly deliberately depends on nothing but the domain, so it cannot borrow these
/// enums and has to spell the words out. That is a reasonable trade only while something notices
/// when the two stop agreeing, and until now nothing did: the arrays had no consumer at all, the
/// client had its own copy, and the server validated with Enum.TryParse against a third. A rung
/// added to a ladder would have been accepted by the API, offered by no screen, and named by no
/// refusal message.
/// </para>
/// <para>
/// This test project is where the check belongs because it is the one that already sees both sides.
/// Contents rather than order: the range bands carry the die shift as their value, so the enum
/// declares them Close, Medium, Long and returns them Long, Medium, Close, while the wire array is
/// ordered the way a table reads a record card.
/// </para>
/// </remarks>
public sealed class GroundWireVocabularyTests
{
    [Fact]
    public void Bands_AreExactlyTheBandsTheEngineResolvesShotsAt() =>
        AssertSameWords(Enum.GetNames<WeaponRangeBand>(), DirtsideWire.Bands);

    [Fact]
    public void FireControls_AreExactlyTheGunneryLevelsTheEngineKnows() =>
        AssertSameWords(Enum.GetNames<FireControlLevel>(), DirtsideWire.FireControls);

    [Fact]
    public void QualityDice_AreExactlyTheRungsOfTheLadder() =>
        AssertSameWords(Enum.GetNames<QualityDie>(), DirtsideWire.QualityDice);

    [Fact]
    public void AssaultStages_AreExactlyTheStagesAnAssaultCanStandAt() =>
        AssertSameWords(Enum.GetNames<AssaultStage>(), DirtsideWire.AssaultStages);

    [Fact]
    public void ChitColours_AreExactlyTheColoursAChitCanBePrintedIn() =>
        AssertSameWords(Enum.GetNames<ChitColour>(), DirtsideWire.ChitColours);

    [Fact]
    public void ChitSpecials_AreExactlyTheSpecialsThePotCanHold() =>
        AssertSameWords(Enum.GetNames<ChitSpecial>(), DirtsideWire.ChitSpecials);

    [Fact]
    public void ValueScales_AreExactlyTheWaysAChitCanBeCounted() =>
        AssertSameWords(Enum.GetNames<ChitValueScale>(), DirtsideWire.ValueScales);

    /// <summary>
    /// The colour sets a screen offers, held to the enum they are parsed against.
    /// </summary>
    /// <remarks>
    /// A subset rather than an equality, and deliberately: <c>ChitColours</c> is a <c>[Flags]</c>
    /// enum, so it also carries <c>None</c> and every pair, and the screens offer the four
    /// single-word spellings a player picks from. What must hold is that nothing offered is a word
    /// the parser would throw on - an equality here would fail today and would be asserting a UI
    /// decision that has not been made. The unoffered values are recorded as an open item rather
    /// than closed by a test that pretends otherwise.
    /// </remarks>
    [Fact]
    public void ChitColourSets_AreAllWordsTheEngineWouldAccept()
    {
        var known = Enum.GetNames<ChitColours>();

        // Reached-the-subject: the wire array is really populated, so the walk below is not over an
        // empty list - which would pass while proving nothing.
        Assert.NotEmpty(DirtsideWire.ChitColourSets);

        foreach (var offered in DirtsideWire.ChitColourSets)
        {
            Assert.Contains(offered, known);
            Assert.True(Enum.TryParse<ChitColours>(offered, ignoreCase: true, out _), $"'{offered}' is offered and does not parse");
        }
    }

    [Fact]
    public void Ladder_IsTheFaceCountsOfTheSameQualityDice()
    {
        // StarGrunt sends dice as face counts rather than names, so this is the same ladder read as
        // numbers. Ordered here, because a ladder that is not in order is not a ladder.
        var faces = Enum.GetValues<QualityDie>().Select(die => (int)die).Order().ToArray();
        Assert.Equal(faces, StarGruntWire.Ladder);
    }

    private static void AssertSameWords(string[] engine, string[] wire)
    {
        Assert.Equal(engine.Order(StringComparer.Ordinal), wire.Order(StringComparer.Ordinal));
        Assert.Equal(engine.Length, wire.Distinct(StringComparer.Ordinal).Count());
    }
}
