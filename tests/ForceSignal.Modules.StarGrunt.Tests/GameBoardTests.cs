using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers the game as a value, and as the board the activation rules read.
/// </summary>
/// <remarks>
/// <see cref="IStarGruntBoard"/> was written so the policy could read a world it does not own -
/// "the caller keeps it because the caller is the one rolling dice and moving models". Until now
/// there was no caller. These are the tests that it is one.
/// </remarks>
public sealed class GameBoardTests
{
    [Fact]
    public void AGameAnswersTheBoardQuestionsThePolicyAsks()
    {
        // Taken as the interface deliberately: the point is that a game satisfies the contract the
        // activation policy was written against, which is what the policy will receive it as.
        AssertAnswersAsABoard(GameFixtures.TwoSquadGame());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The interface is the assertion. Narrowing this to StarGruntGame would test that a game answers its own methods, which was never in doubt; what is being checked is that it satisfies the contract the activation policy receives it through.")]
    private static void AssertAnswersAsABoard(IStarGruntBoard board)
    {
        Assert.Equal(ConfidenceLevel.Confident, board.State(GameFixtures.Alpha).Confidence);
        Assert.Contains(CommandLevel.Squad, board.CommandLevelsOnTable);
        Assert.Null(board.DashOpening(GameFixtures.Alpha));
    }

    [Fact]
    public void TheBoardSeesCasualtiesAndSuppressionAsTheyLand()
    {
        var game = GameFixtures.TwoSquadGame()
            .WithStatus(GameFixtures.Bravo, status => status with { SuppressionMarkers = 2, IsInCover = true });

        var state = ((IStarGruntBoard)game).State(GameFixtures.Bravo);

        Assert.Equal(2, state.SuppressionMarkers);
        Assert.True(state.IsInCover);
    }

    [Fact]
    public void AUnitNobodyHasHeardOfIsNotOnTheTable()
    {
        var game = GameFixtures.TwoSquadGame();

        Assert.False(game.HasUnit(new UnitId("ghost")));
        Assert.Throws<ArgumentException>(() => game.Unit(new UnitId("ghost")));
    }

    [Fact]
    public void TwoGamesBuiltTheSameWayAreEqual()
    {
        Assert.Equal(GameFixtures.TwoSquadGame(), GameFixtures.TwoSquadGame());
        Assert.Equal(GameFixtures.TwoSquadGame().GetHashCode(), GameFixtures.TwoSquadGame().GetHashCode());
    }

    [Fact]
    public void AGameThatDiffersByOneCasualtyIsNotEqual()
    {
        var hurt = GameFixtures.TwoSquadGame()
            .WithStatus(GameFixtures.Bravo, status => status with { FiguresAlive = 7 });

        Assert.NotEqual(GameFixtures.TwoSquadGame(), hurt);
    }

    [Fact]
    public void TheSessionKnowsBothSidesAndEveryUnitOnThem()
    {
        var game = GameFixtures.TwoSquadGame();

        Assert.Equal(2, game.Session.Sides.Length);
        Assert.Equal(TurnPhase.NotStarted, game.Session.Phase);
    }
}
