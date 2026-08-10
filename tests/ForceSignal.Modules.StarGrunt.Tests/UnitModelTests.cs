using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers what a unit is and what has happened to it. Every die on the definition is a number the
/// user types off their own record card - the engine ships none of them.
/// </summary>
public sealed class UnitModelTests
{
    [Fact]
    public void Status_ProjectsIntoTheStateTheActivationRulesRead()
    {
        var status = new UnitStatus
        {
            FiguresAlive = 6,
            SuppressionMarkers = 2,
            Confidence = ConfidenceLevel.Shaken,
            IsInCover = true,
            IsDisorganised = true,
            NextMoveLeavesCover = true,
            ReactionTestCleared = true,
            TransfersMadeThisTurn = 1,
        };

        var state = status.ToUnitState(CommandLevel.Platoon);

        Assert.Equal(CommandLevel.Platoon, state.Level);
        Assert.Equal(2, state.SuppressionMarkers);
        Assert.Equal(ConfidenceLevel.Shaken, state.Confidence);
        Assert.True(state.IsInCover);
        Assert.True(state.IsDisorganised);
        Assert.True(state.NextMoveLeavesCover);
        Assert.True(state.ReactionTestCleared);
        Assert.Equal(1, state.TransfersMadeThisTurn);
    }

    [Fact]
    public void AUnitStartsAtTheStrengthItsFiguresGiveIt()
    {
        var status = UnitStatus.ForFullStrength(Squad(8));

        Assert.Equal(8, status.FiguresAlive);
        Assert.Equal(0, status.FiguresWounded);
        Assert.Equal(ConfidenceLevel.Confident, status.Confidence);
        Assert.Equal(0, status.SuppressionMarkers);
    }

    [Fact]
    public void AUnitWithNoFiguresIsNotAUnit()
    {
        Assert.Equal(0, UnitStatus.ForFullStrength(Squad(0)).FiguresAlive);
    }

    private static UnitDefinition Squad(int figures) => new()
    {
        Id = new UnitId("alpha"),
        Name = "Alpha Squad",
        Side = new SideId("blue"),
        Figures = [.. Enumerable.Repeat(new FigureProfile(QualityDie.D6), figures)],
    };
}
