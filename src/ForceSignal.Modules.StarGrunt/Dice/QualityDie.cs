namespace ForceSignal.Modules.StarGrunt.Dice;

/// <summary>
/// A die on StarGrunt's quality ladder. The die type <em>is</em> the modifier in this game: rather
/// than adding a plus or a minus to a roll, good circumstances move a roll up the ladder and bad
/// ones move it down, and the number rolled is the number used.
/// </summary>
/// <remarks>
/// The value of each member is its face count, which is what a roller needs and what a range band
/// is measured in. There is no d20 anywhere in the game.
/// </remarks>
public enum QualityDie
{
    /// <summary>The bottom of the ladder.</summary>
    D4 = 4,

    /// <summary>Below average.</summary>
    D6 = 6,

    /// <summary>Average.</summary>
    D8 = 8,

    /// <summary>Above average.</summary>
    D10 = 10,

    /// <summary>The top of the ladder.</summary>
    D12 = 12,
}

/// <summary>
/// What a shift produced: where the die landed, and how many steps could not be applied because
/// the ladder ran out.
/// </summary>
/// <param name="Die">The die after as much of the shift as would fit.</param>
/// <param name="Overflow">
/// Steps left unapplied, signed the same way as the shift that was asked for. Positive means the
/// shift ran off the top of the ladder, negative off the bottom. A closed shift discards this; an
/// open shift hands it to the opponent as a shift the other way.
/// </param>
public readonly record struct DieShift(QualityDie Die, int Overflow)
{
    /// <summary>True when the whole shift fitted on the ladder.</summary>
    public bool IsClosed => Overflow == 0;
}

/// <summary>
/// A pair of dice in an opposed roll after both sides' shifts have been settled against each other.
/// </summary>
/// <param name="Actor">The acting side's die.</param>
/// <param name="Opponent">The opposing side's die.</param>
public readonly record struct OpposedDice(QualityDie Actor, QualityDie Opponent);

/// <summary>
/// Moving up and down the quality ladder. Every circumstance in StarGrunt that would be a numeric
/// modifier in another game is a shift here, so this is the arithmetic the whole engine rests on.
/// </summary>
public static class QualityDice
{
    private static readonly QualityDie[] LadderOrder =
        [QualityDie.D4, QualityDie.D6, QualityDie.D8, QualityDie.D10, QualityDie.D12];

    /// <summary>The ladder, worst to best.</summary>
    public static IReadOnlyList<QualityDie> Ladder => LadderOrder;

    /// <summary>Faces on a die, which is also the number a range band is measured in.</summary>
    /// <param name="die">The die to measure.</param>
    /// <returns>The face count.</returns>
    public static int Faces(QualityDie die) => (int)die;

    /// <summary>Where a die sits on the ladder, zero-based from the bottom.</summary>
    /// <param name="die">The die to locate.</param>
    /// <returns>The rung index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a die on the ladder.</exception>
    public static int Rung(QualityDie die)
    {
        var index = Array.IndexOf(LadderOrder, die);
        return index >= 0
            ? index
            : throw new ArgumentOutOfRangeException(nameof(die), die, "Not a die on the quality ladder.");
    }

    /// <summary>
    /// Shifts a die and reports what would not fit. This is the primitive; callers choose whether
    /// the leftover is discarded or handed to an opponent.
    /// </summary>
    /// <param name="die">The die to shift.</param>
    /// <param name="steps">Rungs to move: positive is better, negative is worse.</param>
    /// <returns>The shifted die and any unapplied steps.</returns>
    public static DieShift Shift(QualityDie die, int steps)
    {
        var target = Rung(die) + steps;
        var landed = Math.Clamp(target, 0, LadderOrder.Length - 1);
        return new DieShift(LadderOrder[landed], target - landed);
    }

    /// <summary>
    /// Shifts a die, discarding anything that runs off either end of the ladder. This is the
    /// default everywhere the rules do not say otherwise.
    /// </summary>
    /// <param name="die">The die to shift.</param>
    /// <param name="steps">Rungs to move: positive is better, negative is worse.</param>
    /// <returns>The shifted die.</returns>
    public static QualityDie ShiftClosed(QualityDie die, int steps) => Shift(die, steps).Die;

    /// <summary>
    /// Settles both sides' shifts in an opposed roll, with the leftovers crossing over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In an open shift, a shift that runs past the end of the ladder is not simply lost - the
    /// excess is applied to the opposing die in the other direction. Troops whose armour is already
    /// as good as the ladder goes still benefit from more cover, by making the incoming weapon
    /// worse instead.
    /// </para>
    /// <para>
    /// The crossed-over excess is applied as a closed shift, so it cannot bounce back again. The
    /// rules describe one hand-off, not a rally between the two dice, and a rally would not
    /// terminate in the general case.
    /// </para>
    /// </remarks>
    /// <param name="actor">The acting side's die before shifts.</param>
    /// <param name="actorSteps">Rungs the acting side's die moves.</param>
    /// <param name="opponent">The opposing side's die before shifts.</param>
    /// <param name="opponentSteps">Rungs the opposing side's die moves.</param>
    /// <returns>Both dice after the shifts and their crossed-over leftovers.</returns>
    public static OpposedDice ShiftOpposed(
        QualityDie actor,
        int actorSteps,
        QualityDie opponent,
        int opponentSteps)
    {
        var actorShift = Shift(actor, actorSteps);
        var opponentShift = Shift(opponent, opponentSteps);

        // Each side's leftover pushes the other die the opposite way. Both leftovers are read from
        // the first pass, so the order the two are applied in cannot change the answer.
        return new OpposedDice(
            ShiftClosed(actorShift.Die, -opponentShift.Overflow),
            ShiftClosed(opponentShift.Die, -actorShift.Overflow));
    }
}
