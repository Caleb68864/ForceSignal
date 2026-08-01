using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Damage;

namespace ForceSignal.Modules.FullThrust.Tests;

public sealed class FullThrustLightThresholdRulesTests
{
    private readonly FullThrustLightThresholdRules _rules = new();

    [Theory]
    // An even hull splits four ways.
    [InlineData(12, new[] { 3, 3, 3, 3 })]
    [InlineData(8, new[] { 2, 2, 2, 2 })]
    // Remainders go to the upper rows first.
    [InlineData(10, new[] { 3, 3, 2, 2 })]
    [InlineData(9, new[] { 3, 2, 2, 2 })]
    [InlineData(6, new[] { 2, 2, 1, 1 })]
    // A hull too small to split four ways gets one box per row.
    [InlineData(3, new[] { 1, 1, 1 })]
    [InlineData(1, new[] { 1 })]
    [InlineData(0, new int[0])]
    public void HullRows_SplitsTheTrackIntoFourRowsWeightedToTheTop(int hullMax, int[] expected)
    {
        Assert.Equal(expected, _rules.HullRows(hullMax));
    }

    [Theory]
    // A 12-box hull has rows of three, so a row completes every third point.
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(9, 3)]
    // The last row completing is the ship's destruction rather than a fourth check.
    [InlineData(12, 4)]
    // Damage beyond the hull cannot complete more rows than the ship has.
    [InlineData(20, 4)]
    public void RowsCompleted_CountsRowsFullyCrossedOff(int hullDamage, int expected)
    {
        Assert.Equal(expected, _rules.RowsCompleted(hullDamage, 12));
    }

    [Theory]
    // Full Thrust Light knocks systems out on low rolls, one number deeper per threshold.
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 2)]
    [InlineData(3, 0, 3)]
    // An attack that tears through extra rows makes the same check one point worse per row.
    [InlineData(2, 1, 3)]
    [InlineData(3, 1, 4)]
    [InlineData(3, 2, 5)]
    public void Resolve_WorsensTheKillNumberWithDepthAndExtraRows(int threshold, int extra, int expectedLostOn)
    {
        var result = Dice(6).Resolve(new ThresholdCheck(threshold, extra, [new ShipSystem(ShipSystemKind.Drive, "Drives")]));

        Assert.Equal(expectedLostOn, result.LostOn);
    }

    [Fact]
    public void Resolve_RollsOneDiePerSurvivingSystem()
    {
        var systems = new[]
        {
            new ShipSystem(ShipSystemKind.Drive, "Drives"),
            new ShipSystem(ShipSystemKind.FireControl, "Fire control"),
            new ShipSystem(ShipSystemKind.Weapon, "Class-3 Beam", Guid.NewGuid()),
        };

        // At the first threshold only a 1 kills, so the drive dies and the rest survive.
        var result = Dice(1, 4, 6).Resolve(new ThresholdCheck(1, 0, systems));

        Assert.Equal(3, result.Rolls.Count);
        Assert.Equal([1, 4, 6], result.Rolls.Select(roll => roll.Die));
        Assert.Equal(1, result.LostOn);
        var lost = Assert.Single(result.Lost);
        Assert.Equal(ShipSystemKind.Drive, lost.Kind);
    }

    [Fact]
    public void Resolve_AtTheThirdThresholdLosesEverythingRollingThreeOrLess()
    {
        var systems = Enumerable.Range(1, 6)
            .Select(index => new ShipSystem(ShipSystemKind.Weapon, $"Mount {index}", Guid.NewGuid()))
            .ToArray();

        var result = Dice(1, 2, 3, 4, 5, 6).Resolve(new ThresholdCheck(3, 0, systems));

        Assert.Equal(3, result.LostOn);
        Assert.Equal(["Mount 1", "Mount 2", "Mount 3"], result.Lost.Select(system => system.Name));
    }

    [Fact]
    public void Resolve_WithNoSurvivingSystemsRollsNothing()
    {
        var result = _rules.Resolve(new ThresholdCheck(2, 0, []));

        Assert.Empty(result.Rolls);
        Assert.Empty(result.Lost);
    }

    [Fact]
    public void Resolve_NeverRollsDeeperThanTheThirdThreshold()
    {
        // Completing the fourth row destroys the ship, so a check is never made for it. A caller
        // that asks anyway is clamped rather than producing a kill-everything check.
        var result = Dice(4).Resolve(new ThresholdCheck(4, 0, [new ShipSystem(ShipSystemKind.Screen, "Screens")]));

        Assert.Equal(3, result.Threshold);
        Assert.Equal(3, result.LostOn);
    }

    /// <summary>Threshold rules fed a fixed sequence of die faces, cycling if more are needed.</summary>
    private static FullThrustLightThresholdRules Dice(params int[] faces)
    {
        var index = 0;
        return new FullThrustLightThresholdRules(() => faces[index++ % faces.Length]);
    }
}
