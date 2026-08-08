using System.Collections.Immutable;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.Dirtside.Sequence;
using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>Two platoons a side, each of two elements, and a turn ready to be played.</summary>
internal static class DirtsideFixtures
{
    public static readonly SideId Blue = new("blue");
    public static readonly SideId Red = new("red");

    public static readonly UnitId Platoon = new("blue-1");
    public static readonly UnitId Support = new("blue-2");
    public static readonly UnitId Watcher = new("red-1");
    public static readonly UnitId Battery = new("red-2");

    public static readonly ElementId First = new("1");
    public static readonly ElementId Second = new("2");

    public static GroundCombatSession Forces() =>
        GroundCombatSession.Start(
            SideState.Of(Blue, [Platoon, Support]),
            SideState.Of(Red, [Watcher, Battery]));

    public static DirtsideBoard Board() =>
        new DirtsideBoard()
            .SetElements(Platoon, [First, Second])
            .SetElements(Support, [First])
            .SetElements(Watcher, [First])
            .SetElements(Battery, [First]);

    public static GroundCombatSession BlueToPlay()
    {
        var begun = GroundCombatSequence.BeginTurn(Forces());
        var chooser = SequenceGuards.FirstActivationChooser(begun) ?? Blue;
        return GroundCombatSequence.ChooseFirstActivator(begun, chooser, takeIt: chooser == Blue);
    }

    public static GroundCombatSession Activating(UnitId unit = default) =>
        GroundCombatSequence.BeginActivation(
            BlueToPlay(), Blue, unit == default ? Platoon : unit);

    public static InterruptOpening Seen(params UnitId[] watchers) =>
        new(new GroundPoint(1, 2, 0), [.. watchers], ImmutableArray<string>.Empty);
}

/// <summary>
/// Dirtside's activation policy. The unit is a platoon, its elements decide independently, and the
/// two interrupts cost opposite amounts.
/// </summary>
public sealed class DirtsideSequenceTests
{
    private static readonly ElementId First = DirtsideFixtures.First;
    private static readonly ElementId Second = DirtsideFixtures.Second;

    [Fact]
    public void EachElementGetsItsOwnMoveAndItsOwnCombatAction()
    {
        // No unit-level budget anywhere. Both elements move and both fire, which would be four
        // actions and twice the allowance in a game that counted them.
        var policy = new DirtsideActivationPolicy(DirtsideFixtures.Board());
        var session = DirtsideFixtures.Activating();

        foreach (var step in new[]
        {
            DirtsideSteps.Move(First),
            DirtsideSteps.Fire(First, "main"),
            DirtsideSteps.Move(Second),
            DirtsideSteps.Fire(Second, "main"),
        })
        {
            Assert.True(GroundCombatSequence.CanTakeStep(session, step, policy).IsAllowed);
            session = GroundCombatSequence.TakeStep(session, step, policy);
        }

        Assert.Equal(4, session.CurrentFrame!.Steps.Length);
    }

