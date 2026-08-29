using System.Collections.Immutable;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Sequence;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Game;

/// <summary>Where a close assault has got to.</summary>
public enum AssaultStage
{
    /// <summary>The attacker has gone in. The defender has to say whether it stands or gives way.</summary>
    AwaitingDefender = 0,

    /// <summary>Both sides are on the position. A round of the exchange may be fought.</summary>
    AwaitingRound = 1,

    /// <summary>A round has been fought. Both sides owe the tests that decide who has had enough.</summary>
    AwaitingAftermath = 2,

    /// <summary>The position is the attacker's. It may test to drive on through it.</summary>
    AwaitingFollowThrough = 3,
}

/// <summary>One side of an open assault: which unit, and which of its elements are still on the position.</summary>
/// <param name="Unit">The platoon.</param>
/// <param name="Stands">The elements committed to the fight that have not yet been removed, in roster order.</param>
/// <param name="Validity">What this side's chits may count in the first round, off the player's card.</param>
/// <param name="HandToHandValidity">What they may count from the second round on, or null to leave it unchanged.</param>
public sealed record AssaultSideState(
    UnitId Unit,
    ImmutableArray<ElementId> Stands,
    ChitValidity Validity,
    ChitValidity? HandToHandValidity)
{
    /// <summary>Structural, because <see cref="ImmutableArray{T}"/> compares by reference.</summary>
    /// <param name="other">The side to compare with.</param>
    public bool Equals(AssaultSideState? other) =>
        other is not null
        && Unit == other.Unit
        && Validity == other.Validity
        && HandToHandValidity == other.HandToHandValidity
        && StructuralEquality.Sequence(Stands, other.Stands);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Unit, Validity, HandToHandValidity, StructuralEquality.SequenceHash(Stands));
}

/// <summary>What the last round of an assault cost each side, kept for the tests that follow it.</summary>
/// <param name="AttackerStands">How many stands the attacker began the round with.</param>
/// <param name="AttackerLosses">How many it lost.</param>
/// <param name="DefenderStands">How many stands the defender began the round with.</param>
/// <param name="DefenderLosses">How many it lost.</param>
public readonly record struct AssaultRoundLosses(
    int AttackerStands,
    int AttackerLosses,
    int DefenderStands,
    int DefenderLosses);

/// <summary>
/// A close assault part-way through being fought.
/// </summary>
/// <param name="Attacker">The side that went in.</param>
/// <param name="DefenderUnit">The platoon holding the position.</param>
/// <param name="Defender">That platoon's side of the exchange, once it has said which of its elements fight. Null until then.</param>
/// <param name="Stage">What is owed next.</param>
/// <param name="Round">The round about to be fought, or just fought, counting from one.</param>
/// <param name="LastRound">What the round just fought cost, or null before the first one.</param>
/// <remarks>
/// A value on the game rather than a frame on the session. A confidence test is not an interrupt
/// window, as the activation policy records, and neither is the exchange around it: nobody else
/// chooses to answer, and the defender is spent rather than acting. What is left to keep is which two
/// units are locked together, who is still standing, and which of the four tests comes next.
/// </remarks>
public sealed record DirtsideAssault(
    AssaultSideState Attacker,
    UnitId DefenderUnit,
    AssaultSideState? Defender,
    AssaultStage Stage,
    int Round,
    AssaultRoundLosses? LastRound);

/// <summary>What a side commits to a close assault, as the game takes it.</summary>
/// <param name="Stands">The elements going in, or holding the position.</param>
/// <param name="Validity">What their chits may count in the first round, set by the other side's cover.</param>
/// <param name="HandToHandValidity">What they may count from the second round on, or null when the other side had no cover to lose.</param>
/// <param name="ThreatLevel">The threat level the side's test is taken at. The player's number.</param>
public sealed record AssaultCommitment(
    ImmutableArray<ElementId> Stands,
    ChitValidity Validity,
    ChitValidity? HandToHandValidity,
    int ThreatLevel);

