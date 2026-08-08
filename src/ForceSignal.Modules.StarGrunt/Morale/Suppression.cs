using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.StarGrunt.Morale;

/// <summary>The result of one attempt to get a unit's head back up.</summary>
/// <param name="Roll">What the leader rolled.</param>
/// <param name="ScoreToBeat">The leadership value the roll had to exceed.</param>
/// <param name="Cleared">True when one marker came off.</param>
/// <param name="Remaining">Markers still on the unit afterwards.</param>
public readonly record struct SuppressionRelief(int Roll, int ScoreToBeat, bool Cleared, int Remaining);

/// <summary>
/// Suppression - the ordinary currency of a firefight, and the thing most fire actually achieves.
/// </summary>
/// <remarks>
/// A suppressed unit is not hurt, it is pinned: it can look, talk, sort itself out under cover,
/// and try to get its head back up, and that is all. Clearing it is slow on purpose - each marker
/// is its own action and its own roll, so a unit caught under sustained fire stays out of the
/// fight for several turns without a single casualty.
/// </remarks>
public static class Suppression
{
    /// <summary>Most markers a unit can carry. Further fire has nothing left to add.</summary>
    public const int MaxMarkers = 3;

    /// <summary>Adds a marker, up to the ceiling.</summary>
    /// <param name="current">Markers already on the unit.</param>
    /// <returns>Markers after this fire.</returns>
    public static int Add(int current) => Math.Clamp(current + 1, 0, MaxMarkers);

    /// <summary>True when a unit carrying this many markers is pinned.</summary>
    /// <param name="markers">Markers on the unit.</param>
    /// <returns>True when the unit is suppressed at all.</returns>
    public static bool IsSuppressed(int markers) => markers > 0;

    /// <summary>
    /// Tries to shake off one marker. The unit's leader rolls its quality die and has to exceed
    /// the leadership value - one action, one roll, one marker at best.
    /// </summary>
    /// <param name="markers">Markers currently on the unit.</param>
    /// <param name="quality">The unit's quality die.</param>
    /// <param name="leadershipValue">The leadership value to beat.</param>
    /// <param name="roller">Die source.</param>
    /// <returns>What the attempt achieved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="roller"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The unit is not suppressed.</exception>
    public static SuppressionRelief TryClear(
        int markers,
        QualityDie quality,
        int leadershipValue,
        IQualityDiceRoller roller)
    {
        ArgumentNullException.ThrowIfNull(roller);
        if (!IsSuppressed(markers))
        {
            throw new InvalidOperationException("That unit is not suppressed.");
        }

        var roll = roller.Roll(quality);
        var cleared = OpposedRolls.BeatsTarget(roll, leadershipValue);
        return new SuppressionRelief(roll, leadershipValue, cleared, cleared ? markers - 1 : markers);
    }

    /// <summary>
    /// Whether a suppressed unit may take a given action at all. Everything that involves moving
    /// into the open or opening fire is off the table until the markers come off.
    /// </summary>
    /// <param name="markers">Markers on the unit.</param>
    /// <param name="action">The action being attempted.</param>
    /// <param name="isInCover">True when the unit is in cover, which reorganising requires.</param>
    /// <returns>True when the action is allowed.</returns>
    public static bool Allows(int markers, SuppressedAction action, bool isInCover = false) =>
        !IsSuppressed(markers) || action switch
        {
            SuppressedAction.Observe or SuppressedAction.Communicate or SuppressedAction.RemoveSuppression => true,
            SuppressedAction.Reorganise => isInCover,
            _ => false,
        };
}

/// <summary>The actions a pinned unit might try, for checking against its markers.</summary>
public enum SuppressedAction
{
    /// <summary>Move, in any form.</summary>
    Move,

    /// <summary>Open fire.</summary>
    Fire,

    /// <summary>Watch what is happening.</summary>
    Observe,

    /// <summary>Get a message out.</summary>
    Communicate,

    /// <summary>Sort the unit out. Only possible with something to hide behind.</summary>
    Reorganise,

    /// <summary>Try to get heads back up.</summary>
    RemoveSuppression,
}
