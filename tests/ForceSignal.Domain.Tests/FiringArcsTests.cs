using ForceSignal.Domain.Rules;

namespace ForceSignal.Domain.Tests;

public sealed class FiringArcsTests
{
    [Theory]
    // Dead ahead and the wedge either side of it read as fore: the arc spans eleven to one o'clock.
    [InlineData(0, FiringArc.Fore)]
    [InlineData(0.9, FiringArc.Fore)]
    [InlineData(11.1, FiringArc.Fore)]
    // Boundaries fall to the more clockwise arc, matching how the arcs are named.
    [InlineData(1, FiringArc.ForeStarboard)]
    [InlineData(2, FiringArc.ForeStarboard)]
    [InlineData(3, FiringArc.AftStarboard)]
    [InlineData(4, FiringArc.AftStarboard)]
    [InlineData(5, FiringArc.Aft)]
    [InlineData(6, FiringArc.Aft)]
    [InlineData(7, FiringArc.AftPort)]
    [InlineData(8, FiringArc.AftPort)]
    [InlineData(9, FiringArc.ForePort)]
    [InlineData(10, FiringArc.ForePort)]
    [InlineData(11, FiringArc.Fore)]
    // Bearings wrap in both directions.
    [InlineData(12, FiringArc.Fore)]
    [InlineData(-1, FiringArc.Fore)]
    [InlineData(-3, FiringArc.ForePort)]
    [InlineData(15, FiringArc.AftStarboard)]
    public void FromRelativeClock_MapsClockPointsOntoSixtyDegreeArcs(double clockPoints, FiringArc expected)
    {
        Assert.Equal(expected, FiringArcs.FromRelativeClock(clockPoints));
    }

    [Theory]
    // Course 12 points up the table, so a target above the ship is dead ahead.
    [InlineData(12, 0, -10, FiringArc.Fore)]
    [InlineData(12, 0, 10, FiringArc.Aft)]
    // Dead abeam is the three o'clock boundary between the two starboard arcs, and boundaries
    // resolve clockwise, so a target exactly on the beam is in the aft quarter.
    [InlineData(12, 10, 0, FiringArc.AftStarboard)]
    [InlineData(12, -10, 0, FiringArc.ForePort)]
    // Slightly forward of the beam is unambiguously the forward quarter.
    [InlineData(12, 10, -1, FiringArc.ForeStarboard)]
    // Course 3 points along positive x, so the same table offsets read as different arcs.
    [InlineData(3, 10, 0, FiringArc.Fore)]
    [InlineData(3, 0, -10, FiringArc.ForePort)]
    [InlineData(3, 0, 10, FiringArc.AftStarboard)]
    [InlineData(3, 1, 10, FiringArc.ForeStarboard)]
    [InlineData(3, -10, 0, FiringArc.Aft)]
    // Course 6 points down the table.
    [InlineData(6, 0, 10, FiringArc.Fore)]
    [InlineData(6, 0, -10, FiringArc.Aft)]
    // A diagonal sits squarely inside a quarter arc rather than on a boundary.
    [InlineData(12, 10, 10, FiringArc.AftStarboard)]
    [InlineData(12, -10, 10, FiringArc.AftPort)]
    public void Bearing_ReadsArcsRelativeToTheShipsCourse(int course, double offsetX, double offsetY, FiringArc expected)
    {
        Assert.Equal(expected, FiringArcs.Bearing(course, offsetX, offsetY));
    }

    [Fact]
    public void Bearing_TreatsAStackedShipAsDeadAhead()
    {
        Assert.Equal(FiringArc.Fore, FiringArcs.Bearing(7, 0, 0));
    }

    [Fact]
    public void Firable_IsEveryArcExceptTheAftBlindSpot()
    {
        Assert.Equal(6, FiringArcs.All.Count);
        Assert.Equal(5, FiringArcs.Firable.Count);
        Assert.DoesNotContain(FiringArc.Aft, FiringArcs.Firable);
        Assert.False(FiringArcs.CanFireThrough(FiringArc.Aft));
        Assert.All(FiringArcs.Firable, arc => Assert.True(FiringArcs.CanFireThrough(arc)));
    }

