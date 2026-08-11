using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Sequence;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Game;

/// <summary>
/// One element's binding declaration of what it is shooting at, as the game takes it.
/// </summary>
/// <remarks>
/// The measured band is the player's - it comes off a tape at the table - and so is whether the
/// target is hull down or moving fast. What the game supplies from its own roster is everything on
/// the record cards: gunnery, signature, armour, chit count, barrels and the validity card.
/// </remarks>
/// <param name="Firer">The platoon shooting.</param>
/// <param name="Element">The element shooting.</param>
/// <param name="Weapon">Which of its systems, by the player's own name for it.</param>
/// <param name="Target">The platoon being shot at.</param>
/// <param name="TargetElement">The element designated, before any dice.</param>
/// <param name="MeasuredBand">The band the tape says the shot falls in.</param>
public sealed record FireCommand(
    UnitId Firer,
    ElementId Element,
    string Weapon,
    UnitId Target,
    ElementId TargetElement,
    WeaponRangeBand MeasuredBand);

public sealed partial record DirtsideGame
{
    /// <summary>
    /// Fires one element's weapon at one designated element, and applies what the chits say.
    /// </summary>
    /// <param name="command">What is being fired at what.</param>
    /// <param name="dice">Where the die results come from.</param>
    /// <param name="pot">The chit pot every hit draws from.</param>
    /// <returns>The game with the shot resolved, or why it could not be taken.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <para>
    /// The shot is checked as an activation step before a die is rolled, which is what keeps the
    /// one-combat-action-per-element limit honest: the limit lives in the frame's spent resources, so
    /// it is the sequence layer that refuses a second shot from the same element, and the fixed-mount
    /// rule that refuses a shot after moving. Neither is re-stated here.
    /// </para>
    /// <para>
    /// A declaration is binding. Firing at an element that an earlier shot already destroyed is
    /// refused rather than redirected - the shot is spent, which is the cost of information the
    /// player did not have when they declared.
    /// </para>
    /// </remarks>
    public GameOutcome<DirtsideGame> Fire(FireCommand command, IQualityDiceRoller dice, IChitPot pot)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(dice);
        ArgumentNullException.ThrowIfNull(pot);

        if (Prepare(command) is not { } shot)
        {
            return GameOutcome.Refused<DirtsideGame>(DescribeBlocker(command));
        }

        // The step first, so a shot that is not legal costs nothing and rolls nothing.
        var fired = TakeStep(DirtsideSteps.Fire(command.Element, command.Weapon));
        if (!fired.IsAllowed)
        {
            return fired;
        }

