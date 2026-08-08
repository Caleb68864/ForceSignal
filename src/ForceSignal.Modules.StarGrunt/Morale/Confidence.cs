using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;

namespace ForceSignal.Modules.StarGrunt.Morale;

/// <summary>
/// How worn out a force is. It sets where a unit starts on the confidence ladder, and it is also a
/// ceiling: a unit that began tired can never be rallied back above where it began.
/// </summary>
public enum FatigueLevel
{
    /// <summary>Rested. Starts confident.</summary>
    Fresh = 0,

    /// <summary>Worn. Starts steady, and cannot be rallied past it.</summary>
    Tired = 1,

    /// <summary>Spent. Starts shaken, and cannot be rallied past it.</summary>
    Exhausted = 2,
}

/// <summary>
/// StarGrunt's half of morale: where a unit starts, how far a leader can bring it back, and what
/// each rung of the ladder stops it doing.
/// </summary>
/// <remarks>
/// <para>
/// The ladder itself and the test procedure are not here. Both games have the five levels and the
/// same roll - quality die against leadership plus threat, exceed to hold, half or less costs two -
/// so they live in <see cref="ConfidenceLadder"/> and this class consumes them.
/// </para>
/// <para>
/// What stayed is what differs. Fatigue is StarGrunt's alone, and it is two rules wearing one name:
/// where a unit starts, and the best it can ever be rallied back to. The restrictions below are
/// cumulative down the ladder for every unit in the game, which is <em>not</em> how the other game
/// works - there the effects table has a column for foot and a column for armour - so an effects
/// table has no business in the shared layer at all.
/// </para>
/// </remarks>
public static class Confidence
{
    /// <summary>
    /// Where a unit begins, and the best it can ever be rallied back to. A force that took the
    /// field already tired does not recover its edge during the battle.
    /// </summary>
    /// <param name="fatigue">How worn the force is.</param>
    /// <returns>The starting and maximum confidence level.</returns>
    public static ConfidenceLevel StartingLevel(FatigueLevel fatigue) => fatigue switch
    {
        FatigueLevel.Fresh => ConfidenceLevel.Confident,
        FatigueLevel.Tired => ConfidenceLevel.Steady,
        _ => ConfidenceLevel.Shaken,
    };

    /// <summary>
    /// Takes one confidence test.
    /// </summary>
    /// <param name="current">Where the unit stands now.</param>
    /// <param name="quality">The unit's quality die.</param>
    /// <param name="leadershipValue">The leadership value on the unit's marker.</param>
    /// <param name="threatLevel">How serious the thing that happened was.</param>
    /// <param name="roller">Die source.</param>
    /// <returns>What the test did to the unit.</returns>
    /// <remarks>
    /// The procedure is the shared one and this only names it in StarGrunt's vocabulary. Kept as a
    /// way in so that a caller working in one game does not have to know which parts of morale are
    /// common property and which are not.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="roller"/> is null.</exception>
    public static ConfidenceTest Test(
        ConfidenceLevel current,
        QualityDie quality,
        int leadershipValue,
        int threatLevel,
        IQualityDiceRoller roller) =>
        ConfidenceLadder.Test(current, quality, leadershipValue, threatLevel, roller);

    /// <summary>
    /// Takes one reaction test: the same roll, with the action rather than the unit's nerve at stake.
    /// </summary>
    /// <param name="quality">The unit's quality die.</param>
    /// <param name="leadershipValue">The leadership value on the unit's marker.</param>
    /// <param name="threatLevel">How much the troops are being asked to swallow.</param>
    /// <param name="roller">Die source.</param>
    /// <returns>Whether the unit will do as it is told.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="roller"/> is null.</exception>
    public static ReactionTest React(
        QualityDie quality,
        int leadershipValue,
        int threatLevel,
        IQualityDiceRoller roller) =>
        ConfidenceLadder.React(quality, leadershipValue, threatLevel, roller);

    /// <summary>Moves a unit down the ladder, never past the bottom of it.</summary>
    /// <param name="current">Where the unit stands now.</param>
    /// <param name="levels">How many rungs to drop.</param>
    /// <returns>Where it ends up.</returns>
    public static ConfidenceLevel Drop(ConfidenceLevel current, int levels) =>
        ConfidenceLadder.Drop(current, levels);

    /// <summary>
    /// Brings a unit back up the ladder, held to whatever its fatigue allows.
    /// </summary>
    /// <param name="current">Where the unit stands now.</param>
    /// <param name="levels">How many rungs to recover.</param>
    /// <param name="fatigue">How worn the force is, which caps the recovery.</param>
    /// <returns>Where it ends up.</returns>
    public static ConfidenceLevel Rally(ConfidenceLevel current, int levels, FatigueLevel fatigue) =>
        ConfidenceLadder.Raise(current, levels, StartingLevel(fatigue));

    /// <summary>
    /// True when a unit at this level needs to pass a reaction test before leaving cover or
    /// advancing on an enemy it can see. Restrictions are cumulative down the ladder.
    /// </summary>
    /// <param name="level">The unit's confidence.</param>
    /// <returns>True when the move is conditional rather than free.</returns>
    public static bool NeedsReactionTestToAdvance(ConfidenceLevel level) => level <= ConfidenceLevel.Shaken;

    /// <summary>
    /// True when a unit at this level will only shoot back at something that has already shot at
    /// it, rather than engaging whatever it likes.
    /// </summary>
    /// <param name="level">The unit's confidence.</param>
    /// <returns>True when the unit's targets are limited to those who fired on it.</returns>
    public static bool FiresOnlyAtWhoeverFiredOnIt(ConfidenceLevel level) => level == ConfidenceLevel.Broken;

    /// <summary>True when a unit at this level has stopped shooting altogether.</summary>
    /// <param name="level">The unit's confidence.</param>
    /// <returns>True when the unit fires at nothing.</returns>
    public static bool HasStoppedFighting(ConfidenceLevel level) => level == ConfidenceLevel.Routed;

    /// <summary>
    /// What being close-assaulted does to a unit that had already broken: it goes straight to the
    /// bottom rather than testing.
    /// </summary>
    /// <param name="level">The unit's confidence when the assault went in.</param>
    /// <returns>Where it ends up.</returns>
    public static ConfidenceLevel OnCloseAssaulted(ConfidenceLevel level) =>
        level <= ConfidenceLevel.Broken ? ConfidenceLevel.Routed : level;
}
