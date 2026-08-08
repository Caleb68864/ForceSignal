using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.StarGrunt.Sequence;

/// <summary>
/// The two moves that need StarGrunt's rules and the shared layer's transitions at the same time.
/// </summary>
/// <remarks>
/// The shared layer's transfer and reaction transitions do not consult a policy - nothing in Dirtside
/// needed them to. Rather than widen the shared interface for one game, the game-specific check is
/// composed with the shared one here, which keeps the shared layer honest and puts the seam somewhere
/// a reader can see it.
/// </remarks>
public static class StarGruntTurn
{
    /// <summary>
    /// Whether a commander may spend an action springing a subordinate.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="policy">StarGrunt's rules.</param>
    /// <param name="beneficiary">The unit to be sprung.</param>
    /// <param name="communicationSucceeded">Whether the caller's communication roll got through.</param>
    /// <returns>Allowed, or the first reason it is not.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static SequenceCheck CanTransfer(
        GroundCombatSession session,
        StarGruntActivationPolicy policy,
        UnitId beneficiary,
        bool communicationSucceeded)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(policy);

        var shared = GroundCombatSequence.CanGrantActivation(session, beneficiary);
        return shared.IsAllowed
            ? policy.CanTransferTo(session, beneficiary, communicationSucceeded)
            : shared;
    }

    /// <summary>
    /// Spends the action and springs the subordinate, in that order.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="policy">StarGrunt's rules.</param>
    /// <param name="beneficiary">The unit being sprung.</param>
    /// <param name="communicationSucceeded">Whether the communication roll got through.</param>
    /// <returns>The session with the granted activation open on top of the commander's own.</returns>
    /// <remarks>
    /// It nests rather than queues, and that is the point of it: the subordinate acts immediately,
    /// before the enemy gets a say, and the commander still has his other action waiting underneath
    /// when the subordinate is done.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The transfer is not legal now.</exception>
    public static GroundCombatSession Transfer(
        GroundCombatSession session,
        StarGruntActivationPolicy policy,
        UnitId beneficiary,
        bool communicationSucceeded)
    {
        var check = CanTransfer(session, policy, beneficiary, communicationSucceeded);
        if (!check.IsAllowed)
        {
            throw new InvalidOperationException(check.Reason);
        }

        var spent = GroundCombatSequence.TakeStep(session, StarGruntSteps.Transfer(beneficiary), policy);
        return GroundCombatSequence.GrantActivation(spent, beneficiary);
    }

    /// <summary>
    /// Answers an open reaction-fire window by firing.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="responder">The reacting unit.</param>
    /// <returns>The session with the reaction under way.</returns>
    /// <remarks>
    /// A convenience over the shared transition purely so that the cost is not something a caller has
    /// to remember and can get half right.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The reaction is not legal now.</exception>
    public static GroundCombatSession ReactWithFire(GroundCombatSession session, UnitId responder) =>
        GroundCombatSequence.DeclareReaction(
            session, responder, StarGruntActivationPolicy.ReactionFireCost);
}
