using System.Collections.Immutable;
using System.Globalization;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Combat;
using ForceSignal.Modules.StarGrunt.Morale;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Game;

/// <summary>
/// One volley, as the player declares it.
/// </summary>
/// <remarks>
/// <para>
/// The distance and the posture are declared rather than computed. That is how these games are
/// played - cover is assigned per shot by eye, and range is measured with a tape - and it is the
/// stance the rest of the project already takes.
/// </para>
/// <para>
/// <see cref="FirepowerDie"/> is declared too, and deliberately. It is a function of how many
/// figures are actually shooting, but the table that turns one into the other is the user's, off
/// their own rules. Deriving it here would mean shipping that table.
/// </para>
/// </remarks>
public sealed record FireCommand
{
    /// <summary>The unit shooting.</summary>
    public required UnitId Firer { get; init; }

    /// <summary>The unit being shot at.</summary>
    public required UnitId Target { get; init; }

    /// <summary>Which of the firer's weapons is being used.</summary>
    public required string WeaponName { get; init; }

    /// <summary>The small-arms firepower die, from the user's own table.</summary>
    public required QualityDie FirepowerDie { get; init; }

    /// <summary>
    /// Which of the unit's own support weapons are being folded into this volley.
    /// </summary>
    /// <remarks>
    /// Named rather than handed over as loose dice, so the game knows which weapons have been used.
    /// That is what lets it hold the squad to the trade the rules make: a weapon folded into squad
    /// fire may not also fire on its own that activation.
    /// </remarks>
    public ImmutableArray<string> SupportWeapons { get; init; } = [];

    /// <summary>How far apart the two units are, as measured at the table.</summary>
    public decimal DistanceInches { get; init; }

    /// <summary>The target's cover and posture, as the players judge it.</summary>
    public TargetPosture TargetPosture { get; init; }
}

public sealed partial record StarGruntGame
{
    /// <summary>
    /// Fires one weapon at another unit, and applies what it does.
    /// </summary>
    /// <param name="command">The volley being declared.</param>
    /// <param name="dice">Where the die results come from.</param>
    /// <returns>The game with the fire resolved, or why it could not be.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="dice"/> is null.</exception>
    /// <remarks>
    /// The shot is checked as an activation step before a die is rolled. That is what keeps the
    /// per-activation weapon limit honest - the limit lives in the frame's spent resources, so it is
    /// the sequence layer that refuses a second volley from the same weapon, not a flag kept here.
    /// </remarks>
    /// <param name="allocator">
    /// Who catches the hits, injectable so a test can put a round where it needs it. Defaults to
    /// spreading them evenly across the figures still standing.
    /// </param>
    public GameOutcome<StarGruntGame> Fire(
        FireCommand command,
        IQualityDiceRoller dice,
        IFigureAllocator? allocator = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(dice);
        allocator ??= new FigureAllocator();

        if (!HasUnit(command.Firer))
        {
            return GameOutcome.Refused<StarGruntGame>($"There is no unit called '{command.Firer}' on the table.");
        }

        if (!HasUnit(command.Target))
        {
            return GameOutcome.Refused<StarGruntGame>($"There is no unit called '{command.Target}' on the table.");
        }

        var firer = Unit(command.Firer);
        var target = Unit(command.Target);
        var firerStatus = Status(command.Firer);
        var targetStatus = Status(command.Target);

        if (firerStatus.IsWipedOut)
        {
            return GameOutcome.Refused<StarGruntGame>($"{firer.Name} has nobody left to shoot with.");
        }

        if (targetStatus.IsWipedOut)
        {
            return GameOutcome.Refused<StarGruntGame>($"{target.Name} has already been wiped out.");
        }

        var weapon = firer.Weapons.FirstOrDefault(w => w.Name == command.WeaponName);
        if (weapon is null)
        {
            return GameOutcome.Refused<StarGruntGame>($"{firer.Name} is not carrying anything called '{command.WeaponName}'.");
        }

        var support = new List<WeaponProfile>();
        foreach (var name in command.SupportWeapons)
        {
            var joining = firer.Weapons.FirstOrDefault(w => w.Name == name);
            if (joining is null)
            {
                return GameOutcome.Refused<StarGruntGame>($"{firer.Name} is not carrying anything called '{name}'.");
            }

            if (!joining.IsSupport)
            {
                return GameOutcome.Refused<StarGruntGame>($"{joining.Name} is not a support weapon.");
            }

            if (joining.NeverJoinsSquadFire)
            {
                return GameOutcome.Refused<StarGruntGame>($"{joining.Name} only ever fires on its own.");
            }

            if (joining.SupportFirepowerDie is null)
            {
                return GameOutcome.Refused<StarGruntGame>(
                    $"{joining.Name}'s card does not say what die it adds to a volley, so it cannot join one.");
            }

            support.Add(joining);
        }

        // Armour is the target's, and a unit whose figures differ takes the fire on the first of
        // them. Per-figure allocation is a refinement the rules allow and this slice does not need.
        var armour = target.Figures.IsDefaultOrEmpty ? QualityDie.D6 : target.Figures[0].ArmourDie;

        // Spend the action first. If the sequence refuses - wrong side's go, weapon already fired
        // this activation, no activation open at all - nothing has been rolled and nothing has moved.
        // Every weapon in the volley is spent, which is what stops a support weapon firing again
        // separately: the per-activation limit lives in the frame's resources and now sees all of
        // them rather than only the small arms.
        var step = StarGruntSteps.Fire(weapon.Name, support.Select(joining => joining.Name));
        var spent = TakeStep(step);
        if (!spent.IsAllowed)
        {
            return spent;
        }

        var outcome = new FireCombat(dice).Resolve(new FireAttempt(
            FirerQuality: firer.QualityDie,
            FirepowerDie: command.FirepowerDie,
            // Every weapon in `support` was refused above unless its card gave a die, so this is a
            // list of dice the player entered rather than a list with holes filled in.
            SupportDice: [.. support.Select(joining => joining.SupportFirepowerDie!.Value)],
            ImpactDie: weapon.ImpactDie,
            TargetArmourDie: armour,
            DistanceInches: command.DistanceInches,
            TargetPosture: command.TargetPosture,
            IsCloseRangeWeapon: weapon.IsCloseRange));

        var landed = Allocate(targetStatus, outcome, allocator);

        return GameOutcome.Allowed(
            spent.Value!
                .WithStatus(command.Target, status => Absorb(status, landed))
                .WithLog(Describe(firer, target, weapon, command, outcome, landed, support)));
    }

