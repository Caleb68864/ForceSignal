using System.Collections.Immutable;
using ForceSignal.Modules.StarGrunt.Combat;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Range is not a to-hit penalty in this game; it inflates the die the target throws. These pin down
/// the procedure - how bands are counted, that cover stacks on top, where the ladder runs out - against
/// an invented range table, and that every number in it is read off the players' profile rather than
/// known here.
/// </summary>
public sealed class RangeBandsTests
{
    private static readonly StarGruntRulesProfile Table = TestRangeTable.Invented;

    private static RangeSolution Shot(
        decimal distance,
        QualityDie firer = QualityDie.D8,
        CoverLevel cover = CoverLevel.None,
        bool inPosition = false,
        bool closeRange = false,
        StarGruntRulesProfile? profile = null) =>
        RangeBands.Resolve(profile ?? Table, distance, firer, new TargetPosture(cover, inPosition), closeRange);

    [Theory]
    // Each quality's band is whatever the table says it is - three, seven and eleven inches here,
    // which is no die's face count. The far edge belongs to the band; a tenth past it is the next.
    [InlineData(QualityDie.D4, 3)]
    [InlineData(QualityDie.D8, 7)]
    [InlineData(QualityDie.D12, 11)]
    public void ABandIsAsWideAsTheTableSaysForTheFirersQuality(QualityDie firer, int inches)
    {
        Assert.Equal(1, Shot(inches, firer).BandsOut);
        Assert.Equal(2, Shot(inches + 0.1m, firer).BandsOut);
    }

    [Theory]
    // A D8 squad's band is seven inches. Everything inside the first band is the same shot.
    [InlineData(0, QualityDie.D4)]
    [InlineData(1, QualityDie.D4)]
    [InlineData(7, QualityDie.D4)]
    // The second row repeats the first, and the third skips a rung. No walk up the ladder produces
    // either, so a pass here is the table being read rather than a walk remembered.
    [InlineData(7.1, QualityDie.D4)]
    [InlineData(14, QualityDie.D4)]
    [InlineData(14.1, QualityDie.D8)]
    [InlineData(21, QualityDie.D8)]
    [InlineData(21.1, QualityDie.D10)]
    [InlineData(28, QualityDie.D10)]
    public void EachBandOutReadsItsOwnRowOffTheTable(decimal distance, QualityDie expected)
    {
        var solution = Shot(distance);

        Assert.True(solution.CanFireEffectively);
        Assert.Equal(expected, solution.RangeDie);
    }

    [Theory]
    [InlineData(CoverLevel.None, false, QualityDie.D4, 0)]
    [InlineData(CoverLevel.Soft, false, QualityDie.D8, 2)]
    [InlineData(CoverLevel.Hard, false, QualityDie.D12, 4)]
    [InlineData(CoverLevel.None, true, QualityDie.D10, 3)]
    public void CoverAndBeingDugInStackOnTopOfRange(CoverLevel cover, bool inPosition, QualityDie expected, int rungs)
    {
        // One band out for a D8 squad, so a D4 before anything else is counted.
        var solution = Shot(7, cover: cover, inPosition: inPosition);

        Assert.True(solution.CanFireEffectively);
        Assert.Equal(expected, solution.RangeDie);
        // The rungs are carried out as well, because the same ones go on the target's armour.
        Assert.Equal(rungs, solution.PostureShift);
    }

