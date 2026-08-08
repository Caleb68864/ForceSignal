namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>
/// The two rules that are word-for-word identical in both games.
/// </summary>
/// <remarks>
/// <para>
/// Both of these are the same sentence in both rulebooks, so they get exactly one implementation and
/// exactly one set of tests. Forking them would mean two chances to get the boundary wrong, and both
/// of them turn on a boundary that is easy to get wrong in the same way: <b>fewer</b> means fewer,
/// and equal is not fewer.
/// </para>
/// <para>
/// Both read a count of units. Neither reads a stored count - the sets are the source, and the count
/// is taken off them at the moment the question is asked.
/// </para>
/// </remarks>
public static class SequenceGuards
{
    /// <summary>
    /// Which side chooses whether to take or give the first activation this turn.
    /// </summary>
    /// <param name="session">The session, at the top of a turn.</param>
    /// <returns>
    /// The side with fewer units on the table, or null when the two are level - the rules leave a tie
    /// to a die roll or to a house convention, so the shared layer names nobody and lets the caller
    /// settle it.
    /// </returns>
    /// <remarks>
    /// Note that this counts units <em>on the table</em>, not unactivated units. It is a different
    /// question from the pass rule and it is decided fresh each turn, so casualties change who gets
    /// the choice as the game goes on.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SideId? FirstActivationChooser(GroundCombatSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var (first, second) = (session.Sides[0], session.Sides[1]);
        if (first.UnitsOnTable == second.UnitsOnTable)
        {
            return null;
        }

        return first.UnitsOnTable < second.UnitsOnTable ? first.Id : second.Id;
    }

    /// <summary>
    /// Whether a side may pass, forcing the opponent to activate twice in a row.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="side">The side that wants to pass.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <remarks>
    /// Legal only while you have <em>fewer</em> unactivated units than your opponent. Level is not
    /// fewer, and that is the whole point of the rule: it stops the larger force from stalling to
    /// make the smaller one commit first, while still letting the smaller force spread its activations
    /// across the turn.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static SequenceCheck MayPass(GroundCombatSession session, SideId side)
    {
        ArgumentNullException.ThrowIfNull(session);

        var mine = session.Side(side).UnactivatedCount;
        var theirs = session.Opponent(side).UnactivatedCount;

        return mine < theirs
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused(
                $"Passing needs fewer unactivated units than the opponent; {side} has {mine} to their {theirs}.");
    }
}
