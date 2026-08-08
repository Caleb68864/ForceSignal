using System.Collections.Immutable;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Sequence;

/// <summary>
/// Dirtside's half of the activation rules, plugged into the shared sequence layer.
/// </summary>
/// <remarks>
/// <para>
/// The shared layer was built against this game first, so most of what follows is the game it was
/// shaped around finally being written down. Four things are worth reading before the code.
/// </para>
/// <para>
/// <b>The unit is a platoon and the elements inside it are what decide.</b> There is no
/// two-action budget anywhere here; each element gets one move and one combat action and takes them
/// in whichever order it likes, or takes neither. The five patterns the rules list - move then act,
/// act then move, act only, move only, nothing - are not enumerated anywhere in this class. They are
/// what is left when you allow one of each per element and let the order fall out. Every limit is
/// therefore written against a resource name that has the element in it, where the other game's are
/// bare, and the shared layer holds no opinion about either scope.
/// </para>
/// <para>
/// <b>The two interrupts cost opposite amounts, and that is why the cost rides on the
/// declaration.</b> Opportunity fire buys an interruption with the firer's entire activation, even
/// if one element of it fired. Area-defence interception costs nothing at all: sensors that are
/// already live intercept on anybody's activation, including a unit that has long since gone, which
/// is precisely what an already-activated responder could never do if the cost were a property of
/// the window. Note also what opportunity fire does <em>not</em> cost - the other game's reaction
/// fire additionally forfeits its side's next go, and nothing in Dirtside does that.
/// </para>
/// <para>
/// <b>A confidence test is not an interrupt window, and this is where that was settled.</b> The
/// plan counted five windows here and suspected one too many. It is one too many. A window exists so
/// that the <em>other</em> player can choose to spend something: it names a responding side, a list
/// of who is eligible, a cap on how many may answer, and a cost. A confidence test has none of those
/// - nobody chooses to take it, nobody may decline it, it belongs to the unit already being shot at,
/// and it costs nothing but morale. Made a window, it would push a frame for a unit that is not
/// acting, and the no-self-retrigger rule would then quietly stop a unit fired on twice from testing
/// twice, which is the opposite of the rule. So it is a consequence resolved inside whatever is
/// already happening - the caller applies it to its own board between steps, and the frame it
/// interrupted resumes underneath. Four windows, not five.
/// </para>
/// <para>
/// This class never rolls anything. Reaction and confidence tests are the caller's, and the policy's
/// job is to say which of them is owed.
/// </para>
/// </remarks>
public sealed class DirtsideActivationPolicy : IActivationPolicy
{
    private readonly IDirtsideBoard board;

    /// <summary>Builds the policy over a board.</summary>
    /// <param name="board">The caller's live model of the table.</param>
    /// <exception cref="ArgumentNullException"><paramref name="board"/> is null.</exception>
    public DirtsideActivationPolicy(IDirtsideBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        this.board = board;
    }

    /// <summary>The interrupt window a moving element opens.</summary>
    public const string OpportunityFireWindow = "OpportunityFire";

    /// <summary>
    /// What answering that window costs: the firing unit's whole activation for the turn.
    /// </summary>
    /// <remarks>
    /// The whole activation however little of the unit fired, which is the trade the rules mean it to
    /// be - interrupt the enemy's move now, or keep your go and act on your own initiative later.
    /// Deliberately without <see cref="ReactionCost.ForfeitsNextPrioritySlot"/>: play returns to the
    /// mover to finish, and then passes over normally.
    /// </remarks>
    public const ReactionCost OpportunityFireCost = ReactionCost.ConsumesActivation;

    /// <summary>The interrupt window a shot that can be shot down opens.</summary>
    public const string AreaDefenceWindow = "AreaDefenceInterception";

    /// <summary>
    /// What answering that window costs: nothing.
    /// </summary>
    /// <remarks>
    /// The free reaction, and the reason the shared layer keeps cost on the declaration rather than
    /// on the window. A system whose sensors are already live has paid for this with the combat
    /// action that switched them on, and it may answer on any activation in the turn - including one
    /// where it has already gone. A cost of <see cref="ReactionCost.None"/> is what lets an
    /// already-activated unit through the shared layer's check at all.
    /// </remarks>
    public const ReactionCost AreaDefenceCost = ReactionCost.None;

    /// <inheritdoc/>
    public SequenceCheck IsStepLegal(GroundCombatSession session, ActivationFrame frame, ActivationStep step)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(step);

        if (DirtsideSteps.ActionOf(step) is not { } action)
        {
            return SequenceCheck.Refused($"'{step.Kind}' is not a Dirtside action.");
        }

        if (step.Subject is not { } element)
        {
            return SequenceCheck.Refused(
                "A Dirtside step has to name the element that took it: the unit is a platoon and its "
                + "elements choose independently, so a step belonging to the whole unit has no rule.");
        }

        var state = board.State(frame.Unit);

        foreach (var gate in new[]
        {
            StoodDown(frame, element, action),
            Repeats(frame, element, action),
            Weapon(step, action),
            FixedMount(frame, element, step, action),
            Integrity(state, action),
            Nerve(state, action),
        })
        {
            if (!gate.IsAllowed)
            {
                return gate;
            }
        }

