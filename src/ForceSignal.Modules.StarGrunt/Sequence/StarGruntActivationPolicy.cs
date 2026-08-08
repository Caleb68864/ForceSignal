using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Morale;

namespace ForceSignal.Modules.StarGrunt.Sequence;

/// <summary>
/// StarGrunt's half of the activation rules, plugged into the shared sequence layer.
/// </summary>
/// <remarks>
/// <para>
/// The shared layer was built against Dirtside on purpose, so this is the other half of the
/// experiment: everything StarGrunt needs that Dirtside did not. Almost all of it fits. The two-action
/// budget, the once-per-activation weapon limit, transferred activations and reaction fire are all
/// expressible without the shared layer knowing any of them exists.
/// </para>
/// <para>
/// <b>The one thing that does not fit</b> is a budget that spans a whole turn rather than one
/// activation. The shared layer discards a frame when it closes, so a commander's two-transfers-per-turn
/// cap has nowhere in the session to be read back from once his own activation is over - and a
/// transferred activation can itself contain a transfer, so the two are genuinely different spans.
/// Per-activation transfers are derived from the frame as everything else is; the per-turn total has
/// to come off the board. That is a real gap in the session model rather than a wrinkle here, and it
/// is worth knowing before the after-action log is built on the same assumption.
/// </para>
/// <para>
/// This class never rolls anything. Reaction tests, communication rolls and confidence tests are the
/// caller's, and the policy's job is to say which of them is owed.
/// </para>
/// </remarks>
public sealed class StarGruntActivationPolicy : IActivationPolicy
{
    private readonly IStarGruntBoard board;

    /// <summary>Builds the policy over a board.</summary>
    /// <param name="board">The caller's live model of the table.</param>
    /// <exception cref="ArgumentNullException"><paramref name="board"/> is null.</exception>
    public StarGruntActivationPolicy(IStarGruntBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        this.board = board;
    }

    /// <summary>The interrupt window a declared dash opens.</summary>
    public const string ReactionFireWindow = "ReactionFire";

    /// <summary>
    /// What answering that window costs.
    /// </summary>
    /// <remarks>
    /// Reaction fire is the expensive one: it spends the firer's whole activation <em>and</em> counts
    /// as that player's next go, so play returns to the mover rather than passing over. Both of those
    /// ride on the declaration rather than on the window, which is what lets Dirtside's free
    /// area-defence interception and this share one mechanism.
    /// </remarks>
    public const ReactionCost ReactionFireCost =
        ReactionCost.ConsumesActivation | ReactionCost.ForfeitsNextPrioritySlot;

