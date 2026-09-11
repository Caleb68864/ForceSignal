using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Assault;
using ForceSignal.Modules.StarGrunt.Combat;
using ForceSignal.Modules.StarGrunt.Morale;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Game;

/// <summary>One pair of figures, as the two players have paired them off over the table.</summary>
/// <param name="AttackerShift">Die types the charging figure's close-combat weapon is worth.</param>
/// <param name="DefenderShift">Die types the receiving figure's is worth.</param>
/// <param name="AttackerPowerArmour">True when the charging figure is in power armour.</param>
/// <param name="DefenderPowerArmour">True when the receiving figure is.</param>
public readonly record struct MeleePairing(
    int AttackerShift = 0,
    int DefenderShift = 0,
    bool AttackerPowerArmour = false,
    bool DefenderPowerArmour = false);

public sealed partial record StarGruntGame
{
    /// <summary>
    /// Declares a close assault and rolls the attacker's nerve to make it.
    /// </summary>
    /// <param name="attacker">The unit charging, which spends its whole activation on this.</param>
    /// <param name="defender">The single unit being charged.</param>
    /// <param name="threatLevel">What the charge asks of them, off the player's own table.</param>
    /// <param name="dice">Where the die result comes from.</param>
    /// <returns>The game with the charge declared, or why it could not be.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    /// <remarks>
    /// The threat comes off the player's own table, like every other threat level here. What the app
    /// owns is the rule beside it: a unit that has already lost its nerve will not go at all, however
    /// it is asked.
    ///
    /// A charge costs the whole activation even when the move to contact needs only one action, so
    /// both are spent here. Failing the test loses the first action and leaves the second for
    /// something that is not another charge, which is the reaction rule already in place.
    /// </remarks>
    public GameOutcome<StarGruntGame> DeclareCloseAssault(
        UnitId attacker,
        UnitId defender,
        int threatLevel,
        IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (threatLevel < 0)
        {
            return GameOutcome.Refused<StarGruntGame>("A threat level cannot be less than nothing.");
        }

        if (!HasUnit(attacker) || !HasUnit(defender))
        {
            return GameOutcome.Refused<StarGruntGame>("Both units have to be on the table.");
        }

        if (attacker == defender)
        {
            return GameOutcome.Refused<StarGruntGame>("A unit cannot charge itself.");
        }

        var charging = Unit(attacker);
        var receiving = Unit(defender);
        if (charging.Side == receiving.Side)
        {
            return GameOutcome.Refused<StarGruntGame>($"{receiving.Name} is on {charging.Name}'s own side.");
        }

        var status = Status(attacker);
        if (!CloseAssault.CanCharge(status.Confidence))
        {
            return GameOutcome.Refused<StarGruntGame>(
                $"{charging.Name} is {status.Confidence} and will not charge anyone.");
        }

        var test = Confidence.React(charging.QualityDie, charging.LeadershipValue, threatLevel, dice);
        var step = StarGruntSteps.Simple(test.Passed ? StarGruntAction.CloseAssault : StarGruntAction.RefusedOrder);
        var spent = TakeStep(step);
        if (!spent.IsAllowed)
        {
            return spent;
        }

        var outcome = test.Passed
            ? $"and went in on {receiving.Name}"
            : "and would not go";

        return GameOutcome.Allowed(
            spent.Value!.WithLog(
                $"{charging.Name} was ordered to charge {receiving.Name}: rolled {test.Roll} against "
                + $"{test.ScoreToBeat} {outcome}."));
    }

