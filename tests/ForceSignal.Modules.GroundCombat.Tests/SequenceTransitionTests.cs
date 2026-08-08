using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.GroundCombat.Tests;

/// <summary>The plain alternation, before any interrupt gets involved.</summary>
public class SequenceTransitionTests
{
    private static readonly ScriptedActivationPolicy Permissive = new();

    private static readonly string[] MoveThenFire = ["move", "fire"];

    [Fact]
    public void ActivatingHandsPlayToTheOpponentAndSpendsTheMarker()
    {
        var session = SequenceFixtures.TurnUnderWay(SequenceFixtures.Game(2, 2), SequenceFixtures.Blue);

        var opened = GroundCombatSequence.BeginActivation(session, SequenceFixtures.Blue, new UnitId("blue-1"));
        var closed = GroundCombatSequence.EndFrame(opened, Permissive);

        Assert.Equal(SequenceFixtures.Red, closed.ActiveSide);
        Assert.True(closed.IsIdle);
        Assert.Contains(new UnitId("blue-1"), closed.Side(SequenceFixtures.Blue).Activated);
        Assert.Equal(1, closed.Side(SequenceFixtures.Blue).UnactivatedCount);
    }

    [Fact]
    public void AUnitCannotBeActivatedTwiceInOneTurn()
    {
        var session = SequenceFixtures.TurnUnderWay(SequenceFixtures.Game(2, 2), SequenceFixtures.Blue);
        var blue1 = new UnitId("blue-1");

        var afterFirst = GroundCombatSequence.EndFrame(
            GroundCombatSequence.BeginActivation(session, SequenceFixtures.Blue, blue1),
            Permissive);
        var backToBlue = GroundCombatSequence.EndFrame(
            GroundCombatSequence.BeginActivation(afterFirst, SequenceFixtures.Red, new UnitId("red-1")),
            Permissive);

        Assert.False(GroundCombatSequence.CanBeginActivation(backToBlue, SequenceFixtures.Blue, blue1).IsAllowed);
    }

    [Fact]
    public void TheOtherSideCannotActivateOutOfTurn()
    {
        var session = SequenceFixtures.TurnUnderWay(SequenceFixtures.Game(2, 2), SequenceFixtures.Blue);

        var check = GroundCombatSequence.CanBeginActivation(session, SequenceFixtures.Red, new UnitId("red-1"));

        Assert.False(check.IsAllowed);
    }

    [Fact]
    public void AnIncompleteFrameCannotBeClosed()
    {
        var stubborn = new ScriptedActivationPolicy { CompletionRule = (_, _) => false };
        var session = SequenceFixtures.TurnUnderWay(SequenceFixtures.Game(1, 1), SequenceFixtures.Blue);
        var opened = GroundCombatSequence.BeginActivation(session, SequenceFixtures.Blue, new UnitId("blue-1"));

        Assert.False(GroundCombatSequence.CanEndFrame(opened, stubborn).IsAllowed);
        Assert.Throws<InvalidOperationException>(() => GroundCombatSequence.EndFrame(opened, stubborn));
    }

    [Fact]
    public void StepsAccumulateInOrderAndMayNameAnElement()
    {
        var session = SequenceFixtures.TurnUnderWay(SequenceFixtures.Game(1, 1), SequenceFixtures.Blue);
        var opened = GroundCombatSequence.BeginActivation(session, SequenceFixtures.Blue, new UnitId("blue-1"));

        var moved = GroundCombatSequence.TakeStep(
            opened, ActivationStep.Of("move", new ElementId("first-section")), Permissive);
        var fired = GroundCombatSequence.TakeStep(
            moved, ActivationStep.Of("fire", new ElementId("second-section")), Permissive);

        Assert.Equal(MoveThenFire, fired.CurrentFrame!.Steps.Select(step => step.Kind));
        Assert.Equal(new ElementId("second-section"), fired.CurrentFrame.Steps[1].Subject);
    }

    [Fact]
    public void PassingKeepsTheMarkerFaceUp()
    {
        // Three against five: Blue may pass, and passing must not cost it a unit.
        var session = SequenceFixtures.TurnUnderWay(SequenceFixtures.Game(3, 5), SequenceFixtures.Blue);

        var passed = GroundCombatSequence.Pass(session, SequenceFixtures.Blue);

        Assert.Equal(SequenceFixtures.Red, passed.ActiveSide);
        Assert.Equal(3, passed.Side(SequenceFixtures.Blue).UnactivatedCount);
        Assert.Equal(SequenceFixtures.Blue, Assert.Single(passed.ConsecutivePasses));
    }

