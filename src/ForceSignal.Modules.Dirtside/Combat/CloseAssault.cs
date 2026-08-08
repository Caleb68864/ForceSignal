using System.Collections.ObjectModel;
using ForceSignal.Modules.Dirtside.Chits;

namespace ForceSignal.Modules.Dirtside.Combat;

/// <summary>One side of a close assault, as the exchange needs it.</summary>
/// <param name="Id">Whatever the caller calls this side.</param>
/// <param name="Stands">Every stand present. All of them fight; there is no effectiveness check.</param>
/// <param name="Validity">
/// What this side's chits may count in the first round, set by the cover the <em>other</em> side is
/// in.
/// </param>
/// <param name="HandToHandValidity">
/// What this side's chits may count from the second round on, when the other side's cover has
/// stopped mattering. Null leaves it unchanged, which is the right answer for a side whose enemy had
/// no cover to lose.
/// </param>
public sealed record AssaultSide(
    string Id,
    IReadOnlyList<InfantryStand> Stands,
    ChitValidity Validity,
    ChitValidity? HandToHandValidity = null)
{
    /// <summary>The row this side draws on in a given round.</summary>
    /// <param name="round">The round being fought, counting from one.</param>
    /// <returns>The validity for that round.</returns>
    /// <remarks>
    /// Making the round the key is what stops the second-round rule from being something a caller has
    /// to remember to apply. It is the same trick as keying weapon validity by range band.
    /// </remarks>
    public ChitValidity ValidityForRound(int round) =>
        round >= CloseAssault.HandToHandFromRound ? HandToHandValidity ?? Validity : Validity;
}

/// <summary>One side's part in one round of the exchange.</summary>
/// <param name="SideId">Which side drew.</param>
/// <param name="Draws">One entry per stand, never added together.</param>
/// <param name="StandsPresent">How many stands this side started the round with.</param>
/// <param name="Losses">How many of this side's stands were removed. A count of winning enemy draws.</param>
public sealed record AssaultSideResult(
    string SideId,
    IReadOnlyList<StandFireResult> Draws,
    int StandsPresent,
    int Losses)
{
    /// <summary>Losses as a share of the stands present when the round began.</summary>
    public double CasualtyFraction => StandsPresent == 0 ? 0 : (double)Losses / StandsPresent;

    /// <summary>
    /// True when this side lost half its strength or more.
    /// </summary>
    /// <remarks>
    /// The one number the confidence test that follows the round cares about. Computed here and left
    /// here: running that test is the morale layer's job, not this one's.
    /// </remarks>
    public bool LostHalfOrMore => CasualtyFraction >= CloseAssault.HeavyCasualtyShare;
}

/// <summary>One round of a close assault, fought by both sides at once.</summary>
/// <param name="Round">Which round this was, counting from one.</param>
/// <param name="Attacker">The attacking side's draws and losses.</param>
/// <param name="Defender">The defending side's draws and losses.</param>
/// <param name="DefenderCoverStillCounted">
/// False from the second round on, when the fight is too close for cover to mean anything.
/// </param>
public sealed record AssaultRound(
    int Round,
    AssaultSideResult Attacker,
    AssaultSideResult Defender,
    bool DefenderCoverStillCounted);

/// <summary>
/// The close-assault exchange: the fight itself, once both sides have agreed to have it.
/// </summary>
/// <remarks>
/// <para>
/// Two properties carry this whole class, and both are about refusing to let one thing influence
/// another that the rules keep apart.
/// </para>
/// <para>
/// <b>Both sides fight simultaneously, and casualties are marked rather than removed.</b> A stand
/// killed in this round still draws its own chits in this round - it fired as it died. So every draw
/// on both sides is worked out against the strengths the round <em>began</em> with, and the losses
/// are applied only once all the drawing is finished. Resolving one side and then the other with the
/// dead already taken off the table would hand the side that happened to be resolved first a large
/// and entirely invented advantage.
/// </para>
/// <para>
/// <b>Chits are never pooled.</b> The same rule as the firefight, for the same reason: a side's
/// casualties are a count of successful individual draws, never a comparison of one grand total.
/// </para>
/// <para>
/// What is deliberately <em>not</em> here: the reaction test that launches the assault, the
/// defender's confidence test that decides whether it stands, the tests after each round that decide
/// whether either side falls back, and the follow-through test that turns a win into an overrun.
/// Every one of those is a confidence or reaction test, and this game's confidence ladder belongs in
/// the shared ground-combat layer, which has not been built. Writing a second copy of it in here to
/// get close assault finished would be the exact mistake the plan warns against, so the exchange
/// stops at its own edges and hands out the casualty shares those tests will need.
/// </para>
/// </remarks>
public static class CloseAssault
{
    /// <summary>From this round on, the defender's cover has stopped meaning anything.</summary>
    public const int HandToHandFromRound = 2;