public sealed partial record DirtsideGame
{
    /// <summary>
    /// Orders the activated platoon in against a position, and rolls its nerve to go.
    /// </summary>
    /// <param name="defender">The platoon holding the position.</param>
    /// <param name="commitment">Which elements go in, what their chits may count, and what the order asks of them.</param>
    /// <param name="dice">Where the die result comes from.</param>
    /// <returns>The game with the assault launched or refused by the troops, or why it could not be ordered.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <para>
    /// Every committed element spends its combat action here, whether or not the troops go. That is
    /// the reaction-test rule the resolver describes: a unit that will not charge has lost its
    /// combat action, not its nerve, and may be ordered forward again next activation. The steps
    /// are taken before the die is thrown so that an order the sequence would refuse - a broken
    /// unit, an element that has already acted - costs nothing and rolls nothing.
    /// </para>
    /// <para>
    /// Being assaulted spends the defender's activation on the spot. It fights the exchange all the
    /// same, which is why it is spent here and not given a frame.
    /// </para>
    /// </remarks>
    public GameOutcome<DirtsideGame> LaunchAssault(UnitId defender, AssaultCommitment commitment, IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(dice);

        if (WhyLaunchIsRefused(defender, commitment) is { } reason)
        {
            return GameOutcome.Refused<DirtsideGame>(reason);
        }

        var attacker = Session.CurrentFrame!.Unit;
        var going = Unit(attacker);
        var holding = Unit(defender);

        var committed = this;
        foreach (var element in commitment.Stands)
        {
            var stepped = committed.TakeStep(DirtsideSteps.Act(DirtsideAction.CloseAssault, element));
            if (!stepped.IsAllowed)
            {
                return stepped;
            }

            committed = stepped.Value!;
        }

        var test = CloseAssault.Launch(Profile(attacker), commitment.ThreatLevel, dice);
        var stands = string.Join(", ", commitment.Stands.Select(element => going.Element(element)!.Name));

        if (!test.Passed)
        {
            return GameOutcome.Allowed(committed.WithLog(
                $"{going.Name} was ordered in on {holding.Name} with {stands}: rolled {test.Roll} against "
                + $"{test.ScoreToBeat} and would not go. The order may be given again next activation."));
        }

        return GameOutcome.Allowed((committed with
        {
            Session = DirtsideTurn.CloseAssaulted(committed.Session, holding.Side, defender),
            Assault = new DirtsideAssault(
                new AssaultSideState(attacker, commitment.Stands, commitment.Validity, commitment.HandToHandValidity),
                defender,
                Defender: null,
                AssaultStage.AwaitingDefender,
                Round: 1,
                LastRound: null),
        }).WithLog(
            $"{going.Name} went in on {holding.Name} with {stands}: rolled {test.Roll} against "
            + $"{test.ScoreToBeat}. {holding.Name}'s marker is turned; it must stand or give way."));
    }

    /// <summary>Whether an assault could be launched, and why not.</summary>
    /// <param name="defender">The platoon holding the position.</param>
    /// <param name="commitment">What would be committed.</param>
    /// <returns>The refusal in words, or null when the order may be given.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commitment"/> is null.</exception>
    public string? WhyLaunchIsRefused(UnitId defender, AssaultCommitment commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);

        if (Session.CurrentFrame is not { Kind: FrameKind.Activation } frame)
        {
            return "Nothing is activated, so nothing can assault.";
        }

        if (Assault is not null)
        {
            return $"{Unit(Assault.Attacker.Unit).Name} is already fighting an assault. Fight it out first.";
        }

        if (commitment.ThreatLevel < 0)
        {
            return "A threat level cannot be less than nothing.";
        }

        if (!HasUnit(defender))
        {
            return $"There is no platoon called '{defender}' on the table.";
        }

        var going = Unit(frame.Unit);
        var holding = Unit(defender);
        if (going.Side == holding.Side)
        {
            return $"{holding.Name} is on {going.Name}'s own side.";
        }

        if (Status(defender).IsWipedOut)
        {
            return $"{holding.Name} has nothing left on the position.";
        }

        if (going.IsCybertank || holding.IsCybertank)
        {
            // Every one of the four tests reads a confidence marker, and a cybertank carries none.
            return "A cybertank has no nerve to test, so it neither launches nor receives a close assault.";
        }

        if (ProfileBlocker(going) is { } unprofiled)
        {
            return unprofiled;
        }

        if (CommitmentBlocker(going, commitment) is { } uncommitted)
        {
            return uncommitted;
        }

        // The same check the step will make, in the same words, so the reason a button is disabled
        // is the reason the order would be refused. Checking each element against the frame as it
        // stands is sound because the only gate one element's step changes is its own.
        var policy = new DirtsideActivationPolicy(this);
        foreach (var element in commitment.Stands)
        {
            var check = GroundCombatSequence.CanTakeStep(
                Session, DirtsideSteps.Act(DirtsideAction.CloseAssault, element), policy);
            if (!check.IsAllowed)
            {
                return check.Reason;
            }
        }