    /// <summary>
    /// Rolls the defender's nerve to stand and receive a charge.
    /// </summary>
    /// <param name="attacker">The unit charging.</param>
    /// <param name="defender">The unit being charged.</param>
    /// <param name="terror">True when the attackers are the sort that frighten people.</param>
    /// <param name="dice">Where the die result comes from.</param>
    /// <returns>The game with the test resolved, or why it could not be taken.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    /// <remarks>
    /// The threat comes from the odds, which the app can count off the roster - power armour
    /// counting as two men - and terror doubles it. Terror is a property of the attacking unit
    /// agreed between the players before the game, so it is passed in rather than looked up.
    ///
    /// A defender already broken does not test: it drops straight to routed and withdraws.
    /// </remarks>
    public GameOutcome<StarGruntGame> DefenderStands(
        UnitId attacker,
        UnitId defender,
        bool terror,
        IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (!HasUnit(attacker) || !HasUnit(defender))
        {
            return GameOutcome.Refused<StarGruntGame>("Both units have to be on the table.");
        }

        var charging = Unit(attacker);
        var receiving = Unit(defender);
        var receivingStatus = Status(defender);

        if (receivingStatus.Confidence <= ConfidenceLevel.Broken)
        {
            return GameOutcome.Allowed(
                WithStatus(defender, status => status with { Confidence = ConfidenceLevel.Routed })
                    .WithLog($"{receiving.Name} was already broken and routed the moment {charging.Name} came on."));
        }

        var threat = CloseAssault.StandThreat(
            CloseAssault.Strength(Status(attacker).FiguresAlive),
            CloseAssault.Strength(receivingStatus.FiguresAlive),
            terror);

        var test = Confidence.Test(
            receivingStatus.Confidence,
            receiving.QualityDie,
            receiving.LeadershipValue,
            threat,
            dice);

        var outcome = test.Passed
            ? "and stood"
            : $"and gave way to {test.After}";

        return GameOutcome.Allowed(
            WithStatus(defender, status => status with { Confidence = test.After })
                .WithLog(
                    $"{receiving.Name} was charged by {charging.Name}: rolled {test.Roll} against "
                    + $"{test.ScoreToBeat} (leadership {receiving.LeadershipValue} plus threat {threat}"
                    + $"{(terror ? ", doubled for terror" : string.Empty)}) {outcome}."));
    }

    /// <summary>
    /// Fights one round of melee, one exchange per pairing the players have made.
    /// </summary>
    /// <param name="attacker">The charging unit.</param>
    /// <param name="defender">The receiving unit.</param>
    /// <param name="pairings">Who is fighting whom, as paired off over the table.</param>
    /// <param name="defendersInCover">True in the first round only, while the cover still counts.</param>
    /// <param name="dice">Where the die results come from.</param>
    /// <param name="profile">
    /// The players' rules profile, for what cover is worth to the defenders. Only read when they are
    /// in cover; a round fought in the open never asks.
    /// </param>
    /// <returns>The game with the round fought, or why it could not be.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// The pairing comes in rather than being worked out, because the rule that produces it is a
    /// choice between two people: the attacker pairs one figure per defender and the defender
    /// allocates whoever is left over, precisely so an attacker cannot pick off leaders.
    ///
    /// Figures downed here are casualties on the spot; what became of them is rolled at the end of
    /// the assault, not now.
    /// </remarks>
    public GameOutcome<StarGruntGame> FightMeleeRound(
        UnitId attacker,
        UnitId defender,
        IReadOnlyList<MeleePairing> pairings,
        bool defendersInCover,
        IQualityDiceRoller dice,
        StarGruntRulesProfile profile)
    {
        ArgumentNullException.ThrowIfNull(pairings);
        ArgumentNullException.ThrowIfNull(dice);
        ArgumentNullException.ThrowIfNull(profile);

        if (!HasUnit(attacker) || !HasUnit(defender))
        {
            return GameOutcome.Refused<StarGruntGame>("Both units have to be on the table.");
        }

        if (pairings.Count == 0)
        {
            return GameOutcome.Refused<StarGruntGame>("Nobody has been paired off to fight.");
        }

        // Read only when it is used, and refused before a die is thrown when it is not there. It was
        // `CoverShift = 1` in this module; a round in the open still settles on a blank profile.
        var coverShift = 0;
        if (defendersInCover)
        {
            if (profile.MeleeCoverShift is not { } rungs)
            {
                return GameOutcome.Refused<StarGruntGame>(
                    "This game's rules profile has no entry for how many rungs cover is worth to a defender in "
                    + "the first round of a melee. Enter it in the game's rules profile - this app ships none "
                    + "of its own - or fight the round with the defenders out of cover.");
            }

            coverShift = rungs;
        }

        var charging = Unit(attacker);
        var receiving = Unit(defender);
        var attackerDown = 0;
        var defenderDown = 0;
        var lines = ImmutableArray.CreateBuilder<string>();

        foreach (var pairing in pairings)
        {
            var exchange = CloseAssault.Fight(
                new Combatant(charging.QualityDie, pairing.AttackerShift, pairing.AttackerPowerArmour),
                new Combatant(receiving.QualityDie, pairing.DefenderShift, pairing.DefenderPowerArmour, coverShift),
                dice);

            attackerDown += exchange.AttackerDown ? 1 : 0;
            defenderDown += exchange.DefenderDown ? 1 : 0;
            lines.Add(
                $"{exchange.AttackerDie} {exchange.AttackerScore} against {exchange.DefenderDie} {exchange.DefenderScore}: "
                + (exchange.Tied ? "neither gave way" : exchange.DefenderDown ? "a defender went down" : "an attacker went down"));
        }

        var fought = WithStatus(attacker, status => Down(status, attackerDown))
            .WithStatus(defender, status => Down(status, defenderDown));

        return GameOutcome.Allowed(
            fought.WithLog(
                $"{charging.Name} closed with {receiving.Name}: {string.Join("; ", lines)}. "
                + $"{attackerDown} attacker(s) and {defenderDown} defender(s) down."));
    }

