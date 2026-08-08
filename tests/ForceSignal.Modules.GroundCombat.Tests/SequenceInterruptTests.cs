using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.GroundCombat.Tests;

/// <summary>
/// Interrupt windows, and the frame stack Dirtside needs on its own.
/// </summary>
public class SequenceInterruptTests
{
    private const string OpportunityFire = "opportunity-fire";
    private const string AreaDefence = "area-defence";

    private static readonly UnitId Mover = new("blue-1");
    private static readonly UnitId Interceptor = new("blue-2");
    private static readonly UnitId Ambusher = new("red-1");
    private static readonly UnitId Spare = new("red-2");

    private static readonly InterruptGeometry Midway =
        InterruptGeometry.At(new GroundPoint(14.0, 9.5, 0.0), "in-the-open");

    private static readonly InterruptGeometry Overhead =
        InterruptGeometry.At(new GroundPoint(20.0, 11.0, 4.0), "airborne");

    /// <summary>
    /// Moving triggers opportunity fire against the mover; launching triggers area-defence
    /// interception against the launch. Nothing here knows either game - the kinds are just strings.
    /// </summary>
    private static readonly ScriptedActivationPolicy Nesting = new()
    {
        TriggerRule = (_, _, step) => step.Kind switch
        {
            "move" => SequenceFixtures.Window(OpportunityFire, SequenceFixtures.Red, Midway, Ambusher),
            "launch" => SequenceFixtures.Window(AreaDefence, SequenceFixtures.Blue, Overhead, Interceptor),
            _ => null,
        },
    };

    /// <summary>
    /// Dirtside reaches three frames on its own: a mover interrupted by opportunity fire, and that
    /// fire interrupted in turn by interception of the missile it just launched. No StarGrunt
    /// anywhere in it, which is what makes the stack shared-layer work rather than a StarGrunt
    /// concession.
    /// </summary>
    [Fact]
    public void AnInterruptInsideAnInterruptReachesThreeFramesAndUnwindsCleanly()
    {
        var session = Moving();
        Assert.Equal(1, session.Depth);

        var opportunity = session.AwaitingAnswer;
        Assert.NotNull(opportunity);
        Assert.Equal(OpportunityFire, opportunity.Kind);

        // Opportunity fire spends the firing unit's whole activation for the turn.
        var reacting = GroundCombatSequence.DeclareReaction(session, Ambusher, ReactionCost.ConsumesActivation);
        Assert.Equal(2, reacting.Depth);
        Assert.Null(reacting.AwaitingAnswer);

        var launched = GroundCombatSequence.TakeStep(reacting, ActivationStep.Of("launch"), Nesting);
        Assert.Equal(AreaDefence, launched.AwaitingAnswer!.Kind);

        // Area defence with live sensors intercepts without losing its own activation.
        var intercepting = GroundCombatSequence.DeclareReaction(launched, Interceptor, ReactionCost.None);
        Assert.Equal(3, intercepting.Depth);
        Assert.Equal(Interceptor, intercepting.CurrentFrame!.Unit);

        var interceptDone = GroundCombatSequence.EndFrame(intercepting, Nesting);
        Assert.Equal(2, interceptDone.Depth);

        // The inner window has closed; the outer one has not, because its responder is still firing.
        Assert.Equal(OpportunityFire, Assert.Single(interceptDone.WindowStack).Kind);
        Assert.DoesNotContain(Interceptor, interceptDone.Side(SequenceFixtures.Blue).Activated);

        var opportunityDone = GroundCombatSequence.EndFrame(interceptDone, Nesting);
        Assert.Equal(1, opportunityDone.Depth);
        Assert.Empty(opportunityDone.WindowStack);
        Assert.Contains(Ambusher, opportunityDone.Side(SequenceFixtures.Red).Activated);

        // The suspended mover resumes exactly where it was, step history intact.
        Assert.Equal(Mover, opportunityDone.CurrentFrame!.Unit);
        Assert.Equal("move", Assert.Single(opportunityDone.CurrentFrame.Steps).Kind);
        Assert.Null(opportunityDone.AwaitingAnswer);

        var finished = GroundCombatSequence.EndFrame(
            GroundCombatSequence.TakeStep(opportunityDone, ActivationStep.Of("fire"), Nesting),
            Nesting);

        Assert.True(finished.IsIdle);
        Assert.Equal(SequenceFixtures.Red, finished.ActiveSide);
    }

