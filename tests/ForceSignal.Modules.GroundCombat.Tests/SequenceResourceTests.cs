using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.GroundCombat.Tests;

/// <summary>
/// What has been spent is read off the steps, never stored.
/// </summary>
/// <remarks>
/// The rule being protected here is one of GZG's own corrections: a weapon's fire limit is per
/// <em>activation</em>, not per game turn. Working from the unrevised text gets it wrong, and getting
/// it wrong only shows up once a unit gets a second activation - which is rare enough to survive
/// casual play and be badly wrong in a real game.
/// </remarks>
public class SequenceResourceTests
{
    private const string MainGun = "main-gun";
    private const string Coaxial = "coaxial";

    private static readonly UnitId Tank = new("blue-1");
    private static readonly UnitId Commander = new("blue-2");

    private static readonly ScriptedActivationPolicy OncePerFrame = new()
    {
        StepRule = ScriptedActivationPolicy.OncePerFrame,
    };

    [Fact]
    public void AWeaponCannotFireTwiceInOneFrame()
    {
        var firing = Firing();

        Assert.False(
            GroundCombatSequence.CanTakeStep(firing, ActivationStep.Of("fire", null, MainGun), OncePerFrame)
                .IsAllowed);

        // A different weapon is untouched by the limit.
        Assert.True(
            GroundCombatSequence.CanTakeStep(firing, ActivationStep.Of("fire", null, Coaxial), OncePerFrame)
                .IsAllowed);
    }

    /// <summary>
    /// The whole point. A transferred activation is a new frame, and because the limit is read off the
    /// frame's own steps there is nowhere for the earlier shot to have been remembered.
    /// </summary>
    [Fact]
    public void TheLimitResetsWithTheFrameRatherThanWithTheTurn()
    {
        var firing = Firing();
        Assert.Contains(MainGun, firing.CurrentFrame!.ResourcesSpent);

        var spent = GroundCombatSequence.EndFrame(firing, OncePerFrame);
        Assert.Contains(Tank, spent.Side(SequenceFixtures.Blue).Activated);

        // Red goes, then Blue's commander activates and hands the tank a second activation. Same turn.
        var backToBlue = GroundCombatSequence.EndFrame(
            GroundCombatSequence.BeginActivation(spent, SequenceFixtures.Red, new UnitId("red-1")),
            OncePerFrame);
        var granting = GroundCombatSequence.GrantActivation(
            GroundCombatSequence.BeginActivation(backToBlue, SequenceFixtures.Blue, Commander),
            Tank);

        Assert.Equal(1, granting.TurnNumber);
        Assert.Empty(granting.CurrentFrame!.ResourcesSpent);
        Assert.True(
            GroundCombatSequence.CanTakeStep(granting, ActivationStep.Of("fire", null, MainGun), OncePerFrame)
                .IsAllowed);

        var firedAgain = GroundCombatSequence.TakeStep(
            granting, ActivationStep.Of("fire", null, MainGun), OncePerFrame);

        Assert.Contains(MainGun, firedAgain.CurrentFrame!.ResourcesSpent);

        // The commander's own frame, one below, is unaffected - spending is per frame both ways.
        Assert.Empty(firedAgain.FrameStack[^2].ResourcesSpent);
    }

    [Fact]
    public void ReactingInsideSomebodyElsesFrameIsItsOwnBudget()
    {
        var reactive = new ScriptedActivationPolicy
        {
            StepRule = ScriptedActivationPolicy.OncePerFrame,
            TriggerRule = (_, _, step) => step.Kind == "move"
                ? SequenceFixtures.Window(
                    "opportunity-fire",
                    SequenceFixtures.Red,
                    InterruptGeometry.At(new GroundPoint(3.0, 3.0, 0.0)),
                    new UnitId("red-1"))
                : null,
        };

        var moving = GroundCombatSequence.TakeStep(
            Started(), ActivationStep.Of("move", null, MainGun), reactive);
        var reacting = GroundCombatSequence.DeclareReaction(
            moving, new UnitId("red-1"), ReactionCost.ConsumesActivation);

        // The reacting unit's frame starts empty even though the frame below it has spent the same name.
        Assert.Empty(reacting.CurrentFrame!.ResourcesSpent);
        Assert.True(
            GroundCombatSequence.CanTakeStep(reacting, ActivationStep.Of("fire", null, MainGun), reactive)
                .IsAllowed);
    }

    [Fact]
    public void StepsWithNothingToSpendCostNothing()
    {
        var moved = GroundCombatSequence.TakeStep(Started(), ActivationStep.Of("move"), OncePerFrame);

        Assert.Empty(moved.CurrentFrame!.ResourcesSpent);
        Assert.False(moved.CurrentFrame.HasSpent(MainGun));
    }

    [Fact]
    public void AStepNeedsAKind()
    {
        Assert.Throws<ArgumentException>(() => ActivationStep.Of(" "));
    }

    private static GroundCombatSession Started() =>
        GroundCombatSequence.BeginActivation(
            SequenceFixtures.TurnUnderWay(Forces(), SequenceFixtures.Blue), SequenceFixtures.Blue, Tank);

    private static GroundCombatSession Firing() =>
        GroundCombatSequence.TakeStep(Started(), ActivationStep.Of("fire", null, MainGun), OncePerFrame);

    private static GroundCombatSession Forces() =>
        GroundCombatSession.Start(
            SideState.Of(SequenceFixtures.Blue, [Tank, Commander]),
            SideState.Of(SequenceFixtures.Red, [new UnitId("red-1"), new UnitId("red-2")]));
}