    /// <summary>
    /// Rolls what became of the figures downed in an assault, now that it is over.
    /// </summary>
    /// <param name="unit">The unit whose downed figures are being settled.</param>
    /// <param name="downed">How many of its figures went down.</param>
    /// <param name="wonTheAssault">True when its side holds the ground at the finish.</param>
    /// <param name="deadUpTo">The highest roll that means dead, off the player's own table.</param>
    /// <param name="woundedUpTo">The highest roll that means wounded.</param>
    /// <param name="fateDie">
    /// The die those bands are read against, off the same table they came from. Null when the table
    /// did not say, which is refused rather than guessed at.
    /// </param>
    /// <param name="dice">Where the die results come from.</param>
    /// <returns>The game with the fates settled, or why they could not be.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// A stunned man on the winning side gets up again; on the losing side he is left behind and
    /// taken, which is why this cannot be rolled until somebody has won. The losers cannot carry
    /// their downed away.
    /// </para>
    /// <para>
    /// The die is supplied for the same reason the bands are. This rolled a flat <c>QualityDie.D6</c>
    /// while taking the bands off the player - which is half a table: a player whose own chart reads
    /// dead on 1-3 and wounded on 4-7 had the 8 to 10 that means stunned made unreachable, silently,
    /// because the app was throwing a die their chart was never written for. Bands without the die
    /// they are read against are not numbers at all.
    /// </para>
    /// </remarks>
    public GameOutcome<StarGruntGame> SettleTheDowned(
        UnitId unit,
        int downed,
        bool wonTheAssault,
        int deadUpTo,
        int woundedUpTo,
        QualityDie? fateDie,
        IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (deadUpTo < 1 || woundedUpTo < deadUpTo)
        {
            return GameOutcome.Refused<StarGruntGame>(
                "The bands have to climb: dead at the bottom, then wounded, then stunned above it.");
        }

        if (fateDie is null)
        {
            return GameOutcome.Refused<StarGruntGame>(
                "Your table has not said which die the downed are settled on, so the bands above have nothing to be read against.");
        }

        if (!HasUnit(unit))
        {
            return GameOutcome.Refused<StarGruntGame>($"There is no unit called '{unit}' on the table.");
        }

        if (downed <= 0)
        {
            return GameOutcome.Refused<StarGruntGame>($"{Unit(unit).Name} had nobody down.");
        }

        // A unit cannot have more figures down than it ever had, and the number arrives off the
        // wire. Without the clamp, one request naming two billion downed figures rolled two
        // billion dice inside the service's lock and wedged the engine for everyone.
        downed = Math.Min(downed, Unit(unit).FullStrength);

        var dead = 0;
        var wounded = 0;
        var recovered = 0;

        for (var figure = 0; figure < downed; figure++)
        {
            switch (CloseAssault.Fate(dice.Roll(fateDie.Value), deadUpTo, woundedUpTo))
            {
                case DownedFate.Dead:
                    dead++;
                    break;
                case DownedFate.Wounded:
                    wounded++;
                    break;
                default:
                    // Back on his feet if his side held the ground; taken prisoner if it did not.
                    if (wonTheAssault)
                    {
                        recovered++;
                    }
                    else
                    {
                        dead++;
                    }

                    break;
            }
        }

        var taken = wonTheAssault ? string.Empty : " The rest were left to the victors.";

        return GameOutcome.Allowed(
            WithStatus(unit, status => status with
            {
                FiguresAlive = status.FiguresAlive + recovered,
                FiguresWounded = status.FiguresWounded + wounded,
            })
                .WithLog(
                    $"{Unit(unit).Name} counted its down: {dead} dead, {wounded} wounded, "
                    + $"{recovered} back on their feet.{taken}"));
    }

    /// <summary>Takes figures out of the fighting strength as they are downed in melee.</summary>
    private static UnitStatus Down(UnitStatus status, int downed) =>
        downed <= 0 ? status : status with { FiguresAlive = Math.Max(0, status.FiguresAlive - downed) };
}
