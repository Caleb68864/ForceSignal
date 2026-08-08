using System.Collections.Immutable;

namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>
/// Every legal move from one <see cref="GroundCombatSession"/> to the next.
/// </summary>
/// <remarks>
/// <para>
/// Each transition is a pure function of the session it is given, and each comes with a
/// <c>Can*</c> twin that returns a reason instead of throwing. Nothing here mutates anything, so a
/// caller may hold on to any session it has ever seen - which is what makes undo, replay and an
/// after-action log fall out of the design rather than have to be built.
/// </para>
/// <para>
/// The verbs throw when applied to a session their twin would refuse. That is the same bargain the
/// combat code strikes: asking and committing are separate acts, and committing to a move you were
/// told was illegal is a programming error rather than a game event.
/// </para>
/// </remarks>
public static class GroundCombatSequence
{
    /// <summary>
    /// A tripwire, not a rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nesting is already bounded by three separate things, none of which is a depth limit: a
    /// commander has only so many actions to give away, transfers only travel down a chain of command
    /// with finitely many levels, and no reaction may retrigger its own kind - which is enforced
    /// structurally by <see cref="GroundCombatSession.IsWindowKindOpen"/> rather than counted.
    /// </para>
    /// <para>
    /// This ceiling exists so that a hole in any of those three announces itself instead of running
    /// away. It is an invariant check: if it ever fires, the bug is upstream of it and raising the
    /// number would be the wrong fix.
    /// </para>
    /// </remarks>
    public const int NestingCeiling = 8;

