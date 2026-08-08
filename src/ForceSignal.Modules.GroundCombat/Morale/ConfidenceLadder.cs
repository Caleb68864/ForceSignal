using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.GroundCombat.Morale;

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
/// The result of one reaction test: the same arithmetic, with nothing but the order at stake.
/// </summary>
/// <param name="Roll">What the unit rolled.</param>
/// <param name="ScoreToBeat">Leadership value plus the threat level of what was asked of it.</param>
/// <param name="Passed">True when the troops did as they were told.</param>
/// <remarks>
/// There is no badly-failed tier here and its absence is the whole difference between the two tests.
/// A reaction test cannot cost a level, so how far short the roll fell changes nothing: the unit
/// either carries out the action or it does not, and a failure costs it the action rather than its
/// nerve.
/// </remarks>
public readonly record struct ReactionTest(int Roll, int ScoreToBeat, bool Passed);

/// <summary>
/// The confidence ladder and the one procedure both games test against it with.
/// </summary>
/// <remarks>
/// <para>
/// Both rulebooks describe this identically, down to the wording: add the threat level of the
/// circumstance to the unit's leadership value, roll the unit's quality die, and <em>exceed</em> the
/// total. Failing costs a confidence level; rolling half the score or less costs two. Threat levels
/// never add up - when several circumstances apply at once only the single highest counts.
/// </para>
/// <para>
/// Nothing here knows a number. Threat levels, leadership values and which die a grade of troops
/// rolls are all the caller's, off its own record card; see <see cref="QualityGrades{TGrade}"/> for
/// the last of those.
/// </para>
/// <para>
/// Nothing here rolls on the caller's behalf either, beyond the die it is handed. Deciding
/// <em>that</em> a test is owed is a game-layer judgement - the two games trigger on different
/// events - so this class only ever answers a test that has already been called for.
/// </para>
/// </remarks>
public static class ConfidenceLadder
{
    /// <summary>The top of the ladder, and the ceiling on any rally that has no tighter one.</summary>
    public static ConfidenceLevel Best => ConfidenceLevel.Confident;

    /// <summary>The bottom of the ladder. Nothing drops past it.</summary>
    public static ConfidenceLevel Worst => ConfidenceLevel.Routed;

    /// <summary>
    /// The threat level to test against when more than one circumstance applies.
    /// </summary>
    /// <param name="threatLevels">Every threat level in play, in the caller's own numbers.</param>
    /// <returns>The single highest, or zero when nothing applies.</returns>
    /// <remarks>
    /// The single highest, never the sum. This is a rule rather than a convenience: a unit that has
    /// just lost its leader while taking heavy casualties tests once, against the worse of the two.
    /// Adding them would roughly double how fast morale collapses in exactly the situations where the
    /// rules mean it to hold.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="threatLevels"/> is null.</exception>
    public static int Threat(params int[] threatLevels)
    {
        ArgumentNullException.ThrowIfNull(threatLevels);
        return threatLevels.Length == 0 ? 0 : threatLevels.Max();
    }

    /// <summary>The number a test has to beat.</summary>
    /// <param name="leadershipValue">The leadership value on the unit's marker.</param>
    /// <param name="threatLevel">How serious the circumstance is.</param>
    /// <returns>The score to exceed.</returns>
    public static int ScoreToBeat(int leadershipValue, int threatLevel) => leadershipValue + threatLevel;

    /// <summary>
    /// Takes one confidence test, with a level of morale at stake.
    /// </summary>
    /// <remarks>
    /// The roll has to exceed the score, as everywhere else in both games. Matching it is a failure
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

        var score = ScoreToBeat(leadershipValue, threatLevel);
        var roll = roller.Roll(quality);
        var levelsLost = LevelsLost(roll, score);

        return new ConfidenceTest(roll, score, levelsLost, current, Drop(current, levelsLost));
    }

    /// <summary>
    /// Takes one reaction test, with nothing but the order at stake.
    /// </summary>
    /// <param name="quality">The unit's quality die.</param>
    /// <param name="leadershipValue">The leadership value on the unit's marker.</param>
    /// <param name="threatLevel">How much the troops are being asked to swallow.</param>
    /// <param name="roller">Die source.</param>
    /// <returns>Whether the unit will do as it is told.</returns>
    /// <remarks>
    /// Identical arithmetic to a confidence test, and a deliberately different return, because the
    /// consequence is where the two part company. A failed reaction test costs the action - the unit
    /// stays put and may be ordered to try again - and never a confidence level. Handing back a
    /// <see cref="ConfidenceTest"/> with a level attached would invite a caller to apply it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="roller"/> is null.</exception>
    public static ReactionTest React(
        QualityDie quality,
        int leadershipValue,
        int threatLevel,
        IQualityDiceRoller roller)
    {
        ArgumentNullException.ThrowIfNull(roller);

        var score = ScoreToBeat(leadershipValue, threatLevel);
        var roll = roller.Roll(quality);

        return new ReactionTest(roll, score, OpposedRolls.BeatsTarget(roll, score));
    }

    /// <summary>Moves a unit down the ladder, never past the bottom of it.</summary>
    /// <param name="current">Where the unit stands now.</param>
    /// <param name="levels">How many rungs to drop.</param>
    /// <returns>Where it ends up.</returns>
    public static ConfidenceLevel Drop(ConfidenceLevel current, int levels) =>
        (ConfidenceLevel)Math.Max((int)Worst, (int)current - Math.Max(0, levels));

    /// <summary>
    /// Brings a unit back up the ladder, held to a ceiling.
    /// </summary>
    /// <param name="current">Where the unit stands now.</param>
    /// <param name="levels">How many rungs to recover.</param>
    /// <param name="ceiling">The best the unit may be brought back to.</param>
    /// <returns>Where it ends up.</returns>
    /// <remarks>
    /// The ceiling is a parameter rather than a fixed top rung because the games disagree about what
    /// caps a rally: one has a force that took the field tired never recovering its edge, and the
    /// other has nothing of the sort. Passing <see cref="Best"/> is the uncapped case.
    /// </remarks>
    public static ConfidenceLevel Raise(ConfidenceLevel current, int levels, ConfidenceLevel ceiling) =>
        (ConfidenceLevel)Math.Min((int)ceiling, (int)current + Math.Max(0, levels));

    /// <summary>
    /// How badly one roll missed, in levels.
    /// </summary>
    /// <param name="roll">What was rolled.</param>
    /// <param name="scoreToBeat">The score it had to exceed.</param>
    /// <returns>Zero, one, or two.</returns>
    public static int LevelsLost(int roll, int scoreToBeat) =>
        OpposedRolls.BeatsTarget(roll, scoreToBeat)
            ? 0

            // Half or less of the score is a rout in miniature. Integer halving rounds down, so
            // against a score of 5 it is a roll of 2 or less that costs two levels.
            : roll <= scoreToBeat / 2 ? 2 : 1;
}