        return null;
    }

    /// <summary>
    /// Rolls the defender's nerve to stand and receive the assault, or give up the position.
    /// </summary>
    /// <param name="commitment">Which of the defender's elements hold, what their chits may count, and how frightening what is coming is.</param>
    /// <param name="dice">Where the die result comes from.</param>
    /// <returns>The game with the defender standing or gone, or why the test could not be taken.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// A confidence test, not a reaction test, so giving way costs the defender morale as well as
    /// the position. A defender whose nerve had already gone never rolls; the resolver reports a
    /// null test for it, and the log says so rather than inventing a roll.
    /// </remarks>
    public GameOutcome<DirtsideGame> DefenderStands(AssaultCommitment commitment, IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(dice);

        if (WhyStandIsRefused(commitment) is { } reason)
        {
            return GameOutcome.Refused<DirtsideGame>(reason);
        }

        var assault = Assault!;
        var holding = Unit(assault.DefenderUnit);
        var going = Unit(assault.Attacker.Unit);
        var defence = CloseAssault.StandOrWithdraw(Profile(holding.Id), commitment.ThreatLevel, dice);
        var stands = string.Join(", ", commitment.Stands.Select(element => holding.Element(element)!.Name));

        var settled = WithStatus(holding.Id, status => status with { Confidence = defence.Confidence });

        if (defence.Withdraws)
        {
            var why = defence.Test is { } failed
                ? $"rolled {failed.Roll} against {failed.ScoreToBeat} and gave way to {defence.Confidence}"
                : $"was already {Status(holding.Id).Confidence} and broke to {defence.Confidence} without a test";

            return GameOutcome.Allowed((settled with
            {
                Assault = assault with { Stage = AssaultStage.AwaitingFollowThrough },
            }).WithLog(
                $"{holding.Name} {why}. The position is {going.Name}'s without a shot; it may test to drive on through."));
        }

        var test = defence.Test!.Value;
        return GameOutcome.Allowed((settled with
        {
            Assault = assault with
            {
                Defender = new AssaultSideState(holding.Id, commitment.Stands, commitment.Validity, commitment.HandToHandValidity),
                Stage = AssaultStage.AwaitingRound,
            },
        }).WithLog(
            $"{holding.Name} stood to receive {going.Name} with {stands}: rolled {test.Roll} against "
            + $"{test.ScoreToBeat} and held."));
    }

    /// <summary>Whether the defender's test could be taken, and why not.</summary>
    /// <param name="commitment">What the defender would commit.</param>
    /// <returns>The refusal in words, or null when the test may be taken.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commitment"/> is null.</exception>
    public string? WhyStandIsRefused(AssaultCommitment commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);

        if (StageBlocker(AssaultStage.AwaitingDefender) is { } stage)
        {
            return stage;
        }

        if (commitment.ThreatLevel < 0)
        {
            return "A threat level cannot be less than nothing.";
        }

        var holding = Unit(Assault!.DefenderUnit);
        return ProfileBlocker(holding) ?? CommitmentBlocker(holding, commitment);
    }

    /// <summary>
    /// Fights one round of the exchange.
    /// </summary>
    /// <param name="pot">The chit pot every stand draws from.</param>
    /// <returns>The game with the round fought, or why it could not be.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pot"/> is null.</exception>
    /// <remarks>
    /// The resolver reports each side's losses as a count, because a draw is against the side and
    /// not a stand. Which stands come off is the owner's choice at a table; here they come off in
    /// the order they were committed, and the log names them so the table can move the right models.
    /// </remarks>
    public GameOutcome<DirtsideGame> FightAssaultRound(IChitPot pot)
    {
        ArgumentNullException.ThrowIfNull(pot);

        if (WhyRoundIsRefused() is { } reason)
        {
            return GameOutcome.Refused<DirtsideGame>(reason);
        }

        var assault = Assault!;
        var attacker = assault.Attacker;
        var defender = assault.Defender!;

        var round = CloseAssault.ResolveRound(ExchangeSide(attacker), ExchangeSide(defender), pot, assault.Round);

        var (afterAttacker, attackerLost) = RemoveStands(attacker, round.Attacker.Losses);
        var (afterDefender, defenderLost) = RemoveStands(defender, round.Defender.Losses);

        var fought = this with
        {
            Assault = assault with
            {
                Attacker = afterAttacker,
                Defender = afterDefender,
                Stage = AssaultStage.AwaitingAftermath,
                LastRound = new AssaultRoundLosses(
                    round.Attacker.StandsPresent,
                    round.Attacker.Losses,
                    round.Defender.StandsPresent,
                    round.Defender.Losses),
            },
        };

        foreach (var element in attackerLost)
        {
            fought = fought.WithStatus(attacker.Unit, status => status.WithElement(
                element, current => current with { IsDestroyed = true }));
        }

        foreach (var element in defenderLost)
        {
            fought = fought.WithStatus(defender.Unit, status => status.WithElement(
                element, current => current with { IsDestroyed = true }));
        }

        var cover = round.DefenderCoverStillCounted ? string.Empty : ", hand to hand";
        return GameOutcome.Allowed(fought.WithLog(
            $"Round {round.Round} of {Unit(attacker.Unit).Name}'s assault on {Unit(defender.Unit).Name}{cover}: "
            + $"{Draws(attacker.Unit, round.Attacker)}; {Draws(defender.Unit, round.Defender)}. "
            + $"{Removed(attacker.Unit, attackerLost)} {Removed(defender.Unit, defenderLost)}"));
    }

    /// <summary>Whether a round could be fought, and why not.</summary>
    /// <returns>The refusal in words, or null when a round may be fought.</returns>
    public string? WhyRoundIsRefused() => StageBlocker(AssaultStage.AwaitingRound);

    /// <summary>
    /// The tests after a round: who, if anybody, has had enough.
    /// </summary>
    /// <param name="lightCasualtyThreat">The threat level for a side that got off lightly. The player's number.</param>
    /// <param name="heavyCasualtyThreat">The threat level for a side that was cut up. The player's number.</param>
    /// <param name="dice">Where the die results come from.</param>
    /// <returns>The game with the fight settled or continuing, or why the tests could not be taken.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// The resolver's order holds: the defender tests first, and an attacker whose defender broke is
    /// never asked. Whichever side falls back comes away Under Fire, as the resolver reports and the
    /// roster here applies.
    /// </para>
    /// <para>
    /// A side with no stands left cannot test, because there is nobody to. That is not the
    /// resolver's concern - it is handed sides that exist - so it is settled here: a defender wiped
    /// out has lost the position, and an attacker wiped out has lost the assault, without a die.
    /// </para>
    /// </remarks>
    public GameOutcome<DirtsideGame> ResolveAssaultAftermath(
        int lightCasualtyThreat,
        int heavyCasualtyThreat,
        IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (WhyAftermathIsRefused(lightCasualtyThreat, heavyCasualtyThreat) is { } reason)
        {
            return GameOutcome.Refused<DirtsideGame>(reason);
        }

        var assault = Assault!;
        var going = Unit(assault.Attacker.Unit);
        var holding = Unit(assault.Defender!.Unit);

        if (assault.Defender.Stands.IsEmpty)
        {
            return GameOutcome.Allowed((this with
            {
                Assault = assault with { Stage = AssaultStage.AwaitingFollowThrough },
            }).WithLog($"{holding.Name} has nobody left on the position. It is {going.Name}'s; it may test to drive on through."));
        }

        if (assault.Attacker.Stands.IsEmpty)
        {
            return GameOutcome.Allowed((this with { Assault = null }).WithLog(
                $"{going.Name} has nobody left to press the assault. {holding.Name} holds the position."));
        }

        var losses = assault.LastRound!.Value;
        var round = new AssaultRound(
            assault.Round,
            new AssaultSideResult(going.Id.Value, [], losses.AttackerStands, losses.AttackerLosses),
            new AssaultSideResult(holding.Id.Value, [], losses.DefenderStands, losses.DefenderLosses),
            assault.Round < CloseAssault.HandToHandFromRound);

        var aftermath = CloseAssault.ResolveAftermath(
            round, Profile(going.Id), Profile(holding.Id), lightCasualtyThreat, heavyCasualtyThreat, dice);

        var tested = WithStatus(holding.Id, status => status with
        {
            Confidence = aftermath.DefenderTest.After,
            IsUnderFire = status.IsUnderFire || aftermath.DefenderLeftUnderFire,
        });

        if (aftermath.AttackerTest is { } attackerTest)
        {
            tested = tested.WithStatus(going.Id, status => status with
            {
                Confidence = attackerTest.After,
                IsUnderFire = status.IsUnderFire || aftermath.AttackerLeftUnderFire,
            });
        }

        var defenderLine = $"{holding.Name} rolled {aftermath.DefenderTest.Roll} against {aftermath.DefenderTest.ScoreToBeat}";
        return aftermath.Outcome switch
        {
            AssaultOutcome.AttackerTakesThePosition => GameOutcome.Allowed((tested with
            {
                Assault = assault with { Stage = AssaultStage.AwaitingFollowThrough },
            }).WithLog(
                $"{defenderLine} and broke to {aftermath.DefenderTest.After}, falling back under fire. "
                + $"The position is {going.Name}'s; it may test to drive on through.")),

            AssaultOutcome.AttackerFallsBack => GameOutcome.Allowed((tested with { Assault = null }).WithLog(
                $"{defenderLine} and held; {going.Name} rolled {aftermath.AttackerTest!.Value.Roll} against "
                + $"{aftermath.AttackerTest.Value.ScoreToBeat} and broke to {aftermath.AttackerTest.Value.After}, "
                + $"falling back under fire. {holding.Name} holds the position.")),

            _ => GameOutcome.Allowed((tested with
            {
                Assault = assault with { Stage = AssaultStage.AwaitingRound, Round = assault.Round + 1 },
            }).WithLog(
                $"{defenderLine} and held; {going.Name} rolled {aftermath.AttackerTest!.Value.Roll} against "
                + $"{aftermath.AttackerTest.Value.ScoreToBeat} and held. Neither will let go: they go again, hand to hand.")),
        };
    }

    /// <summary>Whether the tests after a round could be taken, and why not.</summary>
    /// <param name="lightCasualtyThreat">The threat level for a side that got off lightly.</param>
    /// <param name="heavyCasualtyThreat">The threat level for a side that was cut up.</param>
    /// <returns>The refusal in words, or null when the tests may be taken.</returns>
    public string? WhyAftermathIsRefused(int lightCasualtyThreat, int heavyCasualtyThreat)
    {
        if (StageBlocker(AssaultStage.AwaitingAftermath) is { } stage)
        {
            return stage;
        }

        return lightCasualtyThreat < 0 || heavyCasualtyThreat < 0
            ? "A threat level cannot be less than nothing."
            : null;
    }

    /// <summary>
    /// The winner's test to drive on through the position rather than stop on it.
    /// </summary>
    /// <param name="threatLevel">What is being asked of it, keyed by the player off whether the defenders were destroyed or pushed back.</param>
    /// <param name="dice">Where the die result comes from.</param>
    /// <returns>The game with the assault over, and a fresh go for the attacker if it passed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    /// <remarks>
    /// A pass is a whole extra activation on the spot, and the resolver is explicit that it comes
    /// without the sequence layer's transfer machinery. So the open frame is not closed and reopened;
    /// its steps are wiped, and every element in the platoon has its move and its combat action
    /// again. A failure costs nothing but the opportunity: the position is still taken.
    /// </remarks>
    public GameOutcome<DirtsideGame> FollowThrough(int threatLevel, IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (WhyFollowThroughIsRefused(threatLevel) is { } reason)
        {
            return GameOutcome.Refused<DirtsideGame>(reason);
        }

        var going = Unit(Assault!.Attacker.Unit);
        var test = CloseAssault.FollowThrough(Profile(going.Id), threatLevel, dice);
        var ended = this with { Assault = null };

        if (!test.Passed)
        {
            return GameOutcome.Allowed(ended.WithLog(
                $"{going.Name} rolled {test.Roll} against {test.ScoreToBeat} and consolidated on the position."));
        }

        return GameOutcome.Allowed((ended with
        {
            Session = DirtsideTurn.FollowThrough(ended.Session),
        }).WithLog(
            $"{going.Name} rolled {test.Roll} against {test.ScoreToBeat} and drives on through the position: "
            + "every element has its move and its combat action again."));
    }

    /// <summary>Whether the follow-through test could be taken, and why not.</summary>
    /// <param name="threatLevel">What would be asked.</param>
    /// <returns>The refusal in words, or null when the test may be taken.</returns>
    public string? WhyFollowThroughIsRefused(int threatLevel) =>
        StageBlocker(AssaultStage.AwaitingFollowThrough)
        ?? (threatLevel < 0 ? "A threat level cannot be less than nothing." : null);

    /// <summary>The attacker's or defender's nerve, as the resolver's tests read it.</summary>
    private AssaultantProfile Profile(UnitId unit)
    {
        var platoon = Unit(unit);
        return new AssaultantProfile(
            platoon.Quality!.Value,
            platoon.LeadershipValue!.Value,
            Status(unit).Confidence,
            platoon.Kind);
    }

    /// <summary>The stands of one side, as the exchange fights them.</summary>
    private AssaultSide ExchangeSide(AssaultSideState side)
    {
        var platoon = Unit(side.Unit);
        return new AssaultSide(
            side.Unit.Value,
            [.. side.Stands.Select(element =>
            {
                var definition = platoon.Element(element)!;
                return new InfantryStand(element.Value, FirefightChits: 0, definition.AssaultChits!.Value, definition.KillThreshold!.Value);
            })],
            side.Validity,
            side.HandToHandValidity);
    }

    /// <summary>Takes stands off a side, first committed first, and says which.</summary>
    private static (AssaultSideState Side, ImmutableArray<ElementId> Removed) RemoveStands(AssaultSideState side, int losses)
    {
        var removed = side.Stands.Take(Math.Min(losses, side.Stands.Length)).ToImmutableArray();
        return (side with { Stands = side.Stands.RemoveRange(removed) }, removed);
    }

    /// <summary>Refuses a step of the assault that is not the one owed.</summary>
    private string? StageBlocker(AssaultStage expected)
    {
        if (Assault is null)
        {
            return "No assault is being fought.";
        }

        if (Assault.Stage == expected)
        {
            return null;
        }

        var going = Unit(Assault.Attacker.Unit).Name;
        var holding = Unit(Assault.DefenderUnit).Name;
        return Assault.Stage switch
        {
            AssaultStage.AwaitingDefender => $"{holding} has not yet said whether it stands against {going}.",
            AssaultStage.AwaitingRound => $"{going} and {holding} are on the position; a round has to be fought.",
            AssaultStage.AwaitingAftermath => $"Round {Assault.Round} has been fought; {holding} and {going} owe their tests.",
            _ => $"The position is {going}'s. It may test to drive on through, or end its activation.",
        };
    }

    /// <summary>Why a platoon cannot be tested at all: its card does not say what it rolls.</summary>
    private static string? ProfileBlocker(PlatoonDefinition platoon) =>
        platoon.Quality is null || platoon.LeadershipValue is null
            ? $"{platoon.Name}'s record card does not say what die it rolls or what its leadership value is, so its nerve cannot be tested."
            : null;

    /// <summary>Why a platoon cannot commit these elements to an assault.</summary>
    private string? CommitmentBlocker(PlatoonDefinition platoon, AssaultCommitment commitment)
    {
        if (commitment.Stands.IsDefaultOrEmpty)
        {
            return $"{platoon.Name} has to commit at least one element.";
        }

        if (commitment.Stands.Distinct().Count() != commitment.Stands.Length)
        {
            return "An element cannot be committed twice.";
        }

        var status = Status(platoon.Id);
        int? threshold = null;
        foreach (var element in commitment.Stands)
        {
            if (platoon.Element(element) is not { } definition)
            {
                return $"{platoon.Name} has no element called '{element}'.";
            }

            if (status.Element(element).IsDestroyed)
            {
                return $"{definition.Name} is out of the battle.";
            }

            if (definition.AssaultChits is null || definition.KillThreshold is null)
            {
                return $"{definition.Name}'s record card does not say how many chits it draws in an assault or what total removes it.";
            }

            // The resolver refuses a side with mixed toughness in the middle of a round. Refusing it
            // here, before anybody has spent anything, is the same rule at the right moment.
            threshold ??= definition.KillThreshold;
            if (threshold != definition.KillThreshold)
            {
                return $"{platoon.Name}'s committed elements do not all share one kill threshold, so there is no single number for the other side to draw against.";
            }
        }

        return null;
    }

    /// <summary>One side's draws in words.</summary>
    private string Draws(UnitId unit, AssaultSideResult side) =>
        side.Draws.Count == 0
            ? $"{Unit(unit).Name} had nobody to draw against"
            : $"{Unit(unit).Name} drew {string.Join(", ", side.Draws.Select(draw =>
                $"{Unit(unit).Element(new ElementId(draw.FirerId))?.Name ?? draw.FirerId} {draw.Tally.ValidTotal} against {draw.KillThreshold}"
                + $"{(draw.Destroyed ? " (a stand falls)" : string.Empty)}"))}";

    /// <summary>Which stands came off one side, in words.</summary>
    private string Removed(UnitId unit, ImmutableArray<ElementId> removed) =>
        removed.IsEmpty
            ? $"{Unit(unit).Name} lost nobody."
            : $"{Unit(unit).Name} lost {string.Join(", ", removed.Select(element => Unit(unit).Element(element)!.Name))}.";
}