    [Fact]
    public void ActivatingBreaksTheRunOfPasses()
    {
        var session = SequenceFixtures.TurnUnderWay(SequenceFixtures.Game(1, 3), SequenceFixtures.Blue);
        var passed = GroundCombatSequence.Pass(session, SequenceFixtures.Blue);

        var acted = GroundCombatSequence.BeginActivation(passed, SequenceFixtures.Red, new UnitId("red-1"));

        Assert.Empty(acted.ConsecutivePasses);
    }

    [Fact]
    public void AnExhaustedSideMayPassEvenThoughTheBareRuleWouldDeadlock()
    {
        var session = SequenceFixtures.TurnUnderWay(SequenceFixtures.Game(1, 1), SequenceFixtures.Blue);
        var blueDone = GroundCombatSequence.EndFrame(
            GroundCombatSequence.BeginActivation(session, SequenceFixtures.Blue, new UnitId("blue-1")),
            Permissive);
        var redDone = GroundCombatSequence.EndFrame(
            GroundCombatSequence.BeginActivation(blueDone, SequenceFixtures.Red, new UnitId("red-1")),
            Permissive);

        // Nobody has fewer unactivated units than anybody, so the shared guard says no...
        Assert.False(SequenceGuards.MayPass(redDone, SequenceFixtures.Blue).IsAllowed);

        // ...but with nothing left to activate this is not a choice, and the turn must be able to end.
        Assert.True(GroundCombatSequence.CanPass(redDone, SequenceFixtures.Blue).IsAllowed);
        Assert.True(GroundCombatSequence.CanEndTurn(redDone).IsAllowed);
    }

    [Fact]
    public void TheTurnEndsWhenBothSidesPassInSuccession()
    {
        var session = SequenceFixtures.TurnUnderWay(SequenceFixtures.Game(3, 5), SequenceFixtures.Blue);

        var bluePassed = GroundCombatSequence.Pass(session, SequenceFixtures.Blue);
        Assert.False(GroundCombatSequence.CanEndTurn(bluePassed).IsAllowed);

        // Red cannot pass on the bare rule with more unactivated units, so it activates instead.
        Assert.False(GroundCombatSequence.CanPass(bluePassed, SequenceFixtures.Red).IsAllowed);
    }

    [Fact]
    public void TurnEndFlipsEveryMarkerFaceUpAgain()
    {
        var session = SequenceFixtures.TurnUnderWay(SequenceFixtures.Game(1, 1), SequenceFixtures.Blue);
        var blueDone = GroundCombatSequence.EndFrame(
            GroundCombatSequence.BeginActivation(session, SequenceFixtures.Blue, new UnitId("blue-1")),
            Permissive);
        var redDone = GroundCombatSequence.EndFrame(
            GroundCombatSequence.BeginActivation(blueDone, SequenceFixtures.Red, new UnitId("red-1")),
            Permissive);

        var next = GroundCombatSequence.BeginTurn(GroundCombatSequence.EndTurn(redDone));

        Assert.Equal(2, next.TurnNumber);
        Assert.Equal(TurnPhase.ChoosingFirstActivator, next.Phase);
        Assert.All(next.Sides, side => Assert.Empty(side.Activated));
        Assert.Null(next.ActiveSide);
    }

    [Fact]
    public void CloseAssaultCanSpendAUnitsActivationWithNoFrameAtAll()
    {
        var session = SequenceFixtures.TurnUnderWay(SequenceFixtures.Game(1, 2), SequenceFixtures.Blue);
        var assaulting = GroundCombatSequence.BeginActivation(session, SequenceFixtures.Blue, new UnitId("blue-1"));

        var victim = new UnitId("red-1");
        var after = GroundCombatSequence.SpendActivationOutOfSequence(assaulting, SequenceFixtures.Red, victim);

        Assert.Contains(victim, after.Side(SequenceFixtures.Red).Activated);
        Assert.Equal(1, after.Depth);
        Assert.Equal(new UnitId("blue-1"), after.CurrentFrame!.Unit);
        Assert.False(GroundCombatSequence.CanSpendActivationOutOfSequence(after, SequenceFixtures.Red, victim).IsAllowed);
    }

    [Fact]
    public void RefusedTransitionsThrowWhenAppliedAnyway()
    {
        var session = SequenceFixtures.Game(1, 1);

        Assert.Throws<InvalidOperationException>(
            () => GroundCombatSequence.BeginActivation(session, SequenceFixtures.Blue, new UnitId("blue-1")));
    }
}
