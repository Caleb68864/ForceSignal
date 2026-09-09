using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Damage;

namespace ForceSignal.Modules.FullThrust.Tests;

/// <summary>
/// Threshold checks, played against <see cref="TestRules.Invented"/>, whose hulls are drawn in three
/// rows rather than anybody's published four.
/// </summary>
public sealed class FullThrustLightThresholdRulesTests
{
    private readonly FullThrustLightThresholdRules _rules = new();
    private static readonly RulesProfile Rules = TestRules.Invented;

    [Theory]
    // An even hull splits three ways.
    [InlineData(12, new[] { 4, 4, 4 })]
    [InlineData(9, new[] { 3, 3, 3 })]
    // Remainders go to the upper rows first.
    [InlineData(10, new[] { 4, 3, 3 })]
    [InlineData(11, new[] { 4, 4, 3 })]
    [InlineData(8, new[] { 3, 3, 2 })]
    // A hull too small to split that many ways gets one box per row.
    [InlineData(2, new[] { 1, 1 })]
    [InlineData(1, new[] { 1 })]
    [InlineData(0, new int[0])]
    public void TheTrackSplitsIntoTheProfilesRowsWeightedToTheTop(int hullMax, int[] expected) =>
        Assert.Equal(expected, _rules.HullRows(hullMax, Rules));

    [Theory]
    // A 12-box hull in three rows has rows of four, so a row completes every fourth point.
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    // The last row completing is the ship's destruction rather than another check.
    [InlineData(12, 3)]
    // Damage beyond the hull cannot complete more rows than the ship has.
    [InlineData(20, 3)]
    public void RowsFullyCrossedOffAreCounted(int hullDamage, int expected) =>
        Assert.Equal(expected, _rules.RowsCompleted(hullDamage, 12, Rules));

    [Theory]
    // A system is knocked out on a low roll, one number deeper per threshold reached.
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 2)]
    // An attack that tears through extra rows makes the same check one point worse per row.
    [InlineData(1, 1, 2)]
    [InlineData(2, 1, 3)]
    [InlineData(2, 2, 4)]
    public void TheKillNumberWorsensWithDepthAndExtraRows(int threshold, int extra, int expectedLostOn)
    {
        var result = Dice(8).Resolve(
            new ThresholdCheck(threshold, extra, [new ShipSystem(ShipSystemKind.Drive, "Drives")]),
            Rules);

        Assert.Equal(expectedLostOn, result.LostOn);
    }

    [Fact]
    public void OneDieIsRolledPerSurvivingSystem()
    {
        var systems = new[]
        {
            new ShipSystem(ShipSystemKind.Drive, "Drives"),
            new ShipSystem(ShipSystemKind.FireControl, "Fire control"),
            new ShipSystem(ShipSystemKind.Weapon, "Class-3 Beam", Guid.NewGuid()),
        };

        // At the first threshold only a 1 kills, so the drive dies and the rest survive.
        var result = Dice(1, 4, 8).Resolve(new ThresholdCheck(1, 0, systems), Rules);

        Assert.Equal(3, result.Rolls.Count);
        Assert.Equal([1, 4, 8], result.Rolls.Select(roll => roll.Die));
        Assert.Equal(1, result.LostOn);
        var lost = Assert.Single(result.Lost);
        Assert.Equal(ShipSystemKind.Drive, lost.Kind);
    }

    [Fact]
    public void EverythingRollingAtOrUnderTheNumberIsLost()
    {
        var systems = Enumerable.Range(1, 6)
            .Select(index => new ShipSystem(ShipSystemKind.Weapon, $"Mount {index}", Guid.NewGuid()))
            .ToArray();

        // The deepest check this profile reaches, torn one row deeper still: everything on 3 or less.
        var result = Dice(1, 2, 3, 4, 5, 6).Resolve(new ThresholdCheck(2, 1, systems), Rules);

        Assert.Equal(3, result.LostOn);
        Assert.Equal(["Mount 1", "Mount 2", "Mount 3"], result.Lost.Select(system => system.Name));
    }

    [Fact]
    public void NoSurvivingSystemsRollsNothing()
    {
        var result = _rules.Resolve(new ThresholdCheck(2, 0, []), Rules);

        Assert.Empty(result.Rolls);
        Assert.Empty(result.Lost);
    }

    [Fact]
    public void NoCheckIsRolledDeeperThanTheLastRowShortOfDeath()
    {
        // Completing the final row destroys the ship, so a check is never made for it. A caller that
        // asks anyway is clamped rather than producing a kill-everything check.
        var result = Dice(4).Resolve(
            new ThresholdCheck(9, 0, [new ShipSystem(ShipSystemKind.Screen, "Screens")]),
            Rules);

        Assert.Equal(2, result.Threshold);
        Assert.Equal(2, result.LostOn);
    }

    [Theory]
    [InlineData(ShipClassBand.Escort)]
    [InlineData(ShipClassBand.Cruiser)]
    [InlineData(ShipClassBand.Capital)]
    [InlineData(null)]
    public void AFixedRowProfileDrawsTheSameTrackWhateverTheHullIs(ShipClassBand? band) =>
        Assert.Equal(Rules.ThresholdRowCount, FullThrustLightThresholdRules.RowCountFor(Rules, band));

    [Theory]
    // Sized by band, a smaller hull faces fewer checks. A band nobody could work out falls back to
    // the profile's own row count.
    [InlineData(ShipClassBand.Escort, 2)]
    [InlineData(ShipClassBand.Cruiser, 3)]
    [InlineData(ShipClassBand.Capital, 3)]
    [InlineData(null, 3)]
    public void ByClassProfilesSizeTheTrackToTheBand(ShipClassBand? band, int expected) =>
        Assert.Equal(expected, FullThrustLightThresholdRules.RowCountFor(TestRules.WithRowsByClass, band));

    [Fact]
    public void TheTrackSplitsIntoHoweverManyRowsItIsAskedFor()
    {
        // A twelve-box hull in two rows runs 6/6 and faces one check instead of two.
        Assert.Equal([6, 6], FullThrustLightThresholdRules.HullRowsFor(12, rowCount: 2));
        Assert.Equal([4, 4, 4], FullThrustLightThresholdRules.HullRowsFor(12, rowCount: 3));
        // Remainders still weight to the upper rows.
        Assert.Equal([4, 3, 3], FullThrustLightThresholdRules.HullRowsFor(10, rowCount: 3));
        Assert.Equal(1, FullThrustLightThresholdRules.RowsCompletedFor(6, 12, rowCount: 2));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    public void TheDeepestCheckStopsOneRowShortBecauseTheLastRowIsDeath(int rowCount, int expected) =>
        Assert.Equal(expected, FullThrustLightThresholdRules.DeepestThresholdFor(rowCount));

    /// <summary>Threshold rules fed a fixed sequence of die faces, cycling if more are needed.</summary>
    private static FullThrustLightThresholdRules Dice(params int[] faces)
    {
        var index = 0;
        return new FullThrustLightThresholdRules(_ => faces[index++ % faces.Length]);
    }
}