        return SequenceCheck.Allowed;
    }

    /// <summary>
    /// Whether the frame may be closed.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="frame">The frame.</param>
    /// <returns>True when the frame may close.</returns>
    /// <remarks>
    /// <para>
    /// Every element has to have said what it is doing, even if what it is doing is nothing. That is
    /// not bookkeeping for its own sake: an element that sits out its unit's activation has given up
    /// its turn entirely and may not act later, so the moment the marker inverts is the moment that
    /// becomes true of it. Asking for the declaration is how the engine can tell an element that
    /// chose to hold from one whose player has not finished clicking.
    /// </para>
    /// <para>
    /// A reaction frame is judged differently and has to be. Opportunity fire is answered by whatever
    /// part of the unit can reach the mover, and the rules are explicit that the rest of it has not
    /// thereby chosen anything - the activation goes all the same. So one step is enough there.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public bool IsFrameComplete(GroundCombatSession session, ActivationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Kind == FrameKind.Reaction)
        {
            return !frame.Steps.IsDefaultOrEmpty;
        }

        var accounted = Accounted(frame);
        return board.Elements(frame.Unit).All(accounted.Contains);
    }

    /// <summary>Which elements of this unit have not yet said what they are doing.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The elements still to choose, in the board's order.</returns>
    /// <remarks>
    /// The other half of <see cref="IsFrameComplete"/>, exposed because a refusal to close an
    /// activation is only useful to a player if it can say who everybody is waiting on.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public IReadOnlyList<ElementId> StillToChoose(ActivationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var accounted = Accounted(frame);
        return [.. board.Elements(frame.Unit).Where(element => !accounted.Contains(element))];
    }

    /// <inheritdoc/>
    public InterruptWindowRequest? WindowOpenedBy(
        GroundCombatSession session,
        ActivationFrame frame,
        ActivationStep step)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(step);

        if (step.Subject is not { } element || DirtsideSteps.ActionOf(step) is not { } action)
        {
            return null;
        }

        var opponent = session.Opponent(frame.Side);

        return action switch
        {
            DirtsideAction.Move => OpportunityFire(frame, element, opponent),
            DirtsideAction.DirectFire => Interception(frame, element, step, opponent),
            _ => null,
        };
    }

    private InterruptWindowRequest? OpportunityFire(
        ActivationFrame frame,
        ElementId element,
        SideState opponent)
    {
        if (board.OpportunityFireOpening(frame.Unit, element) is not { } opening)
        {
            return null;
        }

        // Only a unit that still has its own activation to trade can make this trade, so eligibility
        // is the intersection of what can reach the mover with what is still face-up. Frozen at the
        // moment of the trigger, because the fire itself will change both.
        var eligible = opening.Watchers
            .Where(opponent.Unactivated.Contains)
            .ToImmutableArray();

        return eligible.IsEmpty
            ? null
            : new InterruptWindowRequest(
                OpportunityFireWindow,
                opponent.Id,
                eligible,

                // No cap in the rules: any number of unactivated units may each spend their turn on
                // the same mover, which is an expensive way to stop something and a legal one.
                ResponderCap: int.MaxValue,
                new InterruptGeometry
                {
                    ResolutionPoint = opening.ResolutionPoint,
                    Circumstances = opening.Circumstances,
                });
    }

    private InterruptWindowRequest? Interception(
        ActivationFrame frame,
        ElementId element,
        ActivationStep step,
        SideState opponent)
    {
        if (DirtsideSteps.WeaponOf(step) is not { } weapon
            || board.InterceptionOpening(frame.Unit, element, weapon) is not { } opening
            || opening.Watchers.IsDefaultOrEmpty)
        {
            return null;
        }

        // Note what is missing: the unactivated filter that opportunity fire needs. Interception is
        // free, so a unit that went early in the turn is as able to answer as one that has not moved,
        // and filtering here would silently take that away.
        return new InterruptWindowRequest(
            AreaDefenceWindow,
            opponent.Id,
            opening.Watchers,
            ResponderCap: int.MaxValue,
            new InterruptGeometry
            {
                ResolutionPoint = opening.ResolutionPoint,
                Circumstances = opening.Circumstances,
            });
    }

    private static ImmutableHashSet<ElementId> Accounted(ActivationFrame frame) =>
        frame.Steps.IsDefaultOrEmpty
            ? []
            : [.. frame.Steps.Select(step => step.Subject).OfType<ElementId>()];

    private static SequenceCheck StoodDown(
        ActivationFrame frame,
        ElementId element,
        DirtsideAction action)
    {
        if (!frame.HasSpent(DirtsideSteps.StoodDown(element)))
        {
            return SequenceCheck.Allowed;
        }

        return SequenceCheck.Refused(
            action == DirtsideAction.Nothing
                ? $"{element} has already stood down."
                : $"{element} stood down, and an element that sits out its unit's activation is out "
                  + "for the turn.");
    }

    private static SequenceCheck Repeats(ActivationFrame frame, ElementId element, DirtsideAction action)
    {
        if (action == DirtsideAction.Nothing)
        {
            // Standing down is a decision about the whole activation, so it cannot follow a move or a
            // shot the element has already taken.
            return frame.HasSpent(DirtsideSteps.Moved(element))
                || frame.HasSpent(DirtsideSteps.Acted(element))
                ? SequenceCheck.Refused($"{element} has already acted and cannot now do nothing.")
                : SequenceCheck.Allowed;
        }

        if (action == DirtsideAction.Move)
        {
            return frame.HasSpent(DirtsideSteps.Moved(element))
                ? SequenceCheck.Refused($"{element} has already moved.")
                : SequenceCheck.Allowed;
        }

        // One combat action per element - which is also the whole of the one-weapon-per-action rule,
        // since an element with one action has one chance to name a system. It falls out rather than
        // needing a rule of its own, and it is scoped per element rather than per activation, unlike
        // the other game's weapon limit.
        return frame.HasSpent(DirtsideSteps.Acted(element))
            ? SequenceCheck.Refused(
                $"{element} has already taken its combat action, and it gets "
                + $"{DirtsideActions.CombatActionsPerElement}.")
            : SequenceCheck.Allowed;
    }

    private static SequenceCheck Weapon(ActivationStep step, DirtsideAction action)
    {
        if (!DirtsideActions.NamesAWeapon(action))
        {
            return SequenceCheck.Allowed;
        }

        return DirtsideSteps.WeaponOf(step) is null
            ? SequenceCheck.Refused("A fire action has to name the one weapon system it fires.")
            : SequenceCheck.Allowed;
    }

    private SequenceCheck FixedMount(
        ActivationFrame frame,
        ElementId element,
        ActivationStep step,
        DirtsideAction action)
    {
        if (!DirtsideActions.NamesAWeapon(action)
            || DirtsideSteps.WeaponOf(step) is not { } weapon
            || !board.IsFixedMount(frame.Unit, element, weapon))
        {
            return SequenceCheck.Allowed;
        }

        // The one exception to elements being free to order their move and their action as they like.
        // A weapon that is aimed by pointing the vehicle can be laid on a target before the vehicle
        // sets off, and not once it has - so this is the only rule in the game that reads the order
        // the steps were taken in rather than just which of them were.
        return frame.HasSpent(DirtsideSteps.Moved(element))
            ? SequenceCheck.Refused(
                $"'{weapon}' is a fixed mount, so {element} may fire it before or instead of moving, "
                + "never after.")
            : SequenceCheck.Allowed;
    }

    private static SequenceCheck Integrity(DirtsideUnitState state, DirtsideAction action)
    {
        // A unit that has come apart may only close its ranks. The caller clears the flag when the
        // elements are back within their spacing, and because the board is read live the rest of the
        // activation is free without anything here having to remember that it happened.
        return !state.IsDisorganised || action is DirtsideAction.Move or DirtsideAction.Nothing
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused(
                "A disorganised unit may only move to restore its integrity.");
    }

    private static SequenceCheck Nerve(DirtsideUnitState state, DirtsideAction action)
    {
        // Everything below this line is switched off for a cybertank, in one place rather than as a
        // clause on each rule. It has no confidence marker to read, so there is nothing to consult.
        if (state.IsCybertank)
        {
            return SequenceCheck.Allowed;
        }

        return action switch
        {
            DirtsideAction.Move => Moving(state),
            DirtsideAction.DirectFire when DirtsideConfidence.HasStoppedFiring(state.Confidence, state.Kind) =>
                SequenceCheck.Refused($"A {state.Confidence} unit fires at nothing."),
            DirtsideAction.CloseAssault when !DirtsideConfidence
                .Restrictions(state.Confidence, state.Kind)
                .HasFlag(ConfidenceRestriction.MayNotCloseAssault) => SequenceCheck.Allowed,
            DirtsideAction.CloseAssault =>
                SequenceCheck.Refused($"A {state.Confidence} unit will not close-assault."),
            _ => SequenceCheck.Allowed,
        };
    }

    private static SequenceCheck Moving(DirtsideUnitState state)
    {
        // Two separate gates that happen to want the same answer from the caller. Under Fire stops
        // any move at all until a test is passed; the confidence ladder stops only an advance. A unit
        // that is both marked and shaken still owes one test, because threat levels are never
        // cumulative - the caller takes it at the higher of the two and comes back cleared.
        if (UnderFire.OwesReactionTestToMove(state.IsUnderFire) && !state.ReactionTestCleared)
        {
            return SequenceCheck.Refused(
                "A unit under fire has to pass a reaction test before it may move at all.");
        }

        if (!state.NextMoveAdvancesOnTheEnemy)
        {
            return SequenceCheck.Allowed;
        }

        if (DirtsideConfidence.RefusesToAdvance(state.Confidence, state.Kind))
        {
            return SequenceCheck.Refused($"A {state.Confidence} unit will not advance on the enemy at all.");
        }

        return !DirtsideConfidence.NeedsReactionTestToAdvance(state.Confidence, state.Kind)
            || state.ReactionTestCleared
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused(
                $"A {state.Confidence} unit needs a passed reaction test before it will advance.");
    }
}
