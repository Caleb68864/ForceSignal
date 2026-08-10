using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers a turn played through the game rather than through the session directly.
/// </summary>
/// <remarks>
/// None of these rules are reimplemented here - every one is a call into
/// <see cref="GroundCombatSequence"/>, which has its own tests. What these check is the wrapping:
/// that a refusal arrives as a reason rather than an exception, and that the game that comes back
/// carries the new session.
/// </remarks>
public sealed class GameTurnTests
{
    [Fact]
    public void ATurnOpensWithSomebodyChoosingWhoGoesFirst()
    {
        var begun = GameFixtures.TwoSquadGame().BeginTurn();

        Assert.True(begun.IsAllowed);
        Assert.Equal(TurnPhase.ChoosingFirstActivator, begun.Value!.Session.Phase);
        Assert.Equal(1, begun.Value.Session.TurnNumber);
    }

    [Fact]
    public void TheSideThatTakesTheFirstActivationGetsIt()
    {
        var game = GameFixtures.TwoSquadGame().BeginTurn().Value!;

        var chosen = game.ChooseFirstActivator(GameFixtures.Blue, takeIt: true);

        Assert.True(chosen.IsAllowed);
        Assert.Equal(TurnPhase.Activating, chosen.Value!.Session.Phase);
        Assert.Equal(GameFixtures.Blue, chosen.Value.Session.ActiveSide);
    }

    [Fact]
    public void GivingTheFirstActivationAwayHandsItToTheOpponent()
    {
        var game = GameFixtures.TwoSquadGame().BeginTurn().Value!;

        var chosen = game.ChooseFirstActivator(GameFixtures.Blue, takeIt: false);

        Assert.Equal(GameFixtures.Red, chosen.Value!.Session.ActiveSide);
    }

    [Fact]
    public void ActivatingOutOfTurnIsRefusedWithAReasonRatherThanAThrow()
    {
        var game = GameFixtures.TwoSquadGame().BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!;

        var refused = game.BeginActivation(GameFixtures.Red, GameFixtures.Bravo);

        Assert.False(refused.IsAllowed);
        Assert.False(string.IsNullOrWhiteSpace(refused.Reason));
        Assert.Null(refused.Value);
    }

    [Fact]
    public void ActivatingAUnitThatIsNotOnTheTableIsRefused()
    {
        var game = GameFixtures.TwoSquadGame().BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!;

        var refused = game.BeginActivation(GameFixtures.Blue, new UnitId("ghost"));

        Assert.False(refused.IsAllowed);
        Assert.Contains("ghost", refused.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnActivationOpensAFrameForTheUnitTakingIt()
    {
        var game = GameFixtures.TwoSquadGame().BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!;

        var activating = game.BeginActivation(GameFixtures.Blue, GameFixtures.Alpha);

        Assert.True(activating.IsAllowed);
        Assert.Equal(GameFixtures.Alpha, activating.Value!.Session.CurrentFrame!.Unit);
    }

    [Fact]
    public void AWholeTurnCanBePlayedThroughToItsEnd()
    {
        var game = GameFixtures.TwoSquadGame().BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!;

        // Move then shoot. Two moves would be refused - the rules want a double move declared up
        // front as a dash, so the reaction window can open at its mid-point.
        game = game.BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;
        game = game.TakeStep(StarGruntSteps.Move()).Value!;
        game = game.TakeStep(StarGruntSteps.Fire("Rifles")).Value!;
        game = game.EndActivation().Value!;

        game = game.BeginActivation(GameFixtures.Red, GameFixtures.Bravo).Value!;
        game = game.TakeStep(StarGruntSteps.Dash()).Value!;
        game = game.EndActivation().Value!;

        var ended = game.EndTurn();

        Assert.True(ended.IsAllowed);
        Assert.Equal(TurnPhase.TurnEnded, ended.Value!.Session.Phase);
    }

    [Fact]
    public void TheNextTurnStartsTheChoiceAgain()
    {
        var ended = PlayedOutTurn();

        var second = ended.BeginTurn();

        Assert.True(second.IsAllowed);
        Assert.Equal(2, second.Value!.Session.TurnNumber);
        Assert.Equal(TurnPhase.ChoosingFirstActivator, second.Value.Session.Phase);
    }

    [Fact]
    public void EveryCommandLeavesTheRosterAlone()
    {
        var start = GameFixtures.TwoSquadGame();

        var played = PlayedOutTurn();

        Assert.Equal(start.Units, played.Units);
    }

    private static StarGruntGame PlayedOutTurn()
    {
        var game = GameFixtures.TwoSquadGame().BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!;

        game = game.BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;
        game = game.TakeStep(StarGruntSteps.Move()).Value!;
        game = game.TakeStep(StarGruntSteps.Fire("Rifles")).Value!;
        game = game.EndActivation().Value!;
        game = game.BeginActivation(GameFixtures.Red, GameFixtures.Bravo).Value!;
        game = game.TakeStep(StarGruntSteps.Dash()).Value!;
        game = game.EndActivation().Value!;

        return game.EndTurn().Value!;
    }
}
