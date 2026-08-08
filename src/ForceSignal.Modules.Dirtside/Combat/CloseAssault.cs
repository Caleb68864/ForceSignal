using System.Collections.ObjectModel;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;

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
/// The unit behind one side of an assault, as the tests need it rather than as the exchange does.
/// </summary>
/// <param name="Quality">The die this unit rolls.</param>
/// <param name="LeadershipValue">The number on its command marker.</param>
/// <param name="Confidence">Where its confidence marker stands.</param>
/// <param name="Kind">Which column of the effects table it reads.</param>
/// <remarks>
/// Kept apart from <see cref="AssaultSide"/> on purpose. That one is the stands and their chits -
/// what fights - and this is the unit's nerve, which is what decides whether the fight happens at
/// all. A vehicle backing its own infantry's assault contributes stands without being the unit whose
/// confidence is on the line.
/// </remarks>
public sealed record AssaultantProfile(
    QualityDie Quality,
    int LeadershipValue,
    ConfidenceLevel Confidence,
    DirtsideUnitKind Kind);

/// <summary>How the defender answered an assault going in.</summary>
/// <param name="Stands">True when the defender is still on the position and the exchange happens.</param>
/// <param name="Test">
/// The test it took, or null when its nerve had already gone and no test was called for.
/// </param>
/// <param name="Confidence">Where the defender's marker stands now.</param>
public sealed record AssaultDefence(bool Stands, ConfidenceTest? Test, ConfidenceLevel Confidence)
{
    /// <summary>True when the defender gave up the position instead of receiving the assault.</summary>
    public bool Withdraws => !Stands;
}

/// <summary>Where a round of assault left the fight.</summary>
public enum AssaultOutcome
{
    /// <summary>The defender has had enough. The position is the attacker's.</summary>
    AttackerTakesThePosition = 0,

    /// <summary>The attacker has had enough. The position holds.</summary>
    AttackerFallsBack = 1,

    /// <summary>Neither side would let go. They go again.</summary>
    AnotherRound = 2,
}

