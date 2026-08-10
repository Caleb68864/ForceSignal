using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers the reaction test: whether troops have the nerve to do the risky thing they were told to.
/// </summary>
/// <remarks>
/// Gap 7. The activation policy has always refused a shaken unit's move out of cover until a test
/// was passed - the seam was built and documented - and nothing ever offered the test, so the gate
/// stood permanently open.
///
/// The same roll as a confidence test, with one difference that decides the whole shape: failing
/// costs the action, never a confidence level.
/// </remarks>
public sealed class GameReactionTests
{
    [Fact]
    public void ASteadyUnitLeavesCoverWithoutBeingAsked()
    {
        // The test is only demanded of a unit whose nerve is already in question.
        var game = Activated().WithStatus(GameFixtures.Alpha, status => status with { NextMoveLeavesCover = true });

        Assert.True(game.TakeStep(StarGruntSteps.Move()).IsAllowed);
    }

    [Fact]
    public void AShakenUnitIsRefusedUntilItHasBeenTested()
    {
        var game = Wavering();

        var refused = game.TakeStep(StarGruntSteps.Move());

        Assert.False(refused.IsAllowed);
        Assert.Contains("reaction test", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PassingTheTestLetsTheUnitGo()
    {
        var game = Wavering().TakeReactionTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(6)).Value!;

        Assert.True(game.TakeStep(StarGruntSteps.Move()).IsAllowed);
    }

    [Fact]
    public void PassingCostsNothingByItself()
    {
        // The action is spent by the move that follows, not by finding the nerve to make it.
        var game = Wavering().TakeReactionTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(6)).Value!;

        Assert.Empty(game.Session.CurrentFrame!.Steps);
    }

    [Fact]
    public void FailingCostsTheActionButNotTheUnitsNerve()
    {
        var before = Wavering().Status(GameFixtures.Alpha).Confidence;

        var game = Wavering().TakeReactionTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(1)).Value!;

        Assert.Equal(before, game.Status(GameFixtures.Alpha).Confidence);
        Assert.Single(game.Session.CurrentFrame!.Steps);
    }

    [Fact]
    public void FailingLeavesTheUnitWhereItIs()
    {
        var game = Wavering().TakeReactionTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(1)).Value!;

        var refused = game.TakeStep(StarGruntSteps.Move());

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void ARefusedOrderCannotBeRetriedInTheSameActivation()
    {
        // The second action must be something else; the unit may try again next turn.
        var game = Wavering().TakeReactionTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(1)).Value!;

        var refused = game.TakeReactionTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
        Assert.Contains("next turn", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheUnitCanStillSpendItsOtherActionOnSomethingElse()
    {
        var game = Wavering().TakeReactionTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(1)).Value!;

        Assert.True(game.TakeStep(StarGruntSteps.Simple(StarGruntAction.Observe)).IsAllowed);
    }

    [Fact]
    public void TheRollIsRecordedWithWhatItNeeded()
    {
        var game = Wavering().TakeReactionTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(1)).Value!;

        Assert.Contains(game.Log, entry =>
            entry.Contains("Alpha Squad", StringComparison.Ordinal) && entry.Contains("nerve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AUnitWithNothingToSteelItselfForIsNotTested()
    {
        // Nothing risky has been declared, so there is no order to refuse.
        var refused = Activated().TakeReactionTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void ClearingTheDeclarationResetsTheGate()
    {
        var game = Wavering()
            .TakeReactionTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(6)).Value!
            .SetNextMoveLeavesCover(GameFixtures.Alpha, false).Value!;

        Assert.False(game.Status(GameFixtures.Alpha).ReactionTestCleared);
    }

    /// <summary>Alpha activated, shaken, and about to be ordered out of cover.</summary>
    private static StarGruntGame Wavering() =>
        Activated()
            .WithStatus(GameFixtures.Alpha, status => status with { Confidence = ConfidenceLevel.Shaken })
            .SetNextMoveLeavesCover(GameFixtures.Alpha, true).Value!;

    private static StarGruntGame Activated() =>
        GameFixtures.TwoSquadGame()
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;
}
