using ForceSignal.Modules.GroundCombat.Morale;

namespace ForceSignal.Modules.Dirtside.Morale;

/// <summary>
/// Which column of the effects table a unit reads.
/// </summary>
/// <remarks>
/// This is not the same question as whether the models are men or vehicles. Infantry riding an
/// armoured transport read the armour column - they have something around them, and it changes what
/// they will agree to do - while troops in a soft-skinned truck read the foot column, because a lorry
/// is no comfort at all. So the caller decides, per unit, per moment, and the engine never infers it.
/// </remarks>
public enum DirtsideUnitKind
{
    /// <summary>Men on their feet, or riding something that will not stop a bullet.</summary>
    DismountedInfantry = 0,

    /// <summary>Vehicles, and the infantry riding inside armoured ones.</summary>
    Armour = 1,
}

/// <summary>
/// What a rung of the ladder stops a unit doing.
/// </summary>
/// <remarks>
/// Flags rather than a single verdict because several of these apply at once, and because a caller
/// asks different questions of them - one gate wants to know whether a move is conditional, another
/// whether the unit may still shoot back.
/// </remarks>
[Flags]
public enum ConfidenceRestriction
{
    /// <summary>Acts normally.</summary>
    None = 0,

    /// <summary>Will leave cover or advance only after passing a reaction test.</summary>
    ReactionTestToAdvance = 1,

    /// <summary>Will not advance on the enemy at all, tested or otherwise.</summary>
    MayNotAdvance = 2,

    /// <summary>Caught in the open, it heads for the nearest cover.</summary>
    MustWithdrawToCoverIfInTheOpen = 4,

    /// <summary>It is going home, wherever it is standing.</summary>
    MustWithdrawToBaseline = 8,

    /// <summary>Will not go in with the bayonet.</summary>
    MayNotCloseAssault = 16,

    /// <summary>Being close-assaulted breaks it outright, without a test.</summary>
    RoutedIfCloseAssaulted = 32,

    /// <summary>Shoots only at whatever is shooting at it.</summary>
    ReturnFireOnly = 64,

    /// <summary>Has stopped shooting altogether.</summary>
    MayNotFire = 128,
}

/// <summary>
/// Dirtside's half of morale: what each rung of the shared ladder costs a unit, by column.
/// </summary>
/// <remarks>
/// <para>
/// The ladder and the test are shared - see <see cref="ConfidenceLadder"/> - and this is the part
/// that is not. Dirtside's effects table has <b>two columns</b>, one for dismounted infantry and one
/// for armour, and they do not degrade in step: the two kinds are restricted by different things at
/// the same rung, and one of them is not a softer version of the other. StarGrunt's restrictions, by
/// contrast, are one cumulative list for everybody. There is no table that is right for both games,
/// which is exactly why neither game's is in the shared layer.
/// </para>
/// <para>
/// Nothing here is a threat level or a die. The rungs are the rules' own five and the effects are
/// procedural; every number a test needs comes in from the caller.
/// </para>
/// </remarks>
public static class DirtsideConfidence
{
    /// <summary>What this rung stops this kind of unit doing.</summary>
    /// <param name="level">Where the unit's confidence marker stands.</param>
    /// <param name="kind">Which column of the table it reads.</param>
    /// <returns>Every restriction that applies.</returns>
    /// <remarks>
    /// Note where the two columns cross over rather than run parallel. Armour is the kind that balks
    /// first: a shaken vehicle crew needs talking out of cover and will not charge, while shaken foot
    /// still advances on a passed test. One rung lower it reverses - broken infantry stops advancing
    /// but keeps fighting from where it is, while broken armour turns for home and fires only if
    /// something makes it.
    /// </remarks>
    public static ConfidenceRestriction Restrictions(ConfidenceLevel level, DirtsideUnitKind kind) =>
        kind == DirtsideUnitKind.DismountedInfantry ? OnFoot(level) : InArmour(level);

    /// <summary>True when this unit needs a passed reaction test before it advances or leaves cover.</summary>
    /// <param name="level">The unit's confidence.</param>
    /// <param name="kind">Which column it reads.</param>
    /// <returns>True when the move is conditional rather than free.</returns>
    public static bool NeedsReactionTestToAdvance(ConfidenceLevel level, DirtsideUnitKind kind) =>
        Restrictions(level, kind).HasFlag(ConfidenceRestriction.ReactionTestToAdvance);

