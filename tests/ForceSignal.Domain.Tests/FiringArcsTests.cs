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
}