    /// <summary>Whether a new turn may begin.</summary>
    /// <param name="session">The session.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck CanBeginTurn(GroundCombatSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Phase is TurnPhase.NotStarted or TurnPhase.TurnEnded
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused($"A turn is already under way ({session.Phase}).");
    }

    /// <summary>
    /// Begins a turn: every marker face-up again, and the choice of who goes first reopened.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>The session at the top of the next turn.</returns>
    /// <remarks>
    /// The turn-end reset is the whole of this. Clearing the activated sets is what flips the markers;
    /// clearing the forfeited slots is what stops a reaction late in one turn from stealing a go in
    /// the next.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A turn is already under way.</exception>
    public static GroundCombatSession BeginTurn(GroundCombatSession session)
    {
        Require(CanBeginTurn(session));

        return session with
        {
            TurnNumber = session.TurnNumber + 1,
            Phase = TurnPhase.ChoosingFirstActivator,
            Sides =
            [
                .. session.Sides.Select(side => side with
                {
                    Activated = ImmutableHashSet<UnitId>.Empty,
                    ForfeitedPriority = ImmutableArray<UnitId>.Empty,
                }),
            ],
            FirstActivator = null,
            ActiveSide = null,
            FrameStack = ImmutableArray<ActivationFrame>.Empty,
            WindowStack = ImmutableArray<InterruptWindow>.Empty,
            ConsecutivePasses = ImmutableArray<SideId>.Empty,
        };
    }

    /// <summary>Whether this side may settle who activates first.</summary>
    /// <param name="session">The session.</param>
    /// <param name="chooser">The side claiming the choice.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck CanChooseFirstActivator(GroundCombatSession session, SideId chooser)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.Phase != TurnPhase.ChoosingFirstActivator)
        {
            return SequenceCheck.Refused("Who goes first is settled only at the top of a turn.");
        }

        var entitled = SequenceGuards.FirstActivationChooser(session);

        // A level count leaves the rules silent, so either side may carry the caller's die roll in.
        return entitled is null || entitled == chooser
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused($"{entitled} has fewer units on the table and so has the choice.");
    }

    /// <summary>Settles who takes the first activation this turn.</summary>
    /// <param name="session">The session.</param>
    /// <param name="chooser">The side making the choice.</param>
    /// <param name="takeIt">True to activate first, false to make the opponent go first.</param>
    /// <returns>The session with the alternation started.</returns>
    /// <exception cref="InvalidOperationException">This side does not have the choice.</exception>
    public static GroundCombatSession ChooseFirstActivator(
        GroundCombatSession session,
        SideId chooser,
        bool takeIt)
    {
        Require(CanChooseFirstActivator(session, chooser));

        var first = takeIt ? chooser : session.Opponent(chooser).Id;
        return session with
        {
            Phase = TurnPhase.Activating,
            FirstActivator = first,
            ActiveSide = first,
        };
    }

    /// <summary>Whether this side may open an activation with this unit.</summary>
    /// <param name="session">The session.</param>
    /// <param name="side">The side wanting to activate.</param>
    /// <param name="unit">The unit to activate.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck CanBeginActivation(GroundCombatSession session, SideId side, UnitId unit)
    {
        ArgumentNullException.ThrowIfNull(session);

        var turn = CanTakeATurn(session, side);
        if (!turn.IsAllowed)
        {
            return turn;
        }

        return session.Side(side).Unactivated.Contains(unit)
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused($"{unit} is not an unactivated unit of {side}.");
    }

    /// <summary>Activates a unit, pushing the frame it will act inside.</summary>
    /// <param name="session">The session.</param>
    /// <param name="side">The side activating.</param>
    /// <param name="unit">The unit being activated.</param>
    /// <returns>The session with the activation open.</returns>
    /// <exception cref="InvalidOperationException">The activation is not legal now.</exception>
    public static GroundCombatSession BeginActivation(
        GroundCombatSession session,
        SideId side,
        UnitId unit)
    {
        Require(CanBeginActivation(session, side, unit));

        // Somebody acting breaks any run of passes, so the turn no longer looks finished.
        return Push(
            session with { ConsecutivePasses = ImmutableArray<SideId>.Empty },
            FrameKind.Activation,
            side,
            unit,
            answeringWindow: null,
            cost: ReactionCost.None);
    }

    /// <summary>Whether this side owes a go it must give up before doing anything else.</summary>
    /// <param name="session">The session.</param>
    /// <param name="side">The side.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck CanForfeitPriority(GroundCombatSession session, SideId side)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.Phase != TurnPhase.Activating || !session.IsIdle)
        {
            return SequenceCheck.Refused("Priority is settled between activations, not inside one.");
        }

        if (session.ActiveSide != side)
        {
            return SequenceCheck.Refused($"It is not {side}'s go.");
        }

        return session.Side(side).ForfeitedSlots > 0
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused($"{side} owes no go.");
    }

    /// <summary>
    /// Gives up a go owed for a reaction that cost this side its priority.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="side">The side giving up its go.</param>
    /// <returns>The session with play back with the opponent.</returns>
    /// <remarks>
    /// This is not a pass. A pass is a choice with its own rule about when it is allowed; this is a
    /// debt already incurred by reacting, so it neither needs the pass guard nor counts towards the
    /// run of passes that ends a turn.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This side owes no go.</exception>
    public static GroundCombatSession ForfeitPriority(GroundCombatSession session, SideId side)
    {
        Require(CanForfeitPriority(session, side));

        var owing = session.Side(side);
        var settled = session.WithSide(owing with { ForfeitedPriority = owing.ForfeitedPriority.RemoveAt(0) });
        return HandOver(settled, side);
    }

    /// <summary>Whether the acting unit may take this step.</summary>
    /// <param name="session">The session.</param>
    /// <param name="step">The proposed step.</param>
    /// <param name="policy">The game's rules for what is legal.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static SequenceCheck CanTakeStep(
        GroundCombatSession session,
        ActivationStep step,
        IActivationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(policy);

        if (session.CurrentFrame is not { } frame)
        {
            return SequenceCheck.Refused("Nothing is activated.");
        }

        if (session.AwaitingAnswer is { } window)
        {
            return SequenceCheck.Refused(
                $"The {window.Kind} window has to be answered before play goes on.");
        }

        return policy.IsStepLegal(session, frame, step);
    }

    /// <summary>
    /// Records a step, and opens whatever window the step exposes the actor to.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="step">The step taken.</param>
    /// <param name="policy">The game's rules.</param>
    /// <returns>The session with the step recorded, possibly suspended by a new window.</returns>
    /// <exception cref="InvalidOperationException">The step is not legal now.</exception>
    public static GroundCombatSession TakeStep(
        GroundCombatSession session,
        ActivationStep step,
        IActivationPolicy policy)
    {
        Require(CanTakeStep(session, step, policy));

        var frame = session.CurrentFrame!;

        // The policy is asked before the step is recorded, so a trigger is described in terms of the
        // move being made rather than of a board that has already moved on.
        var request = policy.WindowOpenedBy(session, frame, step);

        var advanced = session with
        {
            FrameStack = session.FrameStack.SetItem(
                session.FrameStack.Length - 1,
                frame with { Steps = frame.Steps.Add(step) }),
        };

        return request is { } opening && MayOpenWindow(advanced, opening).IsAllowed
            ? OpenWindow(advanced, opening)
            : advanced;
    }

    /// <summary>
    /// Whether a window of this shape may be opened right now.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="request">The window the policy asked for.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <remarks>
    /// The kind check is the structural half of what bounds nesting: a reaction can never retrigger
    /// its own kind, so the same window may not open inside itself however the board looks. Refusing
    /// the window rather than the step matters - the triggering move is perfectly legal, it just does
    /// not give a second bite.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck MayOpenWindow(GroundCombatSession session, InterruptWindowRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.IsWindowKindOpen(request.Kind))
        {
            return SequenceCheck.Refused($"A {request.Kind} window is already open; it cannot retrigger itself.");
        }

        if (request.EligibleResponders.IsDefaultOrEmpty)
        {
            return SequenceCheck.Refused("Nobody is eligible to answer.");
        }

        return request.ResponderCap > 0
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused("A window that admits no responders is not a window.");
    }

    /// <summary>Whether this unit may answer the open window by reacting.</summary>
    /// <param name="session">The session.</param>
    /// <param name="responder">The would-be responder.</param>
    /// <param name="cost">What answering will cost it.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck CanDeclareReaction(
        GroundCombatSession session,
        UnitId responder,
        ReactionCost cost)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.AwaitingAnswer is not { } window)
        {
            return SequenceCheck.Refused("No window is waiting for an answer.");
        }

        if (!window.MayAnswer(responder))
        {
            return SequenceCheck.Refused($"{responder} is not an unanswered eligible responder.");
        }

        var side = session.Side(window.RespondingSide);

        // A reaction that spends an activation needs one left to spend. A standing reaction - a live
        // area-defence system - has no such need, which is exactly why the cost rides on the
        // declaration rather than on the window.
        return !cost.HasFlag(ReactionCost.ConsumesActivation) || side.Unactivated.Contains(responder)
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused($"{responder} has already used its activation and cannot spend it again.");
    }

    /// <summary>Answers the open window by reacting, pushing the reaction's own frame.</summary>
    /// <param name="session">The session.</param>
    /// <param name="responder">The reacting unit.</param>
    /// <param name="cost">What the reaction will cost when it finishes.</param>
    /// <returns>The session with the reaction under way.</returns>
    /// <exception cref="InvalidOperationException">The reaction is not legal now.</exception>
    public static GroundCombatSession DeclareReaction(
        GroundCombatSession session,
        UnitId responder,
        ReactionCost cost)
    {
        Require(CanDeclareReaction(session, responder, cost));

        var window = session.AwaitingAnswer!;
        return Push(session, FrameKind.Reaction, window.RespondingSide, responder, window.Id, cost);
    }

    /// <summary>Whether this unit may decline the open window.</summary>
    /// <param name="session">The session.</param>
    /// <param name="responder">The unit declining.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck CanDeclineReaction(GroundCombatSession session, UnitId responder)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.AwaitingAnswer is not { } window)
        {
            return SequenceCheck.Refused("No window is waiting for an answer.");
        }

        return window.MayAnswer(responder)
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused($"{responder} is not an unanswered eligible responder.");
    }

    /// <summary>Declines to react, and closes the window if that was the last answer wanted.</summary>
    /// <param name="session">The session.</param>
    /// <param name="responder">The unit declining.</param>
    /// <returns>The session, with the suspended frame resumed if the window is now closed.</returns>
    /// <exception cref="InvalidOperationException">The unit cannot answer this window.</exception>
    public static GroundCombatSession DeclineReaction(GroundCombatSession session, UnitId responder)
    {
        Require(CanDeclineReaction(session, responder));
        return SettleAnswer(session, responder);
    }

    /// <summary>Whether the current frame may be closed.</summary>
    /// <param name="session">The session.</param>
    /// <param name="policy">The game's rules.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static SequenceCheck CanEndFrame(GroundCombatSession session, IActivationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(policy);

        if (session.CurrentFrame is not { } frame)
        {
            return SequenceCheck.Refused("Nothing is activated.");
        }

        if (session.AwaitingAnswer is { } window)
        {
            return SequenceCheck.Refused(
                $"The {window.Kind} window has to be answered before this frame can close.");
        }

        return policy.IsFrameComplete(session, frame)
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused($"{frame.Unit} has not finished its {frame.Kind}.");
    }

    /// <summary>
    /// Closes the current frame, charges whatever it cost, and resumes whatever it suspended.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="policy">The game's rules.</param>
    /// <returns>The session one frame shallower.</returns>
    /// <exception cref="InvalidOperationException">The frame is not finished.</exception>
    public static GroundCombatSession EndFrame(GroundCombatSession session, IActivationPolicy policy)
    {
        Require(CanEndFrame(session, policy));

        var frame = session.CurrentFrame!;
        var popped = session with { FrameStack = session.FrameStack.RemoveAt(session.FrameStack.Length - 1) };

        return frame.Kind switch
        {
            FrameKind.Reaction => SettleAnswer(ChargeReaction(popped, frame), frame.Unit),
            FrameKind.Granted => MarkActivated(popped, frame.Side, frame.Unit),

            // Only the bottom of the stack hands play over. Everything above it is an interruption of
            // somebody's go, not a go of its own.
            _ => HandOver(MarkActivated(popped, frame.Side, frame.Unit), frame.Side),
        };
    }

    /// <summary>Whether a commander in the current frame may hand this unit a whole extra activation.</summary>
    /// <param name="session">The session.</param>
    /// <param name="beneficiary">The unit being given the activation.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck CanGrantActivation(GroundCombatSession session, UnitId beneficiary)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.CurrentFrame is not { } frame)
        {
            return SequenceCheck.Refused("Only an activated commander can transfer an activation.");
        }

        if (session.AwaitingAnswer is not null)
        {
            return SequenceCheck.Refused("An open interrupt window has to be answered first.");
        }

        if (frame.Unit == beneficiary)
        {
            return SequenceCheck.Refused("A unit cannot transfer an activation to itself.");
        }

        // Deliberately not gated on the beneficiary being unactivated: a transferred activation is
        // worth having precisely because it revives a unit that has already gone this turn.
        return session.Side(frame.Side).OnTable.Contains(beneficiary)
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused($"{beneficiary} is not a unit of {frame.Side} on the table.");
    }

    /// <summary>Hands a subordinate a whole extra activation, nested inside the commander's own.</summary>
    /// <param name="session">The session.</param>
    /// <param name="beneficiary">The unit being given the activation.</param>
    /// <returns>The session with the granted activation under way.</returns>
    /// <exception cref="InvalidOperationException">The transfer is not legal now.</exception>
    public static GroundCombatSession GrantActivation(GroundCombatSession session, UnitId beneficiary)
    {
        Require(CanGrantActivation(session, beneficiary));

        var frame = session.CurrentFrame!;
        return Push(session, FrameKind.Granted, frame.Side, beneficiary, answeringWindow: null, ReactionCost.None);
    }

    /// <summary>Whether this unit's activation can be taken away from it where it stands.</summary>
    /// <param name="session">The session.</param>
    /// <param name="side">The side that owns the unit.</param>
    /// <param name="unit">The unit losing its activation.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck CanSpendActivationOutOfSequence(
        GroundCombatSession session,
        SideId side,
        UnitId unit)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Side(side).Unactivated.Contains(unit)
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused($"{unit} is not an unactivated unit of {side}.");
    }

    /// <summary>
    /// Spends a unit's activation without giving it a frame to spend it in.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="side">The side that owns the unit.</param>
    /// <param name="unit">The unit losing its activation.</param>
    /// <returns>The session with that unit marked spent.</returns>
    /// <remarks>
    /// Dirtside's close assault does exactly this to the unit being assaulted: its marker inverts at
    /// once, so it loses its go for the turn without ever having taken one. There is no frame here on
    /// purpose - the unit does not act, it is simply spent - which is why this is a plain transition
    /// rather than a degenerate reaction.
    /// </remarks>
    /// <exception cref="InvalidOperationException">That unit has already gone.</exception>
    public static GroundCombatSession SpendActivationOutOfSequence(
        GroundCombatSession session,
        SideId side,
        UnitId unit)
    {
        Require(CanSpendActivationOutOfSequence(session, side, unit));
        return MarkActivated(session, side, unit);
    }

    /// <summary>Whether this side may pass its go.</summary>
    /// <param name="session">The session.</param>
    /// <param name="side">The side wanting to pass.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck CanPass(GroundCombatSession session, SideId side)
    {
        ArgumentNullException.ThrowIfNull(session);

        var turn = CanTakeATurn(session, side);
        if (!turn.IsAllowed)
        {
            return turn;
        }

        // A side with nothing left to activate is not choosing to pass, it simply cannot do anything
        // else. Left to the bare rule, two exhausted sides would deadlock, since neither has fewer
        // unactivated units than the other. The shared guard therefore stays exactly as written and
        // this case is handled beside it rather than inside it.
        return session.Side(side).UnactivatedCount == 0
            ? SequenceCheck.Allowed
            : SequenceGuards.MayPass(session, side);
    }

    /// <summary>Passes, making the opponent activate twice in a row.</summary>
    /// <param name="session">The session.</param>
    /// <param name="side">The side passing.</param>
    /// <returns>The session with play handed over.</returns>
    /// <exception cref="InvalidOperationException">Passing is not legal for this side now.</exception>
    public static GroundCombatSession Pass(GroundCombatSession session, SideId side)
    {
        Require(CanPass(session, side));

        return HandOver(session with { ConsecutivePasses = session.ConsecutivePasses.Add(side) }, side);
    }

    /// <summary>Whether the turn may end.</summary>
    /// <param name="session">The session.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck CanEndTurn(GroundCombatSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.Phase != TurnPhase.Activating)
        {
            return SequenceCheck.Refused("No turn is being played.");
        }

        if (!session.IsIdle)
        {
            return SequenceCheck.Refused("Something is still activated.");
        }

        var everybodyDone = session.Sides.All(side => side.UnactivatedCount == 0);
        var bothPassed = session.ConsecutivePasses.Distinct().Count() == session.Sides.Length;

        return everybodyDone || bothPassed
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused("Neither side has run out nor have both passed in succession.");
    }

    /// <summary>Ends the turn.</summary>
    /// <param name="session">The session.</param>
    /// <returns>The session with the turn closed, ready for the next one to begin.</returns>
    /// <exception cref="InvalidOperationException">The turn cannot end yet.</exception>
    public static GroundCombatSession EndTurn(GroundCombatSession session)
    {
        Require(CanEndTurn(session));
        return session with { Phase = TurnPhase.TurnEnded, ActiveSide = null };
    }

    private static SequenceCheck CanTakeATurn(GroundCombatSession session, SideId side)
    {
        if (session.Phase != TurnPhase.Activating)
        {
            return SequenceCheck.Refused($"The turn is not in its activation phase ({session.Phase}).");
        }

        if (!session.IsIdle)
        {
            return SequenceCheck.Refused($"{session.CurrentFrame!.Unit} is still activated.");
        }

        if (session.ActiveSide != side)
        {
            return SequenceCheck.Refused($"It is not {side}'s go.");
        }

        return session.Side(side).ForfeitedSlots > 0
            ? SequenceCheck.Refused($"{side} owes a go for reacting and must forfeit it first.")
            : SequenceCheck.Allowed;
    }

    private static GroundCombatSession Push(
        GroundCombatSession session,
        FrameKind kind,
        SideId side,
        UnitId unit,
        WindowId? answeringWindow,
        ReactionCost cost)
    {
        if (session.Depth >= NestingCeiling)
        {
            throw new InvalidOperationException(
                $"Frame nesting reached {NestingCeiling}. Nesting is bounded by the transfer budget, "
                + "the depth of the command chain and the no-self-retrigger rule; reaching this "
                + "ceiling means one of those three has a hole, and raising it would hide the hole.");
        }

        var frame = new ActivationFrame
        {
            Id = new FrameId(session.NextIdentity),
            Kind = kind,
            Side = side,
            Unit = unit,
            AnsweringWindow = answeringWindow,
            Cost = cost,
        };

        return session with
        {
            FrameStack = session.FrameStack.Add(frame),
            NextIdentity = session.NextIdentity + 1,
        };
    }

    private static GroundCombatSession OpenWindow(
        GroundCombatSession session,
        InterruptWindowRequest request)
    {
        var window = new InterruptWindow
        {
            Id = new WindowId(session.NextIdentity),
            Kind = request.Kind,
            RespondingSide = request.RespondingSide,
            EligibleResponders = request.EligibleResponders,
            ResponderCap = request.ResponderCap,
            Geometry = request.Geometry ?? new InterruptGeometry(),
            SuspendedAtDepth = session.Depth,
        };

        return session with
        {
            WindowStack = session.WindowStack.Add(window),
            NextIdentity = session.NextIdentity + 1,
        };
    }

    /// <summary>Marks a responder as having answered, and pops the window once nobody is left.</summary>
    private static GroundCombatSession SettleAnswer(GroundCombatSession session, UnitId responder)
    {
        var window = session.WindowStack[^1];
        var answered = window with { Answered = window.Answered.Add(responder) };

        return answered.IsClosed
            ? session with { WindowStack = session.WindowStack.RemoveAt(session.WindowStack.Length - 1) }
            : session with { WindowStack = session.WindowStack.SetItem(session.WindowStack.Length - 1, answered) };
    }

    private static GroundCombatSession ChargeReaction(GroundCombatSession session, ActivationFrame frame)
    {
        var charged = session;
        if (frame.Cost.HasFlag(ReactionCost.ConsumesActivation))
        {
            charged = MarkActivated(charged, frame.Side, frame.Unit);
        }

        if (frame.Cost.HasFlag(ReactionCost.ForfeitsNextPrioritySlot))
        {
            var side = charged.Side(frame.Side);
            charged = charged.WithSide(side with { ForfeitedPriority = side.ForfeitedPriority.Add(frame.Unit) });
        }

        return charged;
    }

    private static GroundCombatSession MarkActivated(GroundCombatSession session, SideId sideId, UnitId unit)
    {
        var side = session.Side(sideId);
        return session.WithSide(side with { Activated = side.Activated.Add(unit) });
    }

    private static GroundCombatSession HandOver(GroundCombatSession session, SideId from) =>
        session with { ActiveSide = session.Opponent(from).Id };

    private static void Require(SequenceCheck check)
    {
        if (!check.IsAllowed)
        {
            throw new InvalidOperationException(check.Reason);
        }
    }
}