    /// <summary>True when this unit will not advance on the enemy at all.</summary>
    /// <param name="level">The unit's confidence.</param>
    /// <param name="kind">Which column it reads.</param>
    /// <returns>True when no test will get it forward.</returns>
    public static bool RefusesToAdvance(ConfidenceLevel level, DirtsideUnitKind kind) =>
        Restrictions(level, kind).HasFlag(ConfidenceRestriction.MayNotAdvance);

    /// <summary>True when this unit may still open fire on something of its own choosing.</summary>
    /// <param name="level">The unit's confidence.</param>
    /// <param name="kind">Which column it reads.</param>
    /// <returns>False when it has stopped firing, or will only fire back.</returns>
    public static bool MayPickItsOwnTarget(ConfidenceLevel level, DirtsideUnitKind kind) =>
        (Restrictions(level, kind)
            & (ConfidenceRestriction.MayNotFire | ConfidenceRestriction.ReturnFireOnly))
        == ConfidenceRestriction.None;

    /// <summary>True when this unit has stopped shooting altogether.</summary>
    /// <param name="level">The unit's confidence.</param>
    /// <param name="kind">Which column it reads.</param>
    /// <returns>True when it fires at nothing.</returns>
    public static bool HasStoppedFiring(ConfidenceLevel level, DirtsideUnitKind kind) =>
        Restrictions(level, kind).HasFlag(ConfidenceRestriction.MayNotFire);

    /// <summary>
    /// What being close-assaulted does to a unit before a shot is exchanged.
    /// </summary>
    /// <param name="level">The unit's confidence when the assault came in.</param>
    /// <param name="kind">Which column it reads.</param>
    /// <returns>Where its marker ends up.</returns>
    /// <remarks>
    /// A crew whose nerve is already going does not fight infantry climbing onto the hull; it breaks
    /// and drives. That is a substitute for the defender's confidence test rather than an extra one -
    /// there is nothing left to test.
    /// </remarks>
    public static ConfidenceLevel OnCloseAssaulted(ConfidenceLevel level, DirtsideUnitKind kind) =>
        Restrictions(level, kind).HasFlag(ConfidenceRestriction.RoutedIfCloseAssaulted)
            ? ConfidenceLevel.Routed
            : level;

    private static ConfidenceRestriction OnFoot(ConfidenceLevel level) => level switch
    {
        // Shaken foot is not banned from anything outright. Its reluctance is expressed through the
        // reaction-test system instead, which is why this column looks thinner than the other one
        // without the troops being any braver.
        ConfidenceLevel.Shaken => ConfidenceRestriction.ReactionTestToAdvance,
        ConfidenceLevel.Broken =>
            ConfidenceRestriction.MayNotAdvance
            | ConfidenceRestriction.MustWithdrawToCoverIfInTheOpen

            // Not from the effects table but from the assault rules, which refuse a broken or routed
            // unit outright. It belongs on the same row as everything else a rung forbids, or a
            // caller has to remember to ask two questions to find out whether a charge is on.
            | ConfidenceRestriction.MayNotCloseAssault,
        ConfidenceLevel.Routed =>
            ConfidenceRestriction.MayNotAdvance
            | ConfidenceRestriction.MustWithdrawToBaseline
            | ConfidenceRestriction.MayNotCloseAssault
            | ConfidenceRestriction.MayNotFire,
        _ => ConfidenceRestriction.None,
    };

    private static ConfidenceRestriction InArmour(ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.Shaken =>
            ConfidenceRestriction.ReactionTestToAdvance
            | ConfidenceRestriction.MustWithdrawToCoverIfInTheOpen
            | ConfidenceRestriction.MayNotCloseAssault
            | ConfidenceRestriction.RoutedIfCloseAssaulted,
        ConfidenceLevel.Broken =>
            ConfidenceRestriction.MayNotAdvance
            | ConfidenceRestriction.MustWithdrawToBaseline
            | ConfidenceRestriction.MayNotCloseAssault
            | ConfidenceRestriction.ReturnFireOnly,
        ConfidenceLevel.Routed =>
            ConfidenceRestriction.MayNotAdvance
            | ConfidenceRestriction.MustWithdrawToBaseline
            | ConfidenceRestriction.MayNotCloseAssault
            | ConfidenceRestriction.MayNotFire,
        _ => ConfidenceRestriction.None,
    };
}
