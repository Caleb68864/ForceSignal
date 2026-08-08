namespace ForceSignal.Modules.Dirtside.Morale;

/// <summary>
/// Being shot at hard enough to hesitate. One marker, on or off, gone by the end of the unit's own
/// next activation.
/// </summary>
/// <remarks>
/// <para>
/// This is emphatically <b>not</b> the other game's suppression, and the two must never be given a
/// common abstraction. Suppression stacks to three, each marker takes its own action and its own
/// roll to shift, and a unit can be buried under it for several turns without losing a man - it is
/// the currency the whole game is played in. This is a single boolean that lapses on its own: a
/// marked unit announces its activation, tests once before it may move, and the marker comes off at
/// the end of that activation whether the test passed or failed. A shared type over the two would
/// have to carry a count that is always one and a clearing roll that never happens.
/// </para>
/// <para>
/// The threat level the move test is taken at is the caller's, as everywhere else.
/// </para>
/// </remarks>
public static class UnderFire
{
    /// <summary>
    /// Whether an attack leaves the target marked.
    /// </summary>
    /// <param name="kind">Which sort of unit was attacked.</param>
    /// <param name="damagedAnElement">True when the attack actually damaged or destroyed something.</param>
    /// <returns>True when a marker goes on the unit.</returns>
    /// <remarks>
    /// The asymmetry is the rule: men keep their heads down when the rounds come near, so being fired
    /// on at all is enough, while a vehicle crew notices only when something lands. Deciding it from
    /// the unit kind rather than from the attack's own effect would mark every vehicle that was ever
    /// missed.
    /// </remarks>
    public static bool MarksTarget(DirtsideUnitKind kind, bool damagedAnElement) =>
        kind == DirtsideUnitKind.DismountedInfantry || damagedAnElement;

    /// <summary>True when a marked unit owes a reaction test before it may move at all.</summary>
    /// <param name="isMarked">Whether the unit is under fire.</param>
    /// <returns>True when the move is conditional.</returns>
    /// <remarks>
    /// Owed on <em>any</em> move, not only an advance - which is the difference between this and the
    /// confidence ladder's own gate. Failing it and standing still still spends the activation the
    /// unit announced.
    /// </remarks>
    public static bool OwesReactionTestToMove(bool isMarked) => isMarked;

    /// <summary>
    /// Whether the marker is still on the unit after a given activation of its own.
    /// </summary>
    /// <param name="isMarked">Whether the unit is under fire.</param>
    /// <param name="itsOwnActivationEnded">True when the unit has just finished activating.</param>
    /// <returns>Whether the marker remains.</returns>
    /// <remarks>
    /// The clearing is tied to the unit's <em>own</em> activation ending, never to the turn. A unit
    /// shot at after it has already gone carries the marker into the next turn and tests under it,
    /// which is the whole reason the timing is worth a function rather than a comment.
    /// </remarks>
    public static bool After(bool isMarked, bool itsOwnActivationEnded) =>
        isMarked && !itsOwnActivationEnded;
}
