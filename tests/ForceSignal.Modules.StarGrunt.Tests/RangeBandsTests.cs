using ForceSignal.Modules.StarGrunt.Combat;
using ForceSignal.Modules.StarGrunt.Dice;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Range is not a to-hit penalty in this game; it inflates the die the target throws. These pin
/// down where each band boundary falls, how cover stacks on top, and the point past which there is
/// no effective shot at all.
/// </summary>
public sealed class RangeBandsTests
{
    [Theory]
    [InlineData(QualityDie.D4, 4)]
    [InlineData(QualityDie.D6, 6)]
    [InlineData(QualityDie.D8, 8)]
    [InlineData(QualityDie.D10, 10)]
    [InlineData(QualityDie.D12, 12)]
    public void ABandIsTheFirersOwnQualityDieMeasuredInInches(QualityDie quality, int expected) =>
        Assert.Equal(expected, RangeBands.BandInches(quality));

    [Theory]
    // A regular squad's band is 8 inches. Everything inside the first band is the same shot.
    [InlineData(0, QualityDie.D4)]
    [InlineData(1, QualityDie.D4)]
    [InlineData(8, QualityDie.D4)]
    // Crossing a band boundary is one rung, and the boundary itself belongs to the nearer band.
    [InlineData(8.1, QualityDie.D6)]
    [InlineData(16, QualityDie.D6)]
    [InlineData(16.1, QualityDie.D8)]
    [InlineData(24, QualityDie.D8)]
    [InlineData(24.1, QualityDie.D10)]
    [InlineData(32, QualityDie.D10)]
    [InlineData(32.1, QualityDie.D12)]
    [InlineData(40, QualityDie.D12)]
    public void EachFullBandOutMovesTheTargetsDieOneRungUp(decimal distance, QualityDie expected)
    {
        var solution = RangeBands.Resolve(distance, QualityDie.D8, new TargetPosture(CoverLevel.None));

        Assert.True(solution.CanFireEffectively);
        Assert.Equal(expected, solution.RangeDie);
    }

    [Fact]
    public void TheWorkedExampleFromTheRangeNotesComesOutAsWritten()
    {
        // A regular squad has an eight-inch band. At thirty inches the target is between three and
        // four bands out, so it rolls a D10.
        var solution = RangeBands.Resolve(30, QualityDie.D8, new TargetPosture(CoverLevel.None));

        Assert.True(solution.CanFireEffectively);
        Assert.Equal(QualityDie.D10, solution.RangeDie);
        Assert.Equal(4, solution.BandsOut);
    }

    [Theory]
    [InlineData(CoverLevel.None, false, QualityDie.D6)]
    [InlineData(CoverLevel.Soft, false, QualityDie.D8)]
    [InlineData(CoverLevel.Hard, false, QualityDie.D10)]
    [InlineData(CoverLevel.None, true, QualityDie.D8)]
    [InlineData(CoverLevel.Hard, true, QualityDie.D12)]
    public void CoverAndBeingDugInStackOnTopOfRange(CoverLevel cover, bool inPosition, QualityDie expected)
    {
        // Two bands out for a regular squad, so a D6 before anything else is counted.
        var solution = RangeBands.Resolve(16, QualityDie.D8, new TargetPosture(cover, inPosition));

        Assert.True(solution.CanFireEffectively);
        Assert.Equal(expected, solution.RangeDie);
    }

    [Theory]
    // Reach falls out of the shift rather than being a rule of its own: the die simply cannot climb
    // past the top of the ladder. Five bands in the open, four in soft cover, three in hard.
    [InlineData(CoverLevel.None, 40)]
    [InlineData(CoverLevel.Soft, 32)]
    [InlineData(CoverLevel.Hard, 24)]
    public void ReachIntoCoverIsShorterBecauseTheDieRunsOutOfLadder(CoverLevel cover, int maxInches)
    {
        var posture = new TargetPosture(cover);
        Assert.Equal(maxInches, RangeBands.MaxEffectiveRangeInches(QualityDie.D8, posture));

        Assert.True(RangeBands.Resolve(maxInches, QualityDie.D8, posture).CanFireEffectively);

        var tooFar = RangeBands.Resolve(maxInches + 0.1m, QualityDie.D8, posture);
        Assert.False(tooFar.CanFireEffectively);
        Assert.Contains("effective range", tooFar.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACloseRangeWeaponSimplyDoesNotReachPastTheFirstBand()
    {
        var posture = new TargetPosture(CoverLevel.None);

        Assert.True(RangeBands.Resolve(8, QualityDie.D8, posture, isCloseRangeWeapon: true).CanFireEffectively);

        var tooFar = RangeBands.Resolve(8.1m, QualityDie.D8, posture, isCloseRangeWeapon: true);
        Assert.False(tooFar.CanFireEffectively);
        Assert.Contains("only effective inside 8 inches", tooFar.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACloseRangeWeaponStillHasItsDieShiftedByCoverInsideThatBand()
    {
        var solution = RangeBands.Resolve(4, QualityDie.D8, new TargetPosture(CoverLevel.Hard), isCloseRangeWeapon: true);

        Assert.True(solution.CanFireEffectively);
        Assert.Equal(QualityDie.D8, solution.RangeDie);
    }

    [Fact]
    public void ATargetDugIntoHardCoverCanBeBeyondReachAtAnyDistance()
    {
        // Untrained troops have a four-inch band; three shifts leaves them two bands of reach.
        var posture = new TargetPosture(CoverLevel.Hard, InPosition: true);
        Assert.Equal(8, RangeBands.MaxEffectiveRangeInches(QualityDie.D4, posture));

        var solution = RangeBands.Resolve(9, QualityDie.D4, posture);
        Assert.False(solution.CanFireEffectively);
        Assert.Contains("hard cover and dug in", solution.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ANegativeDistanceIsRefusedRatherThanMeasured() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RangeBands.Resolve(-1, QualityDie.D8, new TargetPosture(CoverLevel.None)));
}