/// <summary>What the tests after one round of the exchange came to.</summary>
/// <param name="Outcome">Where the fight stands.</param>
/// <param name="DefenderTest">The defender's test, which is always taken.</param>
/// <param name="AttackerTest">
/// The attacker's test, or null when the defender broke first and there was nothing left to ask.
/// </param>
public sealed record AssaultAftermath(
    AssaultOutcome Outcome,
    ConfidenceTest DefenderTest,
    ConfidenceTest? AttackerTest)
{
    /// <summary>
    /// True when the defender pulled out, and so comes away under fire.
    /// </summary>
    /// <remarks>
    /// Any unit that falls back from an assault is left marked. Reported here rather than applied,
    /// because the marker lives on the caller's own board.
    /// </remarks>
    public bool DefenderLeftUnderFire => Outcome == AssaultOutcome.AttackerTakesThePosition;

    /// <summary>True when the attacker pulled out, and so comes away under fire.</summary>
    public bool AttackerLeftUnderFire => Outcome == AssaultOutcome.AttackerFallsBack;

    /// <summary>True when the exchange goes another round with the defender's cover no longer counting.</summary>
    public bool Continues => Outcome == AssaultOutcome.AnotherRound;
}

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
/// <b>The nerve is as much of the fight as the chits are.</b> An assault is four tests around one
/// exchange: the attacker's nerve to launch it, the defender's to stand and receive it, one from
/// each side after every round to see who has had enough, and the winner's to follow through instead
/// of consolidating. Those are all here now, on top of <see cref="ConfidenceLadder"/> rather than in
/// a second copy of it, and the exchange feeds them the casualty share they read.
/// </para>
/// <para>
/// Every threat level those tests use is a parameter. The rules set them from the confidence of the
/// unit being asked, what kind of troops came at it, and how badly the round went - and every one of
/// those numbers is on the caller's own record card, not in here.
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

    /// <summary>Whether this unit's nerve is up to going in at all.</summary>
    /// <param name="attacker">The unit being ordered forward.</param>
    /// <returns>True when the order may be given.</returns>
    /// <remarks>
    /// A separate question from the test, and asked before it, because a unit whose confidence has
    /// already gone is not refused by a bad roll - it is refused outright, and no die is thrown.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="attacker"/> is null.</exception>
    public static bool MayLaunch(AssaultantProfile attacker)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        return !DirtsideConfidence.Restrictions(attacker.Confidence, attacker.Kind)
            .HasFlag(ConfidenceRestriction.MayNotCloseAssault);
    }

    /// <summary>
    /// The attacker's test to launch: will these troops actually go in?
    /// </summary>
    /// <param name="attacker">The unit going in.</param>
    /// <param name="threatLevel">
    /// How much is being asked of it, which the rules set from its own confidence. The caller's
    /// number.
    /// </param>
    /// <param name="roller">Die source.</param>
    /// <returns>Whether the assault goes in.</returns>
    /// <remarks>
    /// A reaction test rather than a confidence test, and the distinction is the whole of what a
    /// failure costs: troops that will not charge have not lost their nerve, they have lost their
    /// combat action. They may still move normally, and may be ordered forward again next activation.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">This unit's confidence forbids assaulting at all.</exception>
    public static ReactionTest Launch(
        AssaultantProfile attacker,
        int threatLevel,
        IQualityDiceRoller roller)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(roller);

        if (!MayLaunch(attacker))
        {
            throw new InvalidOperationException(
                $"A {attacker.Confidence} unit will not close-assault at all, so there is no test to take.");
        }

        return ConfidenceLadder.React(attacker.Quality, attacker.LeadershipValue, threatLevel, roller);
    }

    /// <summary>
    /// The defender's test to stand and receive the assault, or give up the position.
    /// </summary>
    /// <param name="defender">The unit holding the position.</param>
    /// <param name="threatLevel">
    /// How frightening what is coming at it is - the rules key this off the sort of troops making the
    /// assault. The caller's number.
    /// </param>
    /// <param name="roller">Die source.</param>
    /// <returns>Whether there is a fight, and where the defender's marker ended up.</returns>
    /// <remarks>
    /// <para>
    /// A confidence test, not a reaction test: failing costs the position <em>and</em> morale, which
    /// is why a defender can be driven off a position without a shot being exchanged.
    /// </para>
    /// <para>
    /// Some defenders never get as far as the roll. A crew whose nerve is already going does not
    /// fight infantry climbing onto the hull - it breaks and drives, and the null test that comes back
    /// is how a caller can tell the difference between a defender that failed and one that never
    /// tried.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static AssaultDefence StandOrWithdraw(
        AssaultantProfile defender,
        int threatLevel,
        IQualityDiceRoller roller)
    {
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(roller);

        var broken = DirtsideConfidence.OnCloseAssaulted(defender.Confidence, defender.Kind);
        if (broken != defender.Confidence)
        {
            return new AssaultDefence(Stands: false, Test: null, broken);
        }

        var test = ConfidenceLadder.Test(
            defender.Confidence, defender.Quality, defender.LeadershipValue, threatLevel, roller);

        return new AssaultDefence(test.Passed, test, test.After);
    }

    /// <summary>
    /// Which threat level a side tests at after a round, given how the round went for it.
    /// </summary>
    /// <param name="side">That side's part in the round.</param>
    /// <param name="lightCasualtyThreat">The threat level for a side that got off lightly.</param>
    /// <param name="heavyCasualtyThreat">The threat level for a side that was cut up.</param>
    /// <returns>The threat level to test at.</returns>
    /// <remarks>
    /// The only thing the exchange tells the tests, and the reason <see cref="AssaultSideResult"/>
    /// works out a casualty share it never acts on itself. Both numbers are the caller's.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="side"/> is null.</exception>
    public static int ThreatAfterRound(
        AssaultSideResult side,
        int lightCasualtyThreat,
        int heavyCasualtyThreat)
    {
        ArgumentNullException.ThrowIfNull(side);
        return side.LostHalfOrMore ? heavyCasualtyThreat : lightCasualtyThreat;
    }

    /// <summary>
    /// The tests after one round: who, if anybody, has had enough.
    /// </summary>
    /// <param name="round">The round just fought.</param>
    /// <param name="attacker">The attacking unit's nerve.</param>
    /// <param name="defender">The defending unit's nerve.</param>
    /// <param name="lightCasualtyThreat">The threat level for a side that got off lightly.</param>
    /// <param name="heavyCasualtyThreat">The threat level for a side that was cut up.</param>
    /// <param name="roller">Die source.</param>
    /// <returns>Where the fight stands, and both tests that got it there.</returns>
    /// <remarks>
    /// <para>
    /// The order is a rule and not an implementation detail. The defender tests first, and if he
    /// breaks the attacker is never asked - so an attacker who was himself cut to pieces still takes
    /// the position, and keeps whatever nerve he had left. Testing both and comparing would give a
    /// mutual collapse the rules have no room for.
    /// </para>
    /// <para>
    /// Each side tests at its <em>own</em> casualty share, which is why the two threats come in as a
    /// pair rather than one number chosen by the caller.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static AssaultAftermath ResolveAftermath(
        AssaultRound round,
        AssaultantProfile attacker,
        AssaultantProfile defender,
        int lightCasualtyThreat,
        int heavyCasualtyThreat,
        IQualityDiceRoller roller)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(roller);

        var defenderTest = ConfidenceLadder.Test(
            defender.Confidence,
            defender.Quality,
            defender.LeadershipValue,
            ThreatAfterRound(round.Defender, lightCasualtyThreat, heavyCasualtyThreat),
            roller);

        if (!defenderTest.Passed)
        {
            return new AssaultAftermath(
                AssaultOutcome.AttackerTakesThePosition, defenderTest, AttackerTest: null);
        }

        var attackerTest = ConfidenceLadder.Test(
            attacker.Confidence,
            attacker.Quality,
            attacker.LeadershipValue,
            ThreatAfterRound(round.Attacker, lightCasualtyThreat, heavyCasualtyThreat),
            roller);

        return new AssaultAftermath(
            attackerTest.Passed ? AssaultOutcome.AnotherRound : AssaultOutcome.AttackerFallsBack,
            defenderTest,
            attackerTest);
    }

    /// <summary>
    /// The winner's test to keep going through the position rather than stop on it.
    /// </summary>
    /// <param name="attacker">The unit that took the position.</param>
    /// <param name="threatLevel">
    /// How much is being asked, which the rules key off whether the defenders were destroyed or
    /// merely pushed back. The caller's number.
    /// </param>
    /// <param name="roller">Die source.</param>
    /// <returns>Whether the follow-through happens.</returns>
    /// <remarks>
    /// A pass buys a whole extra activation on the spot, driving through the captured position - which
    /// is the one thing in this game that hands a unit a second go without the sequence layer's
    /// transfer machinery. A failure costs nothing but the opportunity; the position is still taken.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static ReactionTest FollowThrough(
        AssaultantProfile attacker,
        int threatLevel,
        IQualityDiceRoller roller)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(roller);

        return ConfidenceLadder.React(attacker.Quality, attacker.LeadershipValue, threatLevel, roller);
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
