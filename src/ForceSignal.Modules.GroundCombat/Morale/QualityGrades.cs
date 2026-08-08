using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.GroundCombat.Morale;

/// <summary>
/// One game's grades of troops, and which rung of the quality ladder each of them rolls on.
/// </summary>
/// <typeparam name="TGrade">
/// Whatever the caller grades troops by - an enum of its own, or the name printed on its marker.
/// </typeparam>
/// <remarks>
/// <para>
/// This is a lookup rather than an enum in the core because the two games do not have the same
/// number of grades: one has a top grade the other simply has no room for. A fixed enum down here
/// would either invent a grade for the game that lacks it or deny one to the game that has it, and
/// every rule that reads a grade would then have a case that cannot happen.
/// </para>
/// <para>
/// The mapping itself is <b>not shipped</b>. Which die a grade rolls is a table off the user's own
/// rules, so it is filled in per game, per force, by the caller - the same policy that keeps
/// firepower ratings and threat levels out of this repository. What the engine supplies is the
/// ladder, the check that every grade lands on a rung of it, and one place to ask.
/// </para>
/// </remarks>
public sealed class QualityGrades<TGrade>
    where TGrade : notnull
{
    private readonly Dictionary<TGrade, QualityDie> dice;

    /// <summary>Builds a lookup from the caller's own grade-to-die table.</summary>
    /// <param name="grades">Each grade the game recognises, and the die it rolls.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grades"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A grade appears twice, or one of them names a die that is not on the quality ladder.
    /// </exception>
    public QualityGrades(IEnumerable<KeyValuePair<TGrade, QualityDie>> grades)
    {
        ArgumentNullException.ThrowIfNull(grades);

        dice = [];
        foreach (var (grade, die) in grades)
        {
            // Rung throws on anything that is not a die on the ladder, which is the point of asking:
            // a typo in a caller's table is caught while the force is being built rather than in the
            // middle of a test nobody can re-roll.
            _ = QualityDice.Rung(die);

            if (!dice.TryAdd(grade, die))
            {
                throw new ArgumentException($"The grade '{grade}' is listed twice.", nameof(grades));
            }
        }
    }

    /// <summary>How many grades this game recognises.</summary>
    public int Count => dice.Count;

    /// <summary>Every grade in the table.</summary>
    public IReadOnlyCollection<TGrade> Grades => dice.Keys;

    /// <summary>The die a grade of troops rolls.</summary>
    /// <param name="grade">The grade to look up.</param>
    /// <returns>Its die.</returns>
    /// <exception cref="ArgumentException">This game has no such grade.</exception>
    public QualityDie DieFor(TGrade grade) =>
        dice.TryGetValue(grade, out var die)
            ? die
            : throw new ArgumentException($"This game has no grade called '{grade}'.", nameof(grade));

    /// <summary>The die a grade rolls, without throwing when the grade is not one of this game's.</summary>
    /// <param name="grade">The grade to look up.</param>
    /// <param name="die">Its die, when there is one.</param>
    /// <returns>True when the grade is in the table.</returns>
    public bool TryDieFor(TGrade grade, out QualityDie die) => dice.TryGetValue(grade, out die);
}