        var result = DirectFire.Resolve(shot, dice, pot);
        return GameOutcome.Allowed(fired.Value!.Apply(command, result));
    }

    /// <summary>
    /// Whether this element could fire this weapon at this target, and why not.
    /// </summary>
    /// <param name="command">The shot being considered.</param>
    /// <returns>The refusal in words, or null when the shot may be taken.</returns>
    /// <remarks>
    /// Shared with <see cref="Fire"/> rather than written twice, so that a screen showing why a
    /// button is disabled uses the same words the command would refuse with.
    /// </remarks>
    public string? WhyFireIsRefused(FireCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Prepare(command) is null ? DescribeBlocker(command) : null;
    }

    /// <summary>Builds the engine's declaration from the roster, or null when it cannot be built.</summary>
    private FireDeclaration? Prepare(FireCommand command)
    {
        if (!HasUnit(command.Firer) || !HasUnit(command.Target))
        {
            return null;
        }

        if (Unit(command.Firer).Element(command.Element) is not { } firer
            || firer.Weapon(command.Weapon) is not { } weapon
            || Unit(command.Target).Element(command.TargetElement) is not { } target)
        {
            return null;
        }

        var firerStatus = Status(command.Firer).Element(command.Element);
        var targetStatus = Status(command.Target).Element(command.TargetElement);
        if (firerStatus.IsDestroyed || firerStatus.IsSystemsDown || targetStatus.IsDestroyed)
        {
            return null;
        }

        if (Unit(command.Firer).Side == Unit(command.Target).Side)
        {
            return null;
        }

        return new FireDeclaration(
            new FiringElement(
                firer.Id.ToString(),
                firer.FireControl,
                firerStatus.MovedOverHalf,
                firerStatus.IsDamaged),
            new WeaponMount(weapon.ChitCount, weapon.Validity, weapon.Barrels),
            new TargetElement(
                target.Id.ToString(),
                target.Signature,
                target.ArmourValue,
                targetStatus.Posture),
            command.MeasuredBand);
    }

    /// <summary>Says in words why a shot could not be prepared.</summary>
    private string DescribeBlocker(FireCommand command)
    {
        if (!HasUnit(command.Firer) || !HasUnit(command.Target))
        {
            return "Both platoons have to be on the table.";
        }

        var firing = Unit(command.Firer);
        var receiving = Unit(command.Target);

        if (firing.Side == receiving.Side)
        {
            return $"{receiving.Name} is on {firing.Name}'s own side.";
        }

        if (firing.Element(command.Element) is not { } firer)
        {
            return $"{firing.Name} has no element called '{command.Element}'.";
        }

        if (firer.Weapon(command.Weapon) is null)
        {
            return $"{firer.Name} is not carrying '{command.Weapon}'.";
        }

        if (receiving.Element(command.TargetElement) is not { } target)
        {
            return $"{receiving.Name} has no element called '{command.TargetElement}'.";
        }

        var firerStatus = Status(command.Firer).Element(command.Element);
        if (firerStatus.IsDestroyed)
        {
            return $"{firer.Name} is out of the battle.";
        }

        if (firerStatus.IsSystemsDown)
        {
            return $"{firer.Name} has its systems down and does nothing until they are back.";
        }

        // The declaration was binding, and what it named is gone. The shot is spent either way.
        return $"{target.Name} was already destroyed, and a declaration cannot be re-pointed.";
    }

    /// <summary>Writes what the chits said onto the roster.</summary>
    private DirtsideGame Apply(FireCommand command, ShotResult result)
    {
        var firer = Unit(command.Firer);
        var target = Unit(command.Target);
        var firerName = $"{firer.Name}'s {firer.Element(command.Element)!.Name}";
        var targetName = $"{target.Name}'s {target.Element(command.TargetElement)!.Name}";

        if (!result.WasFired)
        {
            return WithLog($"{firerName} declared a shot at {targetName} and never took it: {result.Reason}");
        }

        var game = this;
        foreach (var damage in result.Damage)
        {
            if (damage.ShotNeverHappened)
            {
                continue;
            }

            game = game.WithStatus(command.Target, status => status.WithElement(
                command.TargetElement,
                element => element with
                {
                    IsDestroyed = element.IsDestroyed || damage.TargetDestroyed,
                    IsDamaged = element.IsDamaged || damage.TargetDamaged,
                    IsSystemsDown = element.IsSystemsDown || damage.TargetSystemsDown,
                }));

            // A shot can put the firer's own systems down, which is why this is applied to the
            // shooter's roster rather than only reported.
            if (damage.FirerSystemsDown)
            {
                game = game.WithStatus(command.Firer, status => status.WithElement(
                    command.Element, element => element with { IsSystemsDown = true }));
            }
        }

        return game.WithLog(Describe(firerName, targetName, result));
    }

    /// <summary>The shot in words, with everything a table would want to read back.</summary>
    private static string Describe(string firerName, string targetName, ShotResult result)
    {
        var band = result.Band.Band == result.Declaration.MeasuredBand
            ? $"{result.Band.Band} range"
            : $"{result.Band.Band} range (measured {result.Declaration.MeasuredBand})";

        var rolls = string.Join(
            ", ",
            result.Attempts.Select(attempt =>
                $"{attempt.Solution.FirerDie} {attempt.FirerRoll} against {attempt.TargetScore}"
                + $"{(attempt.TargetSecondaryRoll is null ? string.Empty : " (best of two)")}: "
                + $"{(attempt.IsHit ? "hit" : "miss")}"));

        if (result.Hits == 0)
        {
            return $"{firerName} fired on {targetName} at {band}: {rolls}. Nothing landed.";
        }

        var effects = result.Damage
            .Where(damage => !damage.ShotNeverHappened)
            .Select(damage =>
                damage.CatastrophicKill ? "a catastrophic kill"
                : damage.TargetDestroyed ? "knocked out"
                : damage.TargetDamaged ? "damaged"
                : damage.Immobilised ? "immobilised"
                : damage.TargetSystemsDown ? "systems down"
                : "no effect")
            .ToArray();

        var firerDown = result.FirerSystemsDown ? " The firer's own systems went down." : string.Empty;
        return $"{firerName} fired on {targetName} at {band}: {rolls}. "
            + $"{result.Hits} hit(s): {string.Join(", ", effects)}.{firerDown}";
    }
}
