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
    /// <summary>The number a vehicle without backup systems must reach on a D6.</summary>
    public const int RequiredWithoutBackup = 6;

    /// <summary>The number a vehicle with backup systems must reach on a D6.</summary>
    public const int RequiredWithBackup = 3;

    /// <summary>The number that has to come up.</summary>
    /// <param name="hasBackupSystems">Whether backup systems were bought at design time.</param>
    /// <returns>The number to reach or beat.</returns>
    public static int Required(bool hasBackupSystems) =>
        hasBackupSystems ? RequiredWithBackup : RequiredWithoutBackup;

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
    /// <param name="damagedOnActivation">The activation the marker was placed on.</param>
    /// <param name="currentActivation">The activation now being taken.</param>
    /// <param name="roller">Die source.</param>
    /// <param name="hasBackupSystems">Whether backup systems were bought at design time.</param>
    /// <param name="crewAboard">False once the crew have bailed out.</param>
    /// <returns>What the attempt produced, including the roll, for the log.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="roller"/> is null.</exception>
    public static RecoveryAttempt Attempt(
        int damagedOnActivation,
        int currentActivation,
        IQualityDiceRoller roller,
        bool hasBackupSystems = false,
        bool crewAboard = true)
    {
        ArgumentNullException.ThrowIfNull(roller);

        var required = Required(hasBackupSystems);
        if (!CanAttempt(damagedOnActivation, currentActivation, crewAboard))
        {
            // Refused rather than rolled and failed, because the difference matters to the log and
            // because a rolled failure would burn a die a replay would then have to reproduce.
            return new RecoveryAttempt(false, 0, required, false, crewAboard
                ? "Repairs cannot start until an activation after the one the damage happened on."
                : "The crew have bailed out; there is nobody aboard to repair anything.");
        }

        var roll = roller.Roll(QualityDie.D6);

        // Reach it, not beat it. This is one of the few rolls in the family that is not the
        // exceed-don't-match comparison, so it is written as a separate rule rather than borrowed
        // from the opposed-roll helpers.
        return new RecoveryAttempt(true, roll, required, roll >= required, null);
    }
}
