using ForceSignal.Modules.Dirtside.Chits;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// The composition is configuration rather than a rule, so what is tested here is that the shipped
/// default is internally coherent and that a caller can replace it - not that its numbers are right,
/// which is the caller's own rulebook to check.
/// </summary>
public sealed class ChitPotCompositionTests
{
    private static readonly int[] EveryPrintedValue = [0, 1, 2, 3];

    [Fact]
    public void TheDefaultPotHasAHundredNumberedChitsSplitFiftyTwentyFiveTwentyFive()
    {
        var numerical = ChitPotComposition.Default.Chits.Where(c => !c.IsSpecial).ToList();

        Assert.Equal(100, numerical.Count);
        Assert.Equal(50, numerical.Count(c => c.Colour == ChitColour.Red));
        Assert.Equal(25, numerical.Count(c => c.Colour == ChitColour.Yellow));
        Assert.Equal(25, numerical.Count(c => c.Colour == ChitColour.Green));
    }

    [Theory]
    [InlineData(ChitColour.Red)]
    [InlineData(ChitColour.Yellow)]
    [InlineData(ChitColour.Green)]
    public void EveryColourCarriesTheSameSpreadOfValues(ChitColour colour)
    {
        // This is the load-bearing property of the default. The colours are supposed to be identical
        // in severity and to differ only in availability, so if one colour ran hotter than another
        // the validity table would quietly become a damage table.
        var values = ChitPotComposition.Default.Chits
            .Where(c => c.Colour == colour)
            .Select(c => c.Value)
            .ToList();

        Assert.Equal(EveryPrintedValue, values.Distinct().Order().ToArray());

        // Handing the remainder out around the middle keeps the mean within a twentieth of centre
        // even when the count does not divide by four.
        Assert.InRange(values.Average(), 1.45, 1.55);
    }

    [Fact]
    public void TheDefaultPotHasFewerOfTheFirersSystemsDownThanTheTargets()
    {
        // The one thing the source actually says about the special counts. Everything else about
        // them is a documented guess, so it is not asserted here.
        var pot = ChitPotComposition.Default;

        Assert.True(
            pot.CountOf(DamageChit.Of(ChitSpecial.SystemsDownFirer))
            < pot.CountOf(DamageChit.Of(ChitSpecial.SystemsDownTarget)));
    }

    [Fact]
    public void EverySpecialIsInTheDefaultPotAtLeastOnce()
    {
        foreach (var special in Enum.GetValues<ChitSpecial>())
        {
            Assert.True(ChitPotComposition.Default.CountOf(DamageChit.Of(special)) > 0, $"{special} is missing");
        }
    }

    [Fact]
    public void ACallerCanReplaceTheWholeComposition()
    {
        var mine = ChitPotComposition.FromCounts(new Dictionary<DamageChit, int>
        {
            [DamageChit.Numerical(ChitColour.Red, 2)] = 3,
            [DamageChit.Of(ChitSpecial.Boom)] = 1,
        });

        Assert.Equal(4, mine.Count);
        Assert.Equal(3, mine.CountOf(DamageChit.Numerical(ChitColour.Red, 2)));
        Assert.Equal(0, mine.CountOf(DamageChit.Numerical(ChitColour.Red, 1)));
    }

    [Fact]
    public void ACallerCanCorrectJustTheSpecialsAndKeepTheNumbers()
    {
        var corrected = ChitPotComposition.Default.WithSpecials(new Dictionary<ChitSpecial, int>
        {
            [ChitSpecial.Boom] = 1,
        });

        Assert.Equal(101, corrected.Count);
        Assert.Equal(1, corrected.CountOf(DamageChit.Of(ChitSpecial.Boom)));
        Assert.Equal(0, corrected.CountOf(DamageChit.Of(ChitSpecial.Mobility)));
        Assert.Equal(100, corrected.Chits.Count(c => !c.IsSpecial));
    }

    [Fact]
    public void AnEmptyPotIsRefused() =>
        Assert.Throws<ArgumentException>(() => ChitPotComposition.Of([]));

    [Fact]
    public void ANegativeCountIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ChitPotComposition.FromCounts(
            new Dictionary<DamageChit, int> { [DamageChit.Of(ChitSpecial.Boom)] = -1 }));

    [Fact]
    public void AZeroChitIsARealChitRatherThanAnAbsence()
    {
        var zero = DamageChit.Numerical(ChitColour.Green, 0);

        Assert.True(zero.IsZero);
        Assert.False(zero.IsSpecial);
        Assert.True(ChitPotComposition.Default.CountOf(zero) > 0);
    }
}