    [Theory]
    // Canonical names round-trip.
    [InlineData("Fore", FiringArc.Fore)]
    [InlineData("AftPort", FiringArc.AftPort)]
    [InlineData("aft starboard", FiringArc.AftStarboard)]
    // The four-arc names ForceSignal used before six arcs still load.
    [InlineData("Port", FiringArc.ForePort)]
    [InlineData("Starboard", FiringArc.ForeStarboard)]
    [InlineData("All", FiringArc.Fore)]
    public void TryParse_AcceptsLegacyFourArcNames(string name, FiringArc expected)
    {
        Assert.True(FiringArcJsonConverter.TryParse(name, out var arc));
        Assert.Equal(expected, arc);
    }

    [Fact]
    public void TryParse_RejectsSomethingThatIsNotAnArc()
    {
        Assert.False(FiringArcJsonConverter.TryParse("Broadside", out _));
    }

    [Theory]
    // A ship on course 12 points up the table, so these are the six arc boundaries measured from a
    // round-numbered position - exactly what a player produces by dragging a ship onto a grid
    // intersection. The y offsets are +/-sqrt(3), which is what a 30 or 60 degree bearing needs.
    [InlineData(1.0, -1, FiringArc.ForeStarboard)]
    [InlineData(1.0, 0, FiringArc.AftStarboard)]
    [InlineData(1.0, 1, FiringArc.Aft)]
    [InlineData(-1.0, 1, FiringArc.AftPort)]
    [InlineData(-1.0, 0, FiringArc.ForePort)]
    [InlineData(-1.0, -1, FiringArc.Fore)]
    public void Bearing_OnAnExactBoundaryTakesTheMoreClockwiseArc(double offsetX, int rootThreeSign, FiringArc expected) =>
        Assert.Equal(expected, FiringArcs.Bearing(12, offsetX, rootThreeSign * Math.Sqrt(3)));

    [Fact]
    public void Bearing_IsStableWhenTheGeometryLandsAHairEitherSideOfABoundary()
    {
        // The arc tangent of a nominally exact right angle can fall on either side of the boundary
        // on the last bit of the mantissa, and which side it falls on decides whether a mount bears
        // at all. A player who measured a clean right angle should not be refused the shot.
        const double hair = 1e-13;
        Assert.Equal(FiringArc.AftStarboard, FiringArcs.Bearing(12, 1.0, hair));
        Assert.Equal(FiringArc.AftStarboard, FiringArcs.Bearing(12, 1.0, -hair));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FromRelativeClock_WithNoUsableBearingReadsAsForeRatherThanSaturating(double clockPoints) =>
        Assert.Equal(FiringArc.Fore, FiringArcs.FromRelativeClock(clockPoints));

    [Theory]
    [InlineData("\"Fore\"", FiringArc.Fore)]
    [InlineData("\"AftPort\"", FiringArc.AftPort)]
    [InlineData("\"aft-port\"", FiringArc.AftPort)]
    [InlineData("0", FiringArc.Fore)]
    [InlineData("4", FiringArc.AftPort)]
    public void Converter_ReadsBothANameAndAnOrdinal(string json, FiringArc expected) =>
        Assert.Equal(expected, System.Text.Json.JsonSerializer.Deserialize<FiringArc>(json, ConverterOptions));

    [Theory]
    // Fore is the most permissive arc there is, so failing open to it silently granted a mount
    // forward coverage it was never built with. A garbled file must be reported, not repaired.
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("\"Broadside\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Converter_RefusesAnythingThatIsNotAnArcRatherThanFailingOpen(string json) =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<FiringArc>(json, ConverterOptions));

    [Fact]
    public void Converter_RoundTripsEveryArcByName()
    {
        foreach (var arc in FiringArcs.All)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(arc, ConverterOptions);
            Assert.Equal(arc, System.Text.Json.JsonSerializer.Deserialize<FiringArc>(json, ConverterOptions));
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions ConverterOptions =
        new() { Converters = { new FiringArcJsonConverter() } };
}
