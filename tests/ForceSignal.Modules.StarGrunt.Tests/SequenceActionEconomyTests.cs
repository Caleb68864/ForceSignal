using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>Two actions, spendable in any order, and what may be done twice.</summary>
public class SequenceActionEconomyTests
{
    private readonly StarGruntBoard board = SequenceFixtures.Board();
    private readonly StarGruntActivationPolicy policy;

    public SequenceActionEconomyTests() => policy = new StarGruntActivationPolicy(board);

    [Fact]
    public void AnActivationBuysTwoActionsAndNoMore()
    {
        var moved = GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), StarGruntSteps.Move(), policy);
        var fired = GroundCombatSequence.TakeStep(moved, StarGruntSteps.Fire("rifles"), policy);

        Assert.Equal(0, StarGruntActivationPolicy.ActionsRemaining(fired.CurrentFrame!));
        Assert.False(
            GroundCombatSequence.CanTakeStep(fired, StarGruntSteps.Simple(StarGruntAction.Observe), policy)
                .IsAllowed);
    }

    [Fact]
    public void TheOrderOfTheTwoActionsIsFree()
    {
        var fired = GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), StarGruntSteps.Fire("rifles"), policy);

        Assert.True(GroundCombatSequence.CanTakeStep(fired, StarGruntSteps.Move(), policy).IsAllowed);
    }

    [Fact]
    public void ADashCostsBothActions()
    {
        var dashed = GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), StarGruntSteps.Dash(), policy);

        Assert.Equal(0, StarGruntActivationPolicy.ActionsRemaining(dashed.CurrentFrame!));
        Assert.False(GroundCombatSequence.CanTakeStep(dashed, StarGruntSteps.Fire("rifles"), policy).IsAllowed);
    }

    [Fact]
    public void ADashCannotBeSqueezedInAfterSomethingElse()
    {
        var observed = GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA),
            StarGruntSteps.Simple(StarGruntAction.Observe),
            policy);

        var check = GroundCombatSequence.CanTakeStep(observed, StarGruntSteps.Dash(), policy);

        Assert.False(check.IsAllowed);
        Assert.Contains("costs 2 actions", check.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The deliberate departure from the literal rules text, which lists movement as repeatable. Two
    /// move actions are a dash; making the player say so up front is what lets the reaction window
    /// open at the mid-point without a speculative window that might be withdrawn.
    /// </summary>
    [Fact]
    public void MovingTwiceIsRefusedAndPointsAtTheDash()
    {
        var moved = GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), StarGruntSteps.Move(), policy);

        var check = GroundCombatSequence.CanTakeStep(moved, StarGruntSteps.Move(), policy);

        Assert.False(check.IsAllowed);
        Assert.Contains("dash", check.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StarGruntAction.Communicate)]
    [InlineData(StarGruntAction.Observe)]
    [InlineData(StarGruntAction.FormDetachedElement)]
    public void DoubledUpActionsMayBeAttemptedTwice(StarGruntAction action)
    {
        var once = GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), StarGruntSteps.Simple(action), policy);

        Assert.True(GroundCombatSequence.CanTakeStep(once, StarGruntSteps.Simple(action), policy).IsAllowed);
    }

    [Theory]
    [InlineData(StarGruntAction.Reorganise)]
    [InlineData(StarGruntAction.GoInPosition)]
    [InlineData(StarGruntAction.CloseAssault)]
    public void EverythingElseMayBeAttemptedOnlyOnce(StarGruntAction action)
    {
        var once = GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), StarGruntSteps.Simple(action), policy);

        Assert.False(GroundCombatSequence.CanTakeStep(once, StarGruntSteps.Simple(action), policy).IsAllowed);
    }

    /// <summary>No unit is ever forced to act, so an activation is closable the moment it opens.</summary>
    [Fact]
    public void AnActivationMayBeEndedWithoutSpendingAnything()
    {
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);

        Assert.True(GroundCombatSequence.CanEndFrame(opened, policy).IsAllowed);
        Assert.Equal(2, StarGruntActivationPolicy.ActionsRemaining(opened.CurrentFrame!));
    }

    [Fact]
    public void AStepKindTheGameHasNoRuleForIsRefused()
    {
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);

        var check = GroundCombatSequence.CanTakeStep(opened, ActivationStep.Of("Teleport"), policy);

        Assert.False(check.IsAllowed);
        Assert.Contains("not a StarGrunt action", check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void FireAndTransferRefuseTheGenericFactoryBecauseTheyHaveToNameWhatTheySpend()
    {
        Assert.Throws<ArgumentException>(() => StarGruntSteps.Simple(StarGruntAction.Fire));
        Assert.Throws<ArgumentException>(() => StarGruntSteps.Simple(StarGruntAction.TransferAction));
    }

    [Fact]
    public void LeaderActionsAreTheOnesTheLeaderTakesHimself()
    {
        Assert.True(StarGruntActions.IsLeaderAction(StarGruntAction.Observe));
        Assert.True(StarGruntActions.IsLeaderAction(StarGruntAction.TransferAction));
        Assert.False(StarGruntActions.IsLeaderAction(StarGruntAction.Move));
        Assert.False(StarGruntActions.IsLeaderAction(StarGruntAction.Fire));
    }
}