    [Fact]
    public void AnElementGetsOneCombatActionAndOneMoveAndNoMore()
    {
        var policy = new DirtsideActivationPolicy(DirtsideFixtures.Board());
        var session = GroundCombatSequence.TakeStep(
            DirtsideFixtures.Activating(), DirtsideSteps.Fire(First, "main"), policy);

        // The one-weapon-per-combat-action rule needs no rule of its own: an element with one action
        // has one chance to name a system.
        Assert.False(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Fire(First, "secondary"), policy).IsAllowed);
        Assert.False(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Act(DirtsideAction.CloseAssault, First), policy)
            .IsAllowed);

        // ...but the other element's action is untouched, which is the whole point of the scoping.
        Assert.True(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Fire(Second, "main"), policy).IsAllowed);

        session = GroundCombatSequence.TakeStep(session, DirtsideSteps.Move(First), policy);
        Assert.False(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Move(First), policy).IsAllowed);
    }

    [Fact]
    public void AStepThatNamesNoElementHasNoRule()
    {
        // The unit is a platoon. A step belonging to nobody in particular is not something the rules
        // have an answer for, so it is refused rather than applied to the whole unit.
        var policy = new DirtsideActivationPolicy(DirtsideFixtures.Board());
        var orphan = ActivationStep.Of(DirtsideSteps.Name(DirtsideAction.Move));

        var check = GroundCombatSequence.CanTakeStep(DirtsideFixtures.Activating(), orphan, policy);

        Assert.False(check.IsAllowed);
        Assert.Contains("element", check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFixedMountFiresBeforeOrInsteadOfMovingAndNeverAfter()
    {
        // The only rule in the game that reads the order the steps were taken in rather than just
        // which of them were.
        var board = DirtsideFixtures.Board()
            .SetFixedMount(DirtsideFixtures.Platoon, First, "hull-gun");
        var policy = new DirtsideActivationPolicy(board);

        var beforeMoving = DirtsideFixtures.Activating();
        Assert.True(GroundCombatSequence
            .CanTakeStep(beforeMoving, DirtsideSteps.Fire(First, "hull-gun"), policy).IsAllowed);

        var afterMoving = GroundCombatSequence.TakeStep(
            beforeMoving, DirtsideSteps.Move(First), policy);

        Assert.False(GroundCombatSequence
            .CanTakeStep(afterMoving, DirtsideSteps.Fire(First, "hull-gun"), policy).IsAllowed);

        // A weapon on a traverse is unaffected, and so is the element that did not move.
        Assert.True(GroundCombatSequence
            .CanTakeStep(afterMoving, DirtsideSteps.Fire(First, "turret"), policy).IsAllowed);
        Assert.True(GroundCombatSequence
            .CanTakeStep(afterMoving, DirtsideSteps.Fire(Second, "hull-gun"), policy).IsAllowed);
    }

    [Fact]
    public void AnActivationIsFinishedWhenEveryElementHasSaidWhatItIsDoing()
    {
        // Including the ones doing nothing. An element that sits out has given up its whole turn, so
        // the declaration is a decision with teeth rather than bookkeeping.
        var policy = new DirtsideActivationPolicy(DirtsideFixtures.Board());
        var session = GroundCombatSequence.TakeStep(
            DirtsideFixtures.Activating(), DirtsideSteps.Move(First), policy);

        Assert.False(GroundCombatSequence.CanEndFrame(session, policy).IsAllowed);
        Assert.Equal([Second], policy.StillToChoose(session.CurrentFrame!));

        session = GroundCombatSequence.TakeStep(session, DirtsideSteps.StandDown(Second), policy);

        Assert.True(GroundCombatSequence.CanEndFrame(session, policy).IsAllowed);
        Assert.Empty(policy.StillToChoose(session.CurrentFrame!));
    }

    [Fact]
    public void AnElementThatStoodDownIsOutForTheTurn()
    {
        var policy = new DirtsideActivationPolicy(DirtsideFixtures.Board());
        var session = GroundCombatSequence.TakeStep(
            DirtsideFixtures.Activating(), DirtsideSteps.StandDown(First), policy);

        Assert.False(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Move(First), policy).IsAllowed);
        Assert.False(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Fire(First, "main"), policy).IsAllowed);
    }

    [Fact]
    public void AnElementThatHasAlreadyActedCannotThenDeclareItDidNothing()
    {
        var policy = new DirtsideActivationPolicy(DirtsideFixtures.Board());
        var session = GroundCombatSequence.TakeStep(
            DirtsideFixtures.Activating(), DirtsideSteps.Move(First), policy);

        Assert.False(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.StandDown(First), policy).IsAllowed);
    }

    [Fact]
    public void OpportunityFireInterruptsAMoverAndCostsTheInterrupterItsWholeActivation()
    {
        var board = DirtsideFixtures.Board()
            .SetOpportunityFireOpening(
                DirtsideFixtures.Platoon, First, DirtsideFixtures.Seen(DirtsideFixtures.Watcher));
        var policy = new DirtsideActivationPolicy(board);

        var moving = GroundCombatSequence.TakeStep(
            DirtsideFixtures.Activating(), DirtsideSteps.Move(First), policy);

        var window = moving.AwaitingAnswer;
        Assert.NotNull(window);
        Assert.Equal(DirtsideActivationPolicy.OpportunityFireWindow, window.Kind);

        var reacting = DirtsideTurn.ReactWithOpportunityFire(moving, DirtsideFixtures.Watcher);
        var fired = GroundCombatSequence.TakeStep(
            reacting, DirtsideSteps.Fire(First, "main"), policy);
        var resumed = GroundCombatSequence.EndFrame(fired, policy);

        // Its marker is face-down for the rest of the turn, even though only one element fired.
        Assert.DoesNotContain(
            DirtsideFixtures.Watcher, resumed.Side(DirtsideFixtures.Red).Unactivated);

        // And unlike the other game's reaction fire, it costs the side nothing further: play comes
        // straight back to the mover with no go owed.
        Assert.Equal(0, resumed.Side(DirtsideFixtures.Red).ForfeitedSlots);
        Assert.Equal(DirtsideFixtures.Platoon, resumed.CurrentFrame!.Unit);
    }

    [Fact]
    public void AUnitThatHasAlreadyGoneCannotOpportunityFire()
    {
        // The trade is an activation for an interruption, and a unit with nothing left has nothing
        // to trade. Eligibility is frozen at the moment of the trigger.
        var board = DirtsideFixtures.Board()
            .SetOpportunityFireOpening(
                DirtsideFixtures.Platoon, First, DirtsideFixtures.Seen(DirtsideFixtures.Watcher));
        var policy = new DirtsideActivationPolicy(board);

        var spent = GroundCombatSequence.SpendActivationOutOfSequence(
            DirtsideFixtures.Activating(), DirtsideFixtures.Red, DirtsideFixtures.Watcher);

        var moving = GroundCombatSequence.TakeStep(spent, DirtsideSteps.Move(First), policy);

        Assert.Null(moving.AwaitingAnswer);
    }

    [Fact]
    public void InterceptionIsFreeAndAnAlreadyActivatedUnitMayStillDoIt()
    {
        // The free reaction, and the reason the cost rides on the declaration rather than the window.
        // Live sensors were bought earlier with a combat action; answering costs nothing now.
        var board = DirtsideFixtures.Board()
            .SetInterceptionOpening(
                DirtsideFixtures.Platoon,
                First,
                "missiles",
                DirtsideFixtures.Seen(DirtsideFixtures.Battery));
        var policy = new DirtsideActivationPolicy(board);

        var spent = GroundCombatSequence.SpendActivationOutOfSequence(
            DirtsideFixtures.Activating(), DirtsideFixtures.Red, DirtsideFixtures.Battery);

        var launched = GroundCombatSequence.TakeStep(
            spent, DirtsideSteps.Fire(First, "missiles"), policy);

        Assert.Equal(DirtsideActivationPolicy.AreaDefenceWindow, launched.AwaitingAnswer!.Kind);

        var intercepting = DirtsideTurn.InterceptWithAreaDefence(launched, DirtsideFixtures.Battery);
        var shot = GroundCombatSequence.TakeStep(
            intercepting, DirtsideSteps.Fire(First, "ads"), policy);
        var resumed = GroundCombatSequence.EndFrame(shot, policy);

        Assert.Equal(0, resumed.Side(DirtsideFixtures.Red).ForfeitedSlots);
        Assert.Equal(DirtsideFixtures.Platoon, resumed.CurrentFrame!.Unit);
    }

    [Fact]
    public void AnInterruptCanBeInterrupted()
    {
        // The nesting the shared layer's frame stack was justified by, and it happens without either
        // game's special cases: a mover is caught by opportunity fire, and the missile that fire
        // launches is caught by an area-defence system.
        var board = DirtsideFixtures.Board()
            .SetOpportunityFireOpening(
                DirtsideFixtures.Platoon, First, DirtsideFixtures.Seen(DirtsideFixtures.Watcher))
            .SetInterceptionOpening(
                DirtsideFixtures.Watcher,
                First,
                "missiles",
                DirtsideFixtures.Seen(DirtsideFixtures.Support));
        var policy = new DirtsideActivationPolicy(board);

        var moving = GroundCombatSequence.TakeStep(
            DirtsideFixtures.Activating(), DirtsideSteps.Move(First), policy);
        var reacting = DirtsideTurn.ReactWithOpportunityFire(moving, DirtsideFixtures.Watcher);
        var launched = GroundCombatSequence.TakeStep(
            reacting, DirtsideSteps.Fire(First, "missiles"), policy);

        Assert.Equal(2, launched.Depth);
        Assert.Equal(DirtsideActivationPolicy.AreaDefenceWindow, launched.AwaitingAnswer!.Kind);

        var intercepting = DirtsideTurn.InterceptWithAreaDefence(launched, DirtsideFixtures.Support);
        Assert.Equal(3, intercepting.Depth);
    }

    [Fact]
    public void BeingCloseAssaultedTurnsAUnitsMarkerOverWhereItStands()
    {
        // It gets no frame, because it is not acting - it is simply spent. And it is idempotent, so a
        // unit that had already gone is left exactly as it was.
        var session = DirtsideFixtures.BlueToPlay();

        var spent = DirtsideTurn.CloseAssaulted(
            session, DirtsideFixtures.Red, DirtsideFixtures.Watcher);

        Assert.DoesNotContain(DirtsideFixtures.Watcher, spent.Side(DirtsideFixtures.Red).Unactivated);
        Assert.True(spent.IsIdle);
        Assert.Equal(spent, DirtsideTurn.CloseAssaulted(
            spent, DirtsideFixtures.Red, DirtsideFixtures.Watcher));
    }

    [Fact]
    public void AUnitUnderFireOwesAReactionTestBeforeItMovesAtAll()
    {
        // Any move, not only an advance - which is what separates this from the confidence gate.
        var board = DirtsideFixtures.Board()
            .Set(DirtsideFixtures.Platoon, new DirtsideUnitState { IsUnderFire = true });
        var policy = new DirtsideActivationPolicy(board);
        var session = DirtsideFixtures.Activating();

        Assert.False(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Move(First), policy).IsAllowed);

        // Firing is untouched by the marker.
        Assert.True(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Fire(First, "main"), policy).IsAllowed);

        board.Update(DirtsideFixtures.Platoon, state => state with { ReactionTestCleared = true });
        Assert.True(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Move(First), policy).IsAllowed);
    }

    [Fact]
    public void ShakenArmourNeedsTalkingOutOfCoverAndWillNotCharge()
    {
        var board = DirtsideFixtures.Board().Set(
            DirtsideFixtures.Platoon,
            new DirtsideUnitState
            {
                Kind = DirtsideUnitKind.Armour,
                Confidence = ConfidenceLevel.Shaken,
                NextMoveAdvancesOnTheEnemy = true,
            });
        var policy = new DirtsideActivationPolicy(board);
        var session = DirtsideFixtures.Activating();

        Assert.False(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Move(First), policy).IsAllowed);
        Assert.False(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Act(DirtsideAction.CloseAssault, First), policy)
            .IsAllowed);

        // A move that is not an advance was never in question.
        board.Update(
            DirtsideFixtures.Platoon, state => state with { NextMoveAdvancesOnTheEnemy = false });
        Assert.True(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Move(First), policy).IsAllowed);
    }

    [Fact]
    public void BrokenInfantryWillNotAdvanceOnAnyRoll()
    {
        var board = DirtsideFixtures.Board().Set(
            DirtsideFixtures.Platoon,
            new DirtsideUnitState
            {
                Kind = DirtsideUnitKind.DismountedInfantry,
                Confidence = ConfidenceLevel.Broken,
                NextMoveAdvancesOnTheEnemy = true,
                ReactionTestCleared = true,
            });
        var policy = new DirtsideActivationPolicy(board);

        // No test gets it forward, so a cleared one changes nothing - but it is still shooting.
        Assert.False(GroundCombatSequence
            .CanTakeStep(DirtsideFixtures.Activating(), DirtsideSteps.Move(First), policy).IsAllowed);
        Assert.True(GroundCombatSequence
            .CanTakeStep(DirtsideFixtures.Activating(), DirtsideSteps.Fire(First, "rifles"), policy)
            .IsAllowed);
    }

    [Fact]
    public void ARoutedUnitFiresAtNothing()
    {
        var board = DirtsideFixtures.Board().Set(
            DirtsideFixtures.Platoon,
            new DirtsideUnitState { Confidence = ConfidenceLevel.Routed });
        var policy = new DirtsideActivationPolicy(board);

        Assert.False(GroundCombatSequence
            .CanTakeStep(DirtsideFixtures.Activating(), DirtsideSteps.Fire(First, "main"), policy)
            .IsAllowed);
    }

    [Fact]
    public void ACybertankIgnoresEveryOneOfThoseGates()
    {
        // Not a unit with unshakeable morale - a unit with none. There is no confidence marker to
        // read, so there is nothing for any of these rules to consult.
        var board = DirtsideFixtures.Board().Set(
            DirtsideFixtures.Platoon,
            new DirtsideUnitState
            {
                IsCybertank = true,
                Kind = DirtsideUnitKind.Armour,
                Confidence = ConfidenceLevel.Routed,
                IsUnderFire = true,
                NextMoveAdvancesOnTheEnemy = true,
            });
        var policy = new DirtsideActivationPolicy(board);
        var session = DirtsideFixtures.Activating();

        Assert.True(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Move(First), policy).IsAllowed);
        Assert.True(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Fire(First, "main"), policy).IsAllowed);
        Assert.True(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Act(DirtsideAction.CloseAssault, First), policy)
            .IsAllowed);
    }

    [Fact]
    public void ADisorganisedUnitMayOnlyMoveToCloseItsRanks()
    {
        var board = DirtsideFixtures.Board()
            .Set(DirtsideFixtures.Platoon, new DirtsideUnitState { IsDisorganised = true });
        var policy = new DirtsideActivationPolicy(board);
        var session = DirtsideFixtures.Activating();

        Assert.True(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Move(First), policy).IsAllowed);
        Assert.False(GroundCombatSequence
            .CanTakeStep(session, DirtsideSteps.Fire(First, "main"), policy).IsAllowed);
    }

    [Fact]
    public void ASuspendedActivationSurvivesBeingSavedAndRestored()
    {
        // The interrupt geometry is the subtle half of this: a mover caught part-way is resolved
        // against a point it stands at neither end of its move.
        var board = DirtsideFixtures.Board()
            .SetOpportunityFireOpening(
                DirtsideFixtures.Platoon, First, DirtsideFixtures.Seen(DirtsideFixtures.Watcher));
        var policy = new DirtsideActivationPolicy(board);

        var moving = GroundCombatSequence.TakeStep(
            DirtsideFixtures.Activating(), DirtsideSteps.Move(First), policy);

        var restored = SessionSerialization.Restore(SessionSerialization.Save(moving));

        Assert.Equal(moving, restored);
        Assert.Equal(
            new GroundPoint(1, 2, 0), restored.AwaitingAnswer!.Geometry.ResolutionPoint);
    }

    [Fact]
    public void AStepFromTheOtherGameIsRefusedRatherThanMisread()
    {
        var policy = new DirtsideActivationPolicy(DirtsideFixtures.Board());
        var foreign = ActivationStep.Of("Dash", First);

        Assert.False(GroundCombatSequence
            .CanTakeStep(DirtsideFixtures.Activating(), foreign, policy).IsAllowed);
    }

    [Fact]
    public void AWeaponlessFireStepIsRefused()
    {
        var policy = new DirtsideActivationPolicy(DirtsideFixtures.Board());
        var unnamed = ActivationStep.Of(DirtsideSteps.Name(DirtsideAction.DirectFire), First);

        Assert.False(GroundCombatSequence
            .CanTakeStep(DirtsideFixtures.Activating(), unnamed, policy).IsAllowed);

        // And the factory will not build one in the first place.
        Assert.Throws<ArgumentException>(() => DirtsideSteps.Fire(First, "  "));
        Assert.Throws<ArgumentException>(() =>
            DirtsideSteps.Act(DirtsideAction.DirectFire, First));
        Assert.Throws<ArgumentException>(() => DirtsideSteps.Act(DirtsideAction.Move, First));
    }

    [Fact]
    public void ThePolicyRefusesToBeBuiltWithoutABoard() =>
        Assert.Throws<ArgumentNullException>(() => new DirtsideActivationPolicy(null!));
}
