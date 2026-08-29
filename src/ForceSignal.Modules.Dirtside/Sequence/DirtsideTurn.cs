using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Sequence;

/// <summary>
/// The moves that need Dirtside's rules and the shared layer's transitions at the same time.
/// </summary>
/// <remarks>
/// A thin seam on purpose. Everything here could be written by a caller out of the shared
/// transitions; what it buys is that the cost of a reaction is not something the caller has to
/// remember and can get half right - and the two costs in this game are as far apart as they go.
/// </remarks>
public static class DirtsideTurn
{
    /// <summary>
    /// Answers an open opportunity-fire window, spending the firer's whole activation.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="responder">The interrupting unit.</param>
    /// <returns>The session with the interruption under way.</returns>
    /// <exception cref="InvalidOperationException">The reaction is not legal now.</exception>
    public static GroundCombatSession ReactWithOpportunityFire(
        GroundCombatSession session,
        UnitId responder) =>
        GroundCombatSequence.DeclareReaction(
            session, responder, DirtsideActivationPolicy.OpportunityFireCost);

    /// <summary>
    /// Answers an open interception window, which costs the responder nothing.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="responder">The intercepting unit.</param>
    /// <returns>The session with the interception under way.</returns>
    /// <remarks>
    /// Legal for a unit that has already used its activation, and that is the point of it rather than
    /// a leniency: live sensors are a standing capability bought earlier with a combat action, not a
    /// go being traded away now.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The reaction is not legal now.</exception>
    public static GroundCombatSession InterceptWithAreaDefence(
        GroundCombatSession session,
        UnitId responder) =>
        GroundCombatSequence.DeclareReaction(
            session, responder, DirtsideActivationPolicy.AreaDefenceCost);

    /// <summary>
    /// Hands the activated unit a whole extra activation on the spot, for driving on through a
    /// position it has just taken.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>The session with the open frame's steps wiped.</returns>
    /// <remarks>
    /// <para>
    /// The one thing in this game that gives a unit a second go without the transfer machinery, and
    /// it is done without the transfer machinery: the frame stays open and its steps are wiped, so
    /// every resource an element spent is unspent, because spending is read off the steps and
    /// stored nowhere else. Closing the frame and opening another would mark the unit activated and
    /// hand play over, which is exactly what a follow-through is not.
    /// </para>
    /// <para>
    /// A frame that has been wiped also forgets which elements stood down. That is the rule rather
    /// than a leak: a whole extra activation is one every element takes part in.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Nothing is activated.</exception>
    public static GroundCombatSession FollowThrough(GroundCombatSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.CurrentFrame is not { Kind: FrameKind.Activation } frame)
        {
            throw new InvalidOperationException("Only an activated unit can follow through.");
        }

        return session with
        {
            FrameStack = session.FrameStack.SetItem(
                session.FrameStack.Length - 1,
                frame with { Steps = ImmutableArray<ActivationStep>.Empty }),
        };
    }

    /// <summary>
    /// Turns a unit's marker face-down for being close-assaulted.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="side">The side that owns the unit.</param>
    /// <param name="unit">The unit being assaulted.</param>
    /// <returns>The session with that unit spent, or unchanged if it had already gone.</returns>
    /// <remarks>
    /// <para>
    /// Being assaulted costs a unit its activation on the spot, whether or not it had used it. It
    /// gets no frame, because it is not acting - it is simply spent - and it still fights the
    /// exchange, since a defender that has already activated defends all the same.
    /// </para>
    /// <para>
    /// Idempotent rather than refusing, because a unit that has already gone this turn is in exactly
    /// the state this transition exists to put it in, and making the caller check first would only
    /// invite the check to be forgotten.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static GroundCombatSession CloseAssaulted(
        GroundCombatSession session,
        SideId side,
        UnitId unit)
    {
        ArgumentNullException.ThrowIfNull(session);

        return GroundCombatSequence.CanSpendActivationOutOfSequence(session, side, unit).IsAllowed
            ? GroundCombatSequence.SpendActivationOutOfSequence(session, side, unit)
            : session;
    }
}
