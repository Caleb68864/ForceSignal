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

    /// <summary>One extra die per support weapon joining the volley.</summary>
    public ImmutableArray<QualityDie> SupportDice { get; init; } = [];

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
    public GameOutcome<StarGruntGame> Fire(FireCommand command, IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(dice);

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

        // Armour is the target's, and a unit whose figures differ takes the fire on the first of
        // them. Per-figure allocation is a refinement the rules allow and this slice does not need.
        var armour = target.Figures.IsDefaultOrEmpty ? QualityDie.D6 : target.Figures[0].ArmourDie;

        // Spend the action first. If the sequence refuses - wrong side's go, weapon already fired
        // this activation, no activation open at all - nothing has been rolled and nothing has moved.
        var step = StarGruntSteps.Fire(weapon.Name);
        var spent = TakeStep(step);
        if (!spent.IsAllowed)
        {
            return spent;
        }

        var outcome = new FireCombat(dice).Resolve(new FireAttempt(
            FirerQuality: firer.QualityDie,
            FirepowerDie: command.FirepowerDie,
            SupportDice: [.. command.SupportDice],
            ImpactDie: weapon.ImpactDie,
            TargetArmourDie: armour,
            DistanceInches: command.DistanceInches,
            TargetPosture: command.TargetPosture,
            IsCloseRangeWeapon: weapon.IsCloseRange));

        return GameOutcome.Allowed(
            spent.Value!
                .WithStatus(command.Target, status => Absorb(status, outcome))
                .WithLog(Describe(firer, target, weapon, command, outcome)));
    }

    /// <summary>Puts a volley's casualties and suppression onto the unit that took it.</summary>
    /// <remarks>
    /// Two wounds on one figure in a single resolution is a death, which is why the wounds are paired
    /// off here rather than simply counted. The odd one out stays a wound.
    /// </remarks>
    private static UnitStatus Absorb(UnitStatus status, FireOutcome outcome)
    {
        var deathsFromWounds = outcome.Wounds / 2;
        var lingering = outcome.Wounds % 2;
        var killed = Math.Min(status.FiguresAlive, outcome.Kills + deathsFromWounds);

        return status with
        {
            FiguresAlive = status.FiguresAlive - killed,
            FiguresWounded = Math.Min(status.FiguresAlive - killed, status.FiguresWounded + lingering),
            // The cap is the suppression rule's, asked rather than restated.
            SuppressionMarkers = outcome.Suppresses
                ? Suppression.Add(status.SuppressionMarkers)
                : status.SuppressionMarkers,
        };
    }

    private static string Describe(
        UnitDefinition firer,
        UnitDefinition target,
        WeaponProfile weapon,
        FireCommand command,
        FireOutcome outcome)
    {
        var range = command.DistanceInches.ToString("0.#", CultureInfo.InvariantCulture);
        var suppressed = outcome.Suppresses ? ", and it is suppressed" : string.Empty;
        return $"{firer.Name} fired {weapon.Name} at {target.Name} at {range}: "
            + $"{outcome.PotentialHits} potential hit{(outcome.PotentialHits == 1 ? string.Empty : "s")}, "
            + $"{outcome.Kills} killed and {outcome.Wounds} wounded{suppressed}.";
    }
}
