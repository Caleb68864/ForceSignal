using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.StarGrunt.Morale;

/// <summary>
/// How much fight a unit has left. The ladder runs from ready for anything down to running away,
/// and the lower it sinks the less the unit will agree to do.
/// </summary>
/// <remarks>
/// The numeric values run high-to-low so that losing a level is subtraction, which is how the
/// rules describe it - a failed test drops a unit one rung, a badly failed one drops it two.
/// </remarks>
public enum ConfidenceLevel
{
    /// <summary>Morale shattered. Out of the fight and heading for the back edge.</summary>
    Routed = 0,

    /// <summary>Morale almost gone. No longer willing to fight, only to get out of the way.</summary>
    Broken = 1,

    /// <summary>Distinctly worried. Reluctant to take a risk it would otherwise take.</summary>
    Shaken = 2,

    /// <summary>Morale holding. Generally still willing to fight.</summary>
    Steady = 3,

    /// <summary>Morale high. Ready for anything.</summary>
    Confident = 4,
}

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

/// <summary>The result of one confidence test, kept whole so a table can see why morale moved.</summary>
/// <param name="Roll">What the unit rolled.</param>
/// <param name="ScoreToBeat">Leadership value plus the threat level of what happened.</param>
/// <param name="LevelsLost">Zero, one, or two.</param>
/// <param name="Before">Where the unit stood before the test.</param>
/// <param name="After">Where it stands now.</param>
public readonly record struct ConfidenceTest(
    int Roll,
    int ScoreToBeat,
    int LevelsLost,
    ConfidenceLevel Before,
    ConfidenceLevel After)
{
    /// <summary>True when the unit held its nerve.</summary>
    public bool Passed => LevelsLost == 0;
}

/// <summary>
/// Morale: where a unit starts, what knocks it down, and how far a leader can bring it back.
/// </summary>
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
    /// <remarks>
    /// The roll has to exceed the score, as everywhere else in the game. Matching it is a failure
    /// and costs a level; rolling half the score or less means the unit came apart and costs two.
    /// A score the die cannot possibly beat is still rolled, because the question that remains is
    /// whether the unit loses one level or two.
    /// </remarks>
    /// <param name="current">Where the unit stands now.</param>
    /// <param name="quality">The unit's quality die.</param>
    /// <param name="leadershipValue">The leadership value on the unit's marker.</param>
    /// <param name="threatLevel">How serious the thing that happened was.</param>
    /// <param name="roller">Die source.</param>
    /// <returns>What the test did to the unit.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="roller"/> is null.</exception>
    public static ConfidenceTest Test(
        ConfidenceLevel current,
        QualityDie quality,
        int leadershipValue,
        int threatLevel,
        IQualityDiceRoller roller)
    {
        ArgumentNullException.ThrowIfNull(roller);

        var scoreToBeat = leadershipValue + threatLevel;
        var roll = roller.Roll(quality);

        var levelsLost = OpposedRolls.BeatsTarget(roll, scoreToBeat)
            ? 0
            // Half or less of the score is a rout in miniature. Integer halving rounds down, so
            // against a score of 5 it is a roll of 2 or less that costs two levels.
            : roll <= scoreToBeat / 2 ? 2 : 1;

        return new ConfidenceTest(roll, scoreToBeat, levelsLost, current, Drop(current, levelsLost));
    }

    /// <summary>Moves a unit down the ladder, never past the bottom of it.</summary>
    /// <param name="current">Where the unit stands now.</param>
    /// <param name="levels">How many rungs to drop.</param>
    /// <returns>Where it ends up.</returns>
    public static ConfidenceLevel Drop(ConfidenceLevel current, int levels) =>
        (ConfidenceLevel)Math.Max((int)ConfidenceLevel.Routed, (int)current - Math.Max(0, levels));

    /// <summary>
    /// Brings a unit back up the ladder, held to whatever its fatigue allows.
    /// </summary>
    /// <param name="current">Where the unit stands now.</param>
    /// <param name="levels">How many rungs to recover.</param>
    /// <param name="fatigue">How worn the force is, which caps the recovery.</param>
    /// <returns>Where it ends up.</returns>
    public static ConfidenceLevel Rally(ConfidenceLevel current, int levels, FatigueLevel fatigue)
    {
        var ceiling = (int)StartingLevel(fatigue);
        return (ConfidenceLevel)Math.Min(ceiling, (int)current + Math.Max(0, levels));
    }

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
