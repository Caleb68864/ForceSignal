using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Fighters;

namespace ForceSignal.Modules.FullThrust.Tests;

/// <summary>
/// How many groups a deck can work in a turn, under both shapes a profile can pick: capped by what
/// the ship is, or following the bays it actually has.
/// </summary>
/// <remarks>
/// Played against <see cref="TestRules.Invented"/>, where a true carrier works three groups and
/// anything else works one. Three is nobody's published number, which is the point: the engine reads
/// it rather than knowing it.
/// </remarks>
public sealed class FullThrustCarrierOperationRulesTests
{
    private static readonly RulesProfile Rules = TestRules.Invented;

    [Theory]
    // Under the cap the bay count changes nothing: a true carrier works the profile's carrier
    // number, anything else works the other, and launches and recoveries share one budget.
    [InlineData(1, true, 3, 3)]
    [InlineData(6, true, 3, 3)]
    [InlineData(6, false, 1, 1)]
    public void ACappedProfileFollowsWhatTheShipIs(
        int bays, bool isTrueCarrier, int expectedLaunches, int expectedRecoveries)
    {
        var allowance = FullThrustCarrierOperationRules.AllowanceFor(Rules, bays, isTrueCarrier);

        Assert.Equal(expectedLaunches, allowance.Launches);
        Assert.Equal(expectedRecoveries, allowance.Recoveries);
        Assert.True(allowance.SharedAllowance);
    }

    [Theory]
    // Following the bays, the rate is the bay count out and half the bays back, and being a carrier
    // by trade buys nothing extra - which is the whole point of the change.
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(6, 6, 3)]
    // Half of an odd bay count is rounded up, so a small deck is never worse off than under the cap.
    [InlineData(7, 7, 4)]
    [InlineData(0, 0, 0)]
    public void ABayRateProfileFollowsTheBays(int bays, int expectedLaunches, int expectedRecoveries)
    {
        foreach (var isTrueCarrier in new[] { true, false })
        {
            var allowance = FullThrustCarrierOperationRules.AllowanceFor(TestRules.WithBayRates, bays, isTrueCarrier);

            Assert.Equal(expectedLaunches, allowance.Launches);
            Assert.Equal(expectedRecoveries, allowance.Recoveries);
            // Launch and recovery have separate budgets, so a deck can work both ways in a turn.
            Assert.False(allowance.SharedAllowance);
        }
    }

    [Theory]
    // The profile names the faces it cares about; anything it does not mention leaves the group
    // flyable and ready next turn, which is the least this roll can do to it.
    [InlineData(1, true, 0)]
    [InlineData(8, false, 1)]
    [InlineData(4, false, 1)]
    public void TheTurnaroundRollIsReadOffTheProfile(int face, bool expectedGrounded, int expectedTurns)
    {
        var turnaround = new FullThrustCarrierOperationRules(_ => face).RollTurnaround(Rules);

        Assert.Equal(face, turnaround.Roll);
        Assert.Equal(expectedGrounded, turnaround.IsGroundedForGame);
        Assert.Equal(expectedTurns, turnaround.TurnsBeforeRelaunch);
    }

    [Fact]
    public void ADifferentTurnaroundTableIsReadJustTheSame()
    {
        var harsh = Rules with
        {
            Turnaround =
            [
                new TurnaroundEntry(1, IsGroundedForGame: true, TurnsBeforeRelaunch: 0),
                new TurnaroundEntry(2, IsGroundedForGame: false, TurnsBeforeRelaunch: 4),
            ],
        };

        var turnaround = new FullThrustCarrierOperationRules(_ => 2).RollTurnaround(harsh);

        Assert.Equal(4, turnaround.TurnsBeforeRelaunch);
    }
}
