using ForceSignal.Modules.GroundCombat.Text;

namespace ForceSignal.Modules.GroundCombat.Tests;

/// <summary>Playtest p1: "1 hit(s)" and "Red Lancers's" in the log.</summary>
public sealed class WordsTests
{
    [Theory]
    [InlineData("Anvil", "Anvil's")]
    [InlineData("Red Lancers", "Red Lancers'")]
    [InlineData("HAWKS", "HAWKS'")]
    public void APossessiveReadsAsEnglish(string name, string expected) => Assert.Equal(expected, Words.Possessive(name));

    [Theory]
    [InlineData(0, "0 hits")]
    [InlineData(1, "1 hit")]
    [InlineData(2, "2 hits")]
    public void ACountTakesTheRightNoun(int count, string expected) => Assert.Equal(expected, Words.Count(count, "hit"));
}