    /// <summary>How many actions this frame has already spent.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The total cost of its steps.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static int ActionsSpent(ActivationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.Steps.IsDefaultOrEmpty
            ? 0
            : frame.Steps.Sum(step => StarGruntSteps.ActionOf(step) is { } action
                ? StarGruntActions.Cost(action)
                : 0);
    }

    /// <summary>How many actions this frame has left.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>What remains of the two.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static int ActionsRemaining(ActivationFrame frame) =>
        StarGruntActions.ActionsPerActivation - ActionsSpent(frame);

    /// <summary>How many subordinates this frame has already sprung.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The count, read off the frame's spent resources.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static int TransfersMade(ActivationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.ResourcesSpent.Count(name =>
            name.StartsWith(StarGruntSteps.TransferPrefix, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public SequenceCheck IsStepLegal(GroundCombatSession session, ActivationFrame frame, ActivationStep step)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(step);

        if (StarGruntSteps.ActionOf(step) is not { } action)
        {
            return SequenceCheck.Refused($"'{step.Kind}' is not a StarGrunt action.");
        }

        var state = board.State(frame.Unit);

        foreach (var gate in new[]
        {
            Budget(frame, action),
            Integrity(state, action),
            Pinned(state, action),
            Repeats(frame, action),
            Nerve(state, action),
            Weapons(frame, action, step),
            Transfers(frame, state, action, step),
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
    /// <remarks>
    /// Always, short of an over-spend the step gate would already have refused. No unit is ever forced
    /// to use its actions, so an activation is finishable the moment it starts - the budget is
    /// enforced on the way in, not on the way out.
    /// </remarks>
    /// <param name="session">The session.</param>
    /// <param name="frame">The frame.</param>
    /// <returns>True when the frame may close.</returns>
    public bool IsFrameComplete(GroundCombatSession session, ActivationFrame frame) =>
        ActionsRemaining(frame) >= 0;

    /// <inheritdoc/>
    public InterruptWindowRequest? WindowOpenedBy(
        GroundCombatSession session,
        ActivationFrame frame,
        ActivationStep step)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(step);

        // Reaction fire is the only out-of-sequence action in the game, and it triggers on nothing but
        // a unit spending both actions moving. Because that is declared as one step, the window opens
        // deterministically at the moment of the declaration.
        if (StarGruntSteps.ActionOf(step) != StarGruntAction.Dash)
        {
            return null;
        }

        if (board.DashOpening(frame.Unit) is not { } opening)
        {
            return null;
        }

        // A unit that has already gone this turn cannot react, so eligibility is the intersection of
        // what can see the mid-point with what still has its marker face-up. Frozen here rather than
        // recomputed later, because the shot itself will change both.
        var opponent = session.Opponent(frame.Side);
        var eligible = opening.Watchers
            .Where(opponent.Unactivated.Contains)
            .ToImmutableArray();

        return eligible.IsEmpty
            ? null
            : new InterruptWindowRequest(
                ReactionFireWindow,
                opponent.Id,
                eligible,

                // Only one opposing unit may react to any one mover.
                ResponderCap: 1,
                new InterruptGeometry
                {
                    ResolutionPoint = opening.Midpoint,
                    Circumstances = opening.Circumstances,
                });
    }

    /// <summary>
    /// Whether this commander may hand that subordinate an activation.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="beneficiary">The unit to be sprung.</param>
    /// <param name="communicationSucceeded">
    /// Whether the caller's communication roll got through. Automatic at close range in the rules, so
    /// the caller decides; the distance that makes it automatic is the caller's number too.
    /// </param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <remarks>
    /// This is not reachable through <see cref="IActivationPolicy"/>: the shared layer's transfer
    /// transition does not consult a policy, because nothing in Dirtside needed it to. So the caller
    /// composes this with the shared check rather than the shared layer calling down into it - see
    /// <see cref="StarGruntTurn.CanTransfer"/>, which does exactly that.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public SequenceCheck CanTransferTo(
        GroundCombatSession session,
        UnitId beneficiary,
        bool communicationSucceeded)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.CurrentFrame is not { } frame)
        {
            return SequenceCheck.Refused("Only a commander part-way through his own activation may transfer.");
        }

        if (!communicationSucceeded)
        {
            return SequenceCheck.Refused("The message did not get through.");
        }

        var commander = board.State(frame.Unit);
        var subordinate = board.State(beneficiary);

        if (!CommandChain.IsDownward(commander.Level, subordinate.Level))
        {
            return SequenceCheck.Refused(
                $"An activation is passed down the chain; {commander.Level} cannot transfer to {subordinate.Level}.");
        }

        // The transfer step itself is what spends the action, so it has to be affordable before the
        // granted frame is pushed.
        return Budget(frame, StarGruntAction.TransferAction) is { IsAllowed: false } budget
            ? budget
            : Transfers(
                frame,
                commander,
                StarGruntAction.TransferAction,
                StarGruntSteps.Transfer(beneficiary));
    }

    /// <summary>
    /// How much the communication roll is shifted by the levels it skips.
    /// </summary>
    /// <param name="grantor">The commander speaking.</param>
    /// <param name="beneficiary">The unit being spoken to.</param>
    /// <returns>Levels bypassed, for the caller to shift its die by.</returns>
    public int LevelsBypassed(UnitId grantor, UnitId beneficiary) =>
        CommandChain.LevelsBypassed(
            board.State(grantor).Level,
            board.State(beneficiary).Level,
            board.CommandLevelsOnTable);

    private static SequenceCheck Budget(ActivationFrame frame, StarGruntAction action)
    {
        var cost = StarGruntActions.Cost(action);
        var remaining = ActionsRemaining(frame);

        return cost <= remaining
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused(
                remaining == 0
                    ? $"{frame.Unit} has spent both its actions."
                    : $"{action} costs {cost} actions and only {remaining} is left.");
    }

    private static SequenceCheck Integrity(StarGruntUnitState state, StarGruntAction action)
    {
        // A scattered squad owes a reorganise before anything else, and may do nothing else meanwhile.
        // The caller clears the flag once the reorganise resolves, and because the board is read live
        // the second action is free without anything here having to remember that it happened.
        return !state.IsDisorganised || action == StarGruntAction.Reorganise
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused("A disorganised unit must reorganise before it does anything else.");
    }

    private static SequenceCheck Pinned(StarGruntUnitState state, StarGruntAction action)
    {
        // Note what falls out rather than being written: a unit that spends both actions retrying a
        // suppression removal can do nothing else that turn, because both actions are gone.
        return Suppression.Allows(
            state.SuppressionMarkers, StarGruntActions.AsSuppressedAction(action), state.IsInCover)
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused(
                action == StarGruntAction.Reorganise
                    ? "A suppressed unit may only reorganise under cover."
                    : $"A suppressed unit may not {action}.");
    }

    private static SequenceCheck Repeats(ActivationFrame frame, StarGruntAction action)
    {
        if (StarGruntActions.MayRepeat(action) || frame.Steps.IsDefaultOrEmpty)
        {
            return SequenceCheck.Allowed;
        }

        var already = frame.Steps.Any(step => StarGruntSteps.ActionOf(step) == action);
        if (!already)
        {
            return SequenceCheck.Allowed;
        }

        return action == StarGruntAction.Move
            ? SequenceCheck.Refused(
                "Two move actions are a dash, and a dash has to be declared as one so that the "
                + "reaction window opens at its mid-point.")
            : SequenceCheck.Refused($"{action} may only be attempted once in an activation.");
    }

    private static SequenceCheck Nerve(StarGruntUnitState state, StarGruntAction action)
    {
        if (action == StarGruntAction.Fire && Confidence.HasStoppedFighting(state.Confidence))
        {
            return SequenceCheck.Refused("A routed unit fires at nothing.");
        }

        var risky = action is StarGruntAction.Move or StarGruntAction.Dash or StarGruntAction.CloseAssault;
        if (!risky || !Confidence.NeedsReactionTestToAdvance(state.Confidence) || !state.NextMoveLeavesCover)
        {
            return SequenceCheck.Allowed;
        }

        // Failing the test costs the action, not a confidence level - so this is a refusal the caller
        // answers by spending the action elsewhere, not a reason to reconsider the whole activation.
        return state.ReactionTestCleared
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused(
                $"A {state.Confidence} unit needs a passed reaction test before it will leave cover.");
    }

    private static SequenceCheck Weapons(ActivationFrame frame, StarGruntAction action, ActivationStep step)
    {
        if (action != StarGruntAction.Fire)
        {
            return SequenceCheck.Allowed;
        }

        if (StarGruntSteps.WeaponOf(step) is not { } weapon)
        {
            return SequenceCheck.Refused("A fire action has to name the weapon it fires.");
        }

        // Per activation, not per game turn. Reading it off the frame is the whole of the fix: a
        // transferred activation is a new frame, so the same weapon may fire again in the same turn,
        // and no flag anywhere has to be remembered or reset.
        return frame.HasSpent(weapon)
            ? SequenceCheck.Refused($"{weapon[StarGruntSteps.WeaponPrefix.Length..]} has already fired this activation.")
            : SequenceCheck.Allowed;
    }

    private static SequenceCheck Transfers(
        ActivationFrame frame,
        StarGruntUnitState state,
        StarGruntAction action,
        ActivationStep step)
    {
        if (action != StarGruntAction.TransferAction)
        {
            return SequenceCheck.Allowed;
        }

        // Springing the same subordinate twice would be two activations for one unit off one
        // commander, which is not what "two subordinates" means. Naming the beneficiary in the spent
        // set is what makes that fall out rather than need a rule of its own.
        if (StarGruntSteps.TransferTargetOf(step) is { } target && frame.HasSpent(target))
        {
            return SequenceCheck.Refused(
                $"{target[StarGruntSteps.TransferPrefix.Length..]} has already been sprung this activation.");
        }

        if (TransfersMade(frame) >= StarGruntActions.TransfersPerCommander)
        {
            return SequenceCheck.Refused(
                $"A commander may spring {StarGruntActions.TransfersPerCommander} subordinates, no more.");
        }

        // The second gate, and the one the session cannot supply. A commander who has himself been
        // given an extra activation is on a fresh frame, so only the board remembers what he did on
        // the previous one.
        return state.TransfersMadeThisTurn < StarGruntActions.TransfersPerCommander
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused(
                $"{frame.Unit} has already sprung {state.TransfersMadeThisTurn} subordinates this turn.");
    }
}