    /// <summary>What a volley did to particular figures.</summary>
    /// <param name="Killed">Figures killed outright or by a second wound.</param>
    /// <param name="Wounded">Figures put out of the fight but still with the unit.</param>
    /// <param name="LeaderHit">True when one of them was the squad leader.</param>
    private readonly record struct LandedHits(int Killed, int Wounded, bool LeaderHit);

    /// <summary>The squad leader is the first figure while he is on his feet.</summary>
    private const int LeaderFigure = 0;

    /// <summary>
    /// Decides which figures a volley's hits landed on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each hit picks a figure at random from those still standing. A figure taking two wound
    /// results <em>in this resolution</em> is dead; one taking a single wound is a casualty the
    /// squad carries. Wounds from separate volleys never pair - the rule is scoped to one
    /// resolution, and a trooper wounded last turn is already out of the fighting strength.
    /// </para>
    /// <para>
    /// Doing this deterministically, which is what this code used to do, converts every two wounds
    /// into a death and kills roughly eight times more men than the rules do on a full squad. The
    /// randomness is the rule, not decoration.
    /// </para>
    /// </remarks>
    private static LandedHits Allocate(UnitStatus status, FireOutcome outcome, IFigureAllocator allocator)
    {
        var standing = status.FiguresAlive;
        if (standing <= 0)
        {
            return new LandedHits(0, 0, false);
        }

        var woundsPerFigure = new Dictionary<int, int>();
        var killedFigures = new HashSet<int>();

        foreach (var hit in outcome.Hits)
        {
            if (hit.Effect == HitEffect.Stopped)
            {
                continue;
            }

            var figure = allocator.Pick(standing);
            if (hit.Effect == HitEffect.Kill)
            {
                killedFigures.Add(figure);
                continue;
            }

            woundsPerFigure[figure] = woundsPerFigure.GetValueOrDefault(figure) + 1;
        }

        foreach (var pair in woundsPerFigure)
        {
            if (pair.Value >= 2)
            {
                killedFigures.Add(pair.Key);
            }
        }

        var wounded = woundsPerFigure.Count(entry => entry.Value == 1 && !killedFigures.Contains(entry.Key));
        var leaderHit = !status.IsLeaderDown
            && (killedFigures.Contains(LeaderFigure) || woundsPerFigure.ContainsKey(LeaderFigure));

        return new LandedHits(Math.Min(standing, killedFigures.Count), wounded, leaderHit);
    }

    /// <summary>Puts a volley's casualties and suppression onto the unit that took it.</summary>
    /// <remarks>
    /// A wounded figure comes out of the fighting strength and stays with the unit: the rules treat
    /// it as a casualty the squad carries, and each untreated one raises the threat level the player
    /// reads off their own table.
    /// </remarks>
    private static UnitStatus Absorb(UnitStatus status, LandedHits landed)
    {
        var killed = Math.Min(status.FiguresAlive, landed.Killed);
        var wounded = Math.Min(status.FiguresAlive - killed, landed.Wounded);

        return status with
        {
            FiguresAlive = status.FiguresAlive - killed - wounded,
            FiguresWounded = status.FiguresWounded + wounded,
            IsLeaderDown = status.IsLeaderDown || landed.LeaderHit,
            SuppressionMarkers = Suppression.Add(
                landed.LeaderHit ? Suppression.Add(status.SuppressionMarkers) : status.SuppressionMarkers),
        };
    }

    private static string Describe(
        UnitDefinition firer,
        UnitDefinition target,
        WeaponProfile weapon,
        FireCommand command,
        FireOutcome outcome,
        LandedHits landed,
        List<WeaponProfile> support)
    {
        var range = command.DistanceInches.ToString("0.#", CultureInfo.InvariantCulture);
        var suppressed = outcome.Suppresses ? ", and it is suppressed" : string.Empty;
        var joined = support.Count == 0
            ? string.Empty
            : $" with {string.Join(" and ", support.Select(weapon => weapon.Name))}";
        return $"{firer.Name} fired {weapon.Name}{joined} at {target.Name} at {range}: "
            + $"{outcome.PotentialHits} potential hit{(outcome.PotentialHits == 1 ? string.Empty : "s")}, "
            + $"{landed.Killed} killed and {landed.Wounded} wounded{suppressed}."
            + (landed.LeaderHit ? " Its leader is down." : string.Empty);
    }
}