    [Theory]
    // Reach into cover falls out of the shift rather than being a rule of its own: the die cannot
    // climb past the top of the ladder. Four bands in the open is the reach the table gives; soft
    // cover's two rungs run out of ladder on the fourth row, hard cover's four on the third.
    [InlineData(CoverLevel.None, 28)]
    [InlineData(CoverLevel.Soft, 21)]
    [InlineData(CoverLevel.Hard, 14)]
    public void ReachIntoCoverIsShorterBecauseTheDieRunsOutOfLadder(CoverLevel cover, int maxInches)
    {
        Assert.True(Shot(maxInches, cover: cover).CanFireEffectively);

        var tooFar = Shot(maxInches + 0.1m, cover: cover);
        Assert.False(tooFar.CanFireEffectively);
        Assert.False(tooFar.IsMissingFromProfile);
        Assert.Contains("effective range", tooFar.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PastTheReachThePlayersEnteredThereIsNoEffectiveShotRatherThanAMissingRow()
    {
        // The fifth band has no row, and the table says small arms reach four. That is a rule about
        // the shot, not a gap in the table, and the two have to come back differently: one is taken
        // and wasted, the other must not cost the unit anything.
        var solution = Shot(28.1m);

        Assert.False(solution.CanFireEffectively);
        Assert.False(solution.IsMissingFromProfile);
        Assert.Equal(5, solution.BandsOut);
        Assert.Contains("gives small arms 4", solution.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ACloseRangeWeaponSimplyDoesNotReachPastTheFirstBand()
    {
        Assert.True(Shot(7, closeRange: true).CanFireEffectively);

        var tooFar = Shot(7.1m, closeRange: true);
        Assert.False(tooFar.CanFireEffectively);
        Assert.Contains("only effective inside 7 inches", tooFar.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACloseRangeWeaponStillHasItsDieShiftedByCoverInsideThatBand()
    {
        var solution = Shot(4, cover: CoverLevel.Hard, closeRange: true);

        Assert.True(solution.CanFireEffectively);
        Assert.Equal(QualityDie.D12, solution.RangeDie);
    }

    [Fact]
    public void ATargetDugIntoHardCoverCanBeBeyondReachAtAnyDistance()
    {
        // Hard cover and a settled position come to seven rungs on this table, which runs off the
        // ladder from the bottom row - so there is no distance at which the shot is effective.
        var solution = Shot(1, QualityDie.D4, CoverLevel.Hard, inPosition: true);

        Assert.False(solution.CanFireEffectively);
        Assert.False(solution.IsMissingFromProfile);
        Assert.Contains("hard cover and dug in", solution.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ANegativeDistanceIsRefusedRatherThanMeasured() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Shot(-1));

    [Fact]
    public void AMissingProfileIsRefusedRatherThanReadAsEmpty() =>
        Assert.Throws<ArgumentNullException>(() =>
            RangeBands.Resolve(null!, 7, QualityDie.D8, new TargetPosture(CoverLevel.None)));

    [Fact]
    public void ABlankTableRefusesTheFirstThingTheShotReadsAndNamesIt()
    {
        // A game created without a range page. Not a D4 from the bottom of the ladder, not a band
        // the size of the firer's die: a refusal that says what to go and enter.
        var solution = Shot(9, profile: TestRangeTable.Blank);

        Assert.False(solution.CanFireEffectively);
        Assert.True(solution.IsMissingFromProfile);
        Assert.Null(solution.RangeDie);
        Assert.Contains("range band is for D8 troops", solution.Reason!, StringComparison.Ordinal);
        Assert.Contains("ships none of its own", solution.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void NoBandWidthForTheFirersQualityIsRefusedByName()
    {
        var withoutD8 = Table with { BandInches = Table.BandInches.Remove(QualityDie.D8) };

        var refused = Shot(9, profile: withoutD8);
        Assert.True(refused.IsMissingFromProfile);
        Assert.Contains("D8 troops", refused.Reason!, StringComparison.Ordinal);

        // The control that must be accepted: the same table, a firer whose width is on it.
        Assert.True(Shot(9, QualityDie.D6, profile: withoutD8).CanFireEffectively);
    }

    [Fact]
    public void NoRowForTheBandIsRefusedByName()
    {
        var withoutThree = Table with { RangeDice = Table.RangeDice.Remove(3) };

        var refused = Shot(15, profile: withoutThree);
        Assert.True(refused.IsMissingFromProfile);
        Assert.Contains("3 bands out", refused.Reason!, StringComparison.Ordinal);

        // The control: the band beside it is on the table and settles.
        Assert.True(Shot(14, profile: withoutThree).CanFireEffectively);
    }

    [Fact]
    public void WithNoReachEnteredABandPastTheTableAsksForEitherAnswer()
    {
        // Four rows and no reach. The fifth band could be past effective range in the players' rules
        // or a row they have not typed yet, and this app cannot tell which - so it refuses naming both
        // rather than deciding the table ends where the typing did.
        var noReach = Table with { EffectiveBands = null };

        var refused = Shot(28.1m, profile: noReach);
        Assert.True(refused.IsMissingFromProfile);
        Assert.Contains("5 bands out", refused.Reason!, StringComparison.Ordinal);
        Assert.Contains("how many bands small arms reach", refused.Reason!, StringComparison.Ordinal);

        // The control: a band that has a row needs no reach to settle.
        Assert.True(Shot(28, profile: noReach).CanFireEffectively);
    }

    [Theory]
    [InlineData(CoverLevel.Soft, false, "soft cover")]
    [InlineData(CoverLevel.Hard, false, "hard cover")]
    [InlineData(CoverLevel.None, true, "settled into its position")]
    public void AnUnenteredCoverOrPostureIsRefusedByName(CoverLevel cover, bool inPosition, string named)
    {
        var bare = Table with { SoftCoverShift = null, HardCoverShift = null, InPositionShift = null };

        var refused = Shot(7, cover: cover, inPosition: inPosition, profile: bare);
        Assert.True(refused.IsMissingFromProfile);
        Assert.Contains(named, refused.Reason!, StringComparison.Ordinal);

        // The control: the same bare table settles a target in the open, which reads none of them.
        Assert.True(Shot(7, profile: bare).CanFireEffectively);
    }

    [Fact]
    public void OnlyWhatTheShotReadsHasToBeThere()
    {
        // The over-strict trap, which this project has paid for twice. One band width, one row, no
        // reach and no cover: a table that fights one kind of squad at one distance across open
        // ground has entered everything it needs.
        var minimal = new StarGruntRulesProfile(
            ImmutableDictionary<QualityDie, int>.Empty.Add(QualityDie.D8, 7),
            ImmutableDictionary<int, QualityDie>.Empty.Add(1, QualityDie.D6));

        var solution = Shot(5, profile: minimal);

        Assert.True(solution.CanFireEffectively);
        Assert.Equal(QualityDie.D6, solution.RangeDie);
    }

    [Fact]
    public void AShotAlreadyOutOfReachDoesNotGoOnToAskWhatCoverIsWorth()
    {
        // Reach first, cover after: a shot the table already says cannot be effective is not refused
        // for a cover shift it would never have used.
        var noCover = Table with { HardCoverShift = null };

        var solution = Shot(28.1m, cover: CoverLevel.Hard, profile: noCover);

        Assert.False(solution.CanFireEffectively);
        Assert.False(solution.IsMissingFromProfile);
    }
}
