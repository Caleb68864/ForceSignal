using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Fighters;

namespace ForceSignal.Modules.FullThrust.Tests;

/// <summary>
/// How many groups a deck can work in a turn is the clearest difference between the two layers: the
/// older one caps flight operations by what the ship is called, the newer one by how many bays it
/// actually has.
/// </summary>
public sealed class FullThrustCarrierOperationRulesTests
{
    [Theory]
    // Under the older cap the bay count changes nothing: a true carrier works two groups, anything
    // else works one, and launches and recoveries share that one budget.
    [InlineData(1, true, 2, 2)]
    [InlineData(6, true, 2, 2)]
    [InlineData(6, false, 1, 1)]
    public void AllowanceFor_UnderTheLightLayerFollowsWhatTheShipIs(
        int bays, bool isTrueCarrier, int expectedLaunches, int expectedRecoveries)
    {
        var allowance = FullThrustCarrierOperationRules.AllowanceFor(RulesProfile.LightCinematic, bays, isTrueCarrier);

        Assert.Equal(expectedLaunches, allowance.Launches);
        Assert.Equal(expectedRecoveries, allowance.Recoveries);
        Assert.True(allowance.SharedAllowance);
    }

    [Theory]
    // Under the Fleet Book the rate is the bay count out and half the bays back, and being a
    // carrier by trade buys nothing extra - which is the amendment's stated purpose.
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(6, 6, 3)]
    // Half of an odd bay count is rounded up, so a small deck is never worse off than it was.
    [InlineData(7, 7, 4)]
    [InlineData(0, 0, 0)]
    public void AllowanceFor_UnderTheFleetBookLayerFollowsTheBays(int bays, int expectedLaunches, int expectedRecoveries)
    {
        foreach (var isTrueCarrier in new[] { true, false })
        {
            var allowance = FullThrustCarrierOperationRules.AllowanceFor(RulesProfile.FleetBook, bays, isTrueCarrier);

            Assert.Equal(expectedLaunches, allowance.Launches);
            Assert.Equal(expectedRecoveries, allowance.Recoveries);
            // Launch and recovery have separate budgets, so a deck can work both ways in a turn.
            Assert.False(allowance.SharedAllowance);
        }
    }

    [Fact]
    public void AllowanceFor_WithNoLayerNamedFallsBackToTheLightCinematicDefault()
    {
        var allowance = FullThrustCarrierOperationRules.AllowanceFor(null, operationalBays: 6, isTrueCarrier: false);

        Assert.Equal(1, allowance.Launches);
        Assert.True(allowance.SharedAllowance);
    }

    [Theory]
    [InlineData(1, true, 0)]
    [InlineData(2, false, 2)]
    [InlineData(5, false, 2)]
    [InlineData(6, false, 1)]
    public void RollTurnaround_ReadsTheDieIntoTimeOnTheDeck(int face, bool expectedGrounded, int expectedTurns)
    {
        var turnaround = new FullThrustCarrierOperationRules(() => face).RollTurnaround();

        Assert.Equal(face, turnaround.Roll);
        Assert.Equal(expectedGrounded, turnaround.IsGroundedForGame);
        Assert.Equal(expectedTurns, turnaround.TurnsBeforeRelaunch);
    }
}