    [Fact]
    public void ASuspendedFrameCannotActWhileItsWindowIsOpen()
    {
        var session = Moving();

        Assert.False(GroundCombatSequence.CanTakeStep(session, ActivationStep.Of("fire"), Nesting).IsAllowed);
        Assert.False(GroundCombatSequence.CanEndFrame(session, Nesting).IsAllowed);
    }

    [Fact]
    public void DecliningClosesTheWindowAndResumesTheMover()
    {
        var session = Moving();

        var declined = GroundCombatSequence.DeclineReaction(session, Ambusher);

        Assert.Empty(declined.WindowStack);
        Assert.Equal(Mover, declined.CurrentFrame!.Unit);
        Assert.DoesNotContain(Ambusher, declined.Side(SequenceFixtures.Red).Activated);
        Assert.True(GroundCombatSequence.CanTakeStep(declined, ActivationStep.Of("fire"), Nesting).IsAllowed);
    }

    [Fact]
    public void OnlyTheNamedRespondersMayAnswer()
    {
        var session = Moving();

        Assert.False(GroundCombatSequence.CanDeclareReaction(session, Spare, ReactionCost.None).IsAllowed);
        Assert.False(GroundCombatSequence.CanDeclineReaction(session, Spare).IsAllowed);
    }

    [Fact]
    public void AResponderThatHasAlreadyActivatedCannotSpendItsActivationAgain()
    {
        var session = Moving();
        var spent = session.WithSideForTest(SequenceFixtures.Red, side => side with { Activated = side.Activated.Add(Ambusher) });

        // A standing reaction is still fine; one that costs an activation is not.
        Assert.True(GroundCombatSequence.CanDeclareReaction(spent, Ambusher, ReactionCost.None).IsAllowed);
        Assert.False(
            GroundCombatSequence.CanDeclareReaction(spent, Ambusher, ReactionCost.ConsumesActivation).IsAllowed);
    }

    /// <summary>
    /// The structural half of what bounds nesting. Refusing the window rather than the step matters:
    /// the move that would have triggered it is still perfectly legal.
    /// </summary>
    [Fact]
    public void AReactionCannotRetriggerItsOwnKind()
    {
        var alwaysOpportunity = new ScriptedActivationPolicy
        {
            TriggerRule = (_, _, _) =>
                SequenceFixtures.Window(OpportunityFire, SequenceFixtures.Red, Midway, Spare),
        };

        var session = GroundCombatSequence.TakeStep(
            GroundCombatSequence.BeginActivation(
                SequenceFixtures.TurnUnderWay(Forces(), SequenceFixtures.Blue), SequenceFixtures.Blue, Mover),
            ActivationStep.Of("move"),
            alwaysOpportunity);
        Assert.Single(session.WindowStack);

        var reacting = GroundCombatSequence.DeclareReaction(session, Spare, ReactionCost.ConsumesActivation);
        var fired = GroundCombatSequence.TakeStep(reacting, ActivationStep.Of("fire"), alwaysOpportunity);

        Assert.Single(fired.WindowStack);
        Assert.Equal("fire", fired.CurrentFrame!.Steps[0].Kind);
    }

    /// <summary>
    /// StarGrunt's transferred activation is a third frame kind and nothing more. It costs the shared
    /// layer nothing because the stack already had to exist for Dirtside.
    /// </summary>
    [Fact]
    public void AGrantedActivationNestsInsideTheCommandersOwn()
    {
        var session = GroundCombatSequence.BeginActivation(
            SequenceFixtures.TurnUnderWay(Forces(), SequenceFixtures.Blue), SequenceFixtures.Blue, Mover);

        var granted = GroundCombatSequence.GrantActivation(session, Interceptor);

        Assert.Equal(2, granted.Depth);
        Assert.Equal(FrameKind.Granted, granted.CurrentFrame!.Kind);

        var back = GroundCombatSequence.EndFrame(granted, Nesting);

        Assert.Equal(1, back.Depth);
        Assert.Contains(Interceptor, back.Side(SequenceFixtures.Blue).Activated);
        Assert.Equal(SequenceFixtures.Blue, back.ActiveSide);
    }

