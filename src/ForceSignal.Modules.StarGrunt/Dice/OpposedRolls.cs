namespace ForceSignal.Modules.StarGrunt.Dice;

/// <summary>
/// How well a multiple-opposed roll went. The firer throws two or more dice against the target's
/// one, and what matters is how many of them beat it rather than by how much.
/// </summary>
public enum OpposedResult
{
    /// <summary>No firer die beat the target's. Nothing happens at all.</summary>
    Failed = 0,

    /// <summary>Exactly one firer die beat the target's. Enough to suppress, not to hurt.</summary>
    Suppressed = 1,

    /// <summary>Two or more firer dice beat the target's. The attack is fully effective.</summary>
    Effective = 2,
}

/// <summary>
/// The outcome of one multiple-opposed roll, kept whole so a table can audit it.
/// </summary>
/// <param name="ActorRolls">Every die the acting side rolled, in the order they were thrown.</param>
/// <param name="OpponentRoll">The single die the opposing side rolled.</param>
/// <param name="Beats">How many of the acting side's dice exceeded the opponent's.</param>
/// <param name="Result">What that count amounts to.</param>
public readonly record struct MultipleOpposedRoll(
    IReadOnlyList<int> ActorRolls,
    int OpponentRoll,
    int Beats,
    OpposedResult Result)
{
    /// <summary>
    /// Every acting die added together, losers included.
    /// </summary>
    /// <remarks>
    /// This total is what the fire engine divides to work out how many hits an effective attack
    /// scored, and it deliberately counts the dice that lost as well as the ones that won - a
    /// volley that mostly missed still put rounds downrange.
    /// </remarks>
    public int ActorTotal => ActorRolls.Sum();
}

/// <summary>
/// The three shapes of roll StarGrunt resolves nearly everything with.
/// </summary>
/// <remarks>
/// One rule runs through all of them: <b>a roll must exceed the number to succeed</b>. Equalling it
/// is a failure. Every comparison here is strictly greater-than for that reason, and it is the
/// single easiest thing to get wrong when reading the mechanics.
/// </remarks>
public static class OpposedRolls
{
    /// <summary>
    /// Rolls one die against a fixed number - the shape used for confidence tests, reaction tests,
    /// clearing suppression, and getting a message through.
    /// </summary>
    /// <param name="roll">The die result.</param>
    /// <param name="target">The number to beat.</param>
    /// <returns>True when the roll exceeded the target.</returns>
    public static bool BeatsTarget(int roll, int target) => roll > target;

    /// <summary>
    /// Resolves a single opposed roll, where both sides throw one die and the acting side has to
    /// come out on top.
    /// </summary>
    /// <param name="actorRoll">The acting side's die result.</param>
    /// <param name="opponentRoll">The opposing side's die result.</param>
    /// <returns>True when the acting side exceeded the opponent. A tie goes to the opponent.</returns>
    public static bool Wins(int actorRoll, int opponentRoll) => actorRoll > opponentRoll;

    /// <summary>
    /// Resolves a multiple-opposed roll: two or more acting dice against one opposing die, scored
    /// by how many of them beat it.
    /// </summary>
    /// <param name="actorRolls">The acting side's dice results.</param>
    /// <param name="opponentRoll">The opposing side's single die result.</param>
    /// <returns>The counted outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="actorRolls"/> is null.</exception>
    public static MultipleOpposedRoll Resolve(IReadOnlyList<int> actorRolls, int opponentRoll)
    {
        ArgumentNullException.ThrowIfNull(actorRolls);

        var beats = actorRolls.Count(roll => roll > opponentRoll);
        return new MultipleOpposedRoll(actorRolls, opponentRoll, beats, Score(beats));
    }

    /// <summary>What a count of winning dice amounts to.</summary>
    /// <param name="beats">How many acting dice exceeded the opposing die.</param>
    /// <returns>The tier that count reaches.</returns>
    public static OpposedResult Score(int beats) => beats switch
    {
        <= 0 => OpposedResult.Failed,
        1 => OpposedResult.Suppressed,
        _ => OpposedResult.Effective,
    };
}