    /// <summary>The casualty share that separates the two threat levels after a round.</summary>
    public const double HeavyCasualtyShare = 0.5;

    /// <summary>
    /// Fights one round of the exchange.
    /// </summary>
    /// <param name="attacker">The assaulting side.</param>
    /// <param name="defender">The side holding the position.</param>
    /// <param name="pot">The pot. Whole again between every single stand's draw.</param>
    /// <param name="round">Which round this is, counting from one.</param>
    /// <returns>Both sides' draws and what each of them cost.</returns>
    /// <remarks>
    /// There is no fire-effectiveness check anywhere in here. Every stand present fights, however
    /// badly led - at this range nobody is deciding whether to join in.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The round is less than one.</exception>
    public static AssaultRound ResolveRound(
        AssaultSide attacker,
        AssaultSide defender,
        IChitPot pot,
        int round = 1)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(pot);
        ArgumentOutOfRangeException.ThrowIfLessThan(round, 1);

        // Both sides draw before either side loses anybody. This is the simultaneity rule, and it is
        // structural here: the losses are not even counted until both lists exist.
        var attackerDraws = Draw(attacker, defender, round, pot);
        var defenderDraws = Draw(defender, attacker, round, pot);

        // A side's losses come from the *other* side's draws, and are capped at what it had - more
        // successful draws than there are stands is a very good round, not a negative unit.
        var attackerResult = new AssaultSideResult(
            attacker.Id,
            attackerDraws,
            attacker.Stands.Count,
            Math.Min(defenderDraws.Count(d => d.Destroyed), attacker.Stands.Count));

        var defenderResult = new AssaultSideResult(
            defender.Id,
            defenderDraws,
            defender.Stands.Count,
            Math.Min(attackerDraws.Count(d => d.Destroyed), defender.Stands.Count));

        return new AssaultRound(
            round,
            attackerResult,
            defenderResult,
            round < HandToHandFromRound);
    }

    /// <summary>Every stand on one side drawing against the other, each on its own.</summary>
    private static ReadOnlyCollection<StandFireResult> Draw(
        AssaultSide drawing,
        AssaultSide against,
        int round,
        IChitPot pot)
    {
        var validity = drawing.ValidityForRound(round);
        var results = new List<StandFireResult>(drawing.Stands.Count);
        if (against.Stands.Count == 0)
        {
            return new ReadOnlyCollection<StandFireResult>(results);
        }

        var threshold = UniformKillThreshold(against);
        foreach (var stand in drawing.Stands)
        {
            results.Add(InfantryCombat.ResolveDraw(
                stand.Id,
                stand.AssaultChits,
                validity,
                SmallArmsTarget.Stand(against.Id, threshold),
                pot));
        }

        return new ReadOnlyCollection<StandFireResult>(results);
    }

    /// <summary>
    /// The toughness of the side being fought, insisting that it has only one.
    /// </summary>
    /// <remarks>
    /// A close assault is one unit against one unit holding one position, and a unit's stands are of
    /// a kind, so a single threshold is the normal case. Refusing a mixed side rather than quietly
    /// taking the first stand's number is deliberate: the alternative is a silent wrong answer, and
    /// a caller with a genuinely mixed force wants to resolve it a stand at a time anyway.
    /// </remarks>
    private static int UniformKillThreshold(AssaultSide side)
    {
        var threshold = side.Stands[0].KillThreshold;
        return side.Stands.All(s => s.KillThreshold == threshold)
            ? threshold
            : throw new ArgumentException(
                $"The stands on side '{side.Id}' do not all have the same kill threshold, so there is "
                + "no single number for the other side to draw against. Resolve a mixed side one "
                + "stand at a time.",
                nameof(side));
    }
}
