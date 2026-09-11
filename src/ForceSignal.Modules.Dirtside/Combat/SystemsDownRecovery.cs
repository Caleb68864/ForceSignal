using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.Dirtside.Combat;

/// <summary>What one attempt to get the electronics back produced.</summary>
/// <param name="WasAttempted">False when the crew never got to roll.</param>
/// <param name="Roll">What was rolled, or zero when nothing was.</param>
/// <param name="Required">The number that had to be reached.</param>
/// <param name="Cleared">True when the marker comes off and the vehicle works normally again.</param>
/// <param name="Reason">Why no roll was made, or null.</param>
public readonly record struct RecoveryAttempt(
    bool WasAttempted,
    int Roll,
    int Required,
    bool Cleared,
    string? Reason);

/// <summary>
/// Getting a Systems Down marker off again.
/// </summary>
/// <remarks>
/// <para>
/// The only self-repair in the game, and the only place a result is undone rather than accumulated.
/// Three things shape it. It cannot be tried on the activation the damage happened - the crew are
/// busy - so it needs the activation the marker was placed on as well as the current one. It never
/// stops being available: a failure costs nothing but the activation, and the same vehicle may fail
/// six times and succeed on the seventh. And backup systems, bought at design time, turn a long shot
/// into an even chance.
/// </para>
/// <para>
/// Nothing here is stateful. An attempt is a pure function of the two activation numbers, the
/// design, and one die, which is what makes it safe to retry indefinitely and safe to reuse for a
/// vehicle that goes down twice in one game - there is no "already tried" flag to reset and get
/// wrong.
/// </para>
/// <para>
/// It applies to a Systems Down marker whichever side of the shot put it there: the target whose
/// sensors were knocked out and the firer whose own systems failed recover by the same roll.
/// </para>
/// </remarks>
public static class SystemsDownRecovery
{
    /// <summary>
    /// The number that has to come up, or null when the profile does not say.
    /// </summary>
    /// <param name="profile">The numbers this game's players entered off their own rulebook.</param>
    /// <param name="hasBackupSystems">Whether backup systems were bought at design time.</param>
    /// <returns>The number to reach or beat, or null when nobody has entered it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <remarks>
    /// This used to be a pair of constants - reach a 6, or a 3 with backup systems - written into
    /// this file. They are two more readings off somebody's rulebook, so they left with the three
    /// die tables in <c>HitResolution</c> and for the same reason.
    /// </remarks>
    public static int? Required(DirtsideRulesProfile profile, bool hasBackupSystems)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.SystemsDownRecoveryTarget(hasBackupSystems);
    }

    /// <summary>
    /// Whether the crew may try at all this activation.
    /// </summary>
    /// <param name="damagedOnActivation">The activation the marker was placed on.</param>
    /// <param name="currentActivation">The activation now being taken.</param>
    /// <param name="crewAboard">False once the crew have bailed out.</param>
    /// <returns>True when a roll may be made.</returns>
    /// <remarks>
    /// Strictly after, not on or after. Being able to try on the same activation would make a
    /// Systems Down result something a lucky vehicle simply shrugs off before it costs it anything.
    /// </remarks>
    public static bool CanAttempt(int damagedOnActivation, int currentActivation, bool crewAboard = true) =>
        crewAboard && currentActivation > damagedOnActivation;

    /// <summary>Makes one attempt.</summary>
    /// <param name="profile">The die and number this game's players entered off their own rulebook.</param>
    /// <param name="damagedOnActivation">The activation the marker was placed on.</param>
    /// <param name="currentActivation">The activation now being taken.</param>
    /// <param name="roller">Die source.</param>
    /// <param name="hasBackupSystems">Whether backup systems were bought at design time.</param>
    /// <param name="crewAboard">False once the crew have bailed out.</param>
    /// <returns>What the attempt produced, including the roll, for the log.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// A profile that does not carry this roll refuses the attempt and says which row is missing,
    /// rather than substituting a die and a number. It costs the crew nothing: the marker stays on
    /// and the combat action is not spent, so a table can fill the row in and try again.
    /// </remarks>
    public static RecoveryAttempt Attempt(
        DirtsideRulesProfile profile,
        int damagedOnActivation,
        int currentActivation,
        IQualityDiceRoller roller,
        bool hasBackupSystems = false,
        bool crewAboard = true)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(roller);

        if (profile.SystemsDownRecoveryDie is not { } die || Required(profile, hasBackupSystems) is not { } required)
        {
            return new RecoveryAttempt(false, 0, 0, false,
                "This game has no die table entry for getting a Systems Down marker off. Enter the "
                + "die and the number it has to reach in the game's rules profile - this app ships "
                + "no dice of its own.");
        }

        if (!CanAttempt(damagedOnActivation, currentActivation, crewAboard))
        {
            // Refused rather than rolled and failed, because the difference matters to the log and
            // because a rolled failure would burn a die a replay would then have to reproduce.
            return new RecoveryAttempt(false, 0, required, false, crewAboard
                ? "Repairs cannot start until an activation after the one the damage happened on."
                : "The crew have bailed out; there is nobody aboard to repair anything.");
        }

        var roll = roller.Roll(die);

        // Reach it, not beat it. This is one of the few rolls in the family that is not the
        // exceed-don't-match comparison, so it is written as a separate rule rather than borrowed
        // from the opposed-roll helpers.
        return new RecoveryAttempt(true, roll, required, roll >= required, null);
    }
}