    [Fact]
    public void AnAlreadySpentUnitIsExactlyWhoATransferredActivationIsFor()
    {
        var session = GroundCombatSequence.BeginActivation(
            SequenceFixtures.TurnUnderWay(Forces(), SequenceFixtures.Blue), SequenceFixtures.Blue, Mover);
        var spent = session.WithSideForTest(
            SequenceFixtures.Blue, side => side with { Activated = side.Activated.Add(Interceptor) });

        Assert.True(GroundCombatSequence.CanGrantActivation(spent, Interceptor).IsAllowed);
        Assert.False(GroundCombatSequence.CanGrantActivation(spent, Mover).IsAllowed);
    }

    /// <summary>
    /// The ceiling is an invariant check, not a rule, so breaching it throws rather than politely
    /// refusing. If this ever fires in anger the bug is in one of the three things that actually
    /// bound nesting, and raising the number would hide it.
    /// </summary>
    [Fact]
    public void TheDepthCeilingIsATripwireThatThrows()
    {
        var session = GroundCombatSequence.BeginActivation(
            SequenceFixtures.TurnUnderWay(Forces(), SequenceFixtures.Blue), SequenceFixtures.Blue, Mover);

        while (session.Depth < GroundCombatSequence.NestingCeiling)
        {
            var next = session.CurrentFrame!.Unit == Mover ? Interceptor : Mover;
            session = GroundCombatSequence.GrantActivation(session, next);
        }

        var atCeiling = session;
        var oneTooMany = atCeiling.CurrentFrame!.Unit == Mover ? Interceptor : Mover;

        Assert.Throws<InvalidOperationException>(
            () => GroundCombatSequence.GrantActivation(atCeiling, oneTooMany));
    }

    /// <summary>
    /// StarGrunt's reaction fire costs more than Dirtside's opportunity fire: as well as the firing
    /// unit's activation it counts as that player's next go, so play returns to the mover rather than
    /// passing over. That extra bit rides on the declaration, not on the window.
    /// </summary>
    [Fact]
    public void AReactionThatCostsPriorityMakesTheSideGiveUpItsNextGo()
    {
        var reacted = GroundCombatSequence.EndFrame(
            GroundCombatSequence.DeclareReaction(
                Moving(), Ambusher, ReactionCost.ConsumesActivation | ReactionCost.ForfeitsNextPrioritySlot),
            Nesting);
        var moverDone = GroundCombatSequence.EndFrame(reacted, Nesting);

        Assert.Equal(SequenceFixtures.Red, moverDone.ActiveSide);
        Assert.Equal(1, moverDone.Side(SequenceFixtures.Red).ForfeitedSlots);

        // Red cannot simply act; the go it owes has to be given up first.
        Assert.False(GroundCombatSequence.CanBeginActivation(moverDone, SequenceFixtures.Red, Spare).IsAllowed);
        Assert.False(GroundCombatSequence.CanPass(moverDone, SequenceFixtures.Red).IsAllowed);

        var forfeited = GroundCombatSequence.ForfeitPriority(moverDone, SequenceFixtures.Red);

        Assert.Equal(SequenceFixtures.Blue, forfeited.ActiveSide);
        Assert.Equal(0, forfeited.Side(SequenceFixtures.Red).ForfeitedSlots);
        Assert.Empty(forfeited.ConsecutivePasses);
        Assert.True(
            GroundCombatSequence.CanBeginActivation(forfeited, SequenceFixtures.Blue, Interceptor).IsAllowed);
    }

    [Fact]
    public void ADebtFromReactingIsClearedByTheTurnEndReset()
    {
        var owing = Moving().WithSideForTest(
            SequenceFixtures.Red, side => side with { ForfeitedPriority = [Ambusher] });

        var next = GroundCombatSequence.BeginTurn(owing with { Phase = TurnPhase.TurnEnded });

        Assert.Equal(0, next.Side(SequenceFixtures.Red).ForfeitedSlots);
        Assert.Empty(next.FrameStack);
        Assert.Empty(next.WindowStack);
    }

    private static GroundCombatSession Forces() =>
        GroundCombatSession.Start(
            SideState.Of(SequenceFixtures.Blue, [Mover, Interceptor]),
            SideState.Of(SequenceFixtures.Red, [Ambusher, Spare]));

    /// <summary>Blue's mover part-way through a move, with the opportunity-fire window open.</summary>
    private static GroundCombatSession Moving() =>
        GroundCombatSequence.TakeStep(
            GroundCombatSequence.BeginActivation(
                SequenceFixtures.TurnUnderWay(Forces(), SequenceFixtures.Blue), SequenceFixtures.Blue, Mover),
            ActivationStep.Of("move"),
            Nesting);
}
