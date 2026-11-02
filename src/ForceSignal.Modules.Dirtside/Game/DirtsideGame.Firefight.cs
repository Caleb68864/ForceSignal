using System.Collections.Immutable;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.Dirtside.Sequence;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Game;

/// <summary>A firefight, as the table declares it.</summary>
/// <param name="Firer">The infantry platoon firing.</param>
/// <param name="Weapon">The small arms each stand fires, by the name on its card.</param>
/// <param name="Stands">
/// The stands able to join in, in the order the player would have them fire. If the check lets only
/// part of the unit shoot, those are the first ones here: which stands make up the firing half is the
/// player's, and saying so up front saves a second round trip.
/// </param>
/// <param name="Target">The platoon being fired on.</param>
/// <param name="TargetElement">The stand being fired on.</param>
/// <param name="MeasuredBand">The band the tape read, which picks the row off the weapon's card.</param>
public sealed record FirefightCommand(
    UnitId Firer,
    string Weapon,
    ImmutableArray<ElementId> Stands,
    UnitId Target,
    ElementId TargetElement,
    WeaponRangeBand MeasuredBand);

/// <summary>Infantry firing as stands: one check for the unit, then a separate draw per stand.</summary>
/// <remarks>
/// The resolver is <see cref="InfantryCombat"/> and this only sequences it. Every number comes off a
/// card or the rules profile: a stand's draw is the chit count on its small arms, its toughness is the
/// kill threshold it already carries for close assault, and the rungs an Under Fire marker costs are a
/// profile entry, refused by name if the shot needs it and nobody entered it.
/// </remarks>
public sealed partial record DirtsideGame
{
    /// <summary>Fights one firefight.</summary>
    /// <param name="command">What was declared.</param>
    /// <param name="dice">Where the check's die comes from.</param>
    /// <param name="pot">The chit pot, whole again before each stand draws.</param>
    /// <param name="profile">The players' rules profile.</param>
    /// <returns>The game with the firefight settled, or why it could not be fought.</returns>
    /// <remarks>
    /// Every stand named spends its combat action, whether or not the check lets it draw, because they
    /// all tried. Any firefight marks the target, however badly it went.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public GameOutcome<DirtsideGame> Firefight(
        FirefightCommand command,
        IQualityDiceRoller dice,
        IChitPot pot,
        DirtsideRulesProfile profile)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(dice);
        ArgumentNullException.ThrowIfNull(pot);
        ArgumentNullException.ThrowIfNull(profile);

        if (WhyFirefightIsRefused(command, profile) is { } reason)
        {
            return GameOutcome.Refused<DirtsideGame>(reason);
        }

        // Every step first, so a stand that may not fire costs nothing and rolls nothing.
        var game = this;
        foreach (var stand in command.Stands)
        {
            var stepped = game.TakeStep(DirtsideSteps.Fire(stand, command.Weapon));
            if (!stepped.IsAllowed)
            {
                return stepped;
            }

            game = stepped.Value!;
        }

        var firing = Unit(command.Firer);
        var target = Unit(command.Target);
        var aimedAt = target.Element(command.TargetElement)!;
        var solution = InfantryCombat.SolveFireEffectiveness(
            firing.Quality!.Value,
            firing.LeadershipValue!.Value,
            command.Stands.Length,
            Status(command.Firer).IsUnderFire ? profile.RungsLostUnderFire : null);
        var check = InfantryCombat.RollFireEffectiveness(solution, dice);

        var smallArmsTarget = SmallArmsTarget.Stand(aimedAt.Id.Value, aimedAt.KillThreshold!.Value);
        var draws = command.Stands
            .Take(check.StandsFiring)
            .Select(stand =>
            {
                var weapon = firing.Element(stand)!.Weapon(command.Weapon)!;
                return InfantryCombat.ResolveDraw(
                    stand.Value, weapon.ChitCount, weapon.Validity.At(command.MeasuredBand), smallArmsTarget, pot);
            })
            .ToList();
        var result = new FirefightResult(check, draws);

        // One stand per winning draw, never a total. The stand aimed at goes first and the rest in
        // the order they were committed, as an assault's losses are; the log names them so the table
        // can take the right models off.
        var removed = new[] { aimedAt.Id }
            .Concat(target.Elements.Select(element => element.Id).Where(id => id != aimedAt.Id))
            .Where(id => !Status(command.Target).Element(id).IsDestroyed)
            .Take(result.Casualties)
            .ToList();
        foreach (var stand in removed)
        {
            game = game.WithStatus(command.Target, status => status.WithElement(
                stand, element => element with { IsDestroyed = true }));
        }

        var losses = removed.Count == 0
            ? "Nothing was removed"
            : $"{string.Join(", ", removed.Select(id => target.Element(id)!.Name))} removed";

        return GameOutcome.Allowed(game
            .WithStatus(command.Target, status => status with { IsUnderFire = FireEffectivenessCheck.TargetIsUnderFire })
            .WithLog(
                $"{firing.Name} opened a firefight on {target.Name}: rolled {check.Roll} on a {solution.Die} "
                + $"against leadership {solution.Leadership}, {check.Result}, {check.StandsFiring} of "
                + $"{command.Stands.Length} firing. {losses}. {target.Name} is under fire."));
    }

    /// <summary>Whether this firefight can be fought, and why not. Checked before anything is spent.</summary>
    private string? WhyFirefightIsRefused(FirefightCommand command, DirtsideRulesProfile profile)
    {
        if (!HasUnit(command.Firer) || !HasUnit(command.Target))
        {
            return "Both platoons have to be on the table.";
        }

        var firing = Unit(command.Firer);
        var target = Unit(command.Target);

        if (Session.CurrentFrame?.Unit != command.Firer)
        {
            return $"{firing.Name} is not the platoon acting.";
        }

        if (firing.Kind != DirtsideUnitKind.DismountedInfantry)
        {
            return $"{firing.Name} is not infantry, and only stands fight a firefight.";
        }

        if (firing.Side == target.Side)
        {
            return $"{target.Name} is on {firing.Name}'s own side.";
        }

        // Softskin vehicles are small-arms targets in the resolver, and nothing on the roster says
        // which vehicles are soft, so this stays with stands until something can.
        if (target.Kind != DirtsideUnitKind.DismountedInfantry)
        {
            return $"{target.Name} is not infantry. Firing small arms at vehicles is not wired yet.";
        }

        if (ProfileBlocker(firing) is { } unprofiled)
        {
            return unprofiled;
        }

        if (command.Stands.IsDefaultOrEmpty)
        {
            return "A firefight needs at least one stand.";
        }

        if (command.Stands.Distinct().Count() != command.Stands.Length)
        {
            return "A stand can only be named once.";
        }

        foreach (var stand in command.Stands)
        {
            if (firing.Element(stand) is not { } definition)
            {
                return $"{firing.Name} has no stand called '{stand}'.";
            }

            var status = Status(command.Firer).Element(stand);
            if (status.IsDestroyed || status.IsSystemsDown)
            {
                return $"{definition.Name} is out of this firefight.";
            }

            if (definition.Weapon(command.Weapon) is null)
            {
                return $"{definition.Name} is not carrying '{command.Weapon}'.";
            }
        }

        if (target.Element(command.TargetElement) is not { } aimedAt)
        {
            return $"{target.Name} has no stand called '{command.TargetElement}'.";
        }

        if (Status(command.Target).Element(command.TargetElement).IsDestroyed)
        {
            return $"{aimedAt.Name} was already destroyed, and a declaration cannot be re-pointed.";
        }

        if (aimedAt.KillThreshold is null)
        {
            return $"{aimedAt.Name}'s card does not give a kill threshold, so a draw has nothing to reach.";
        }

        if (Status(command.Firer).IsUnderFire && profile.RungsLostUnderFire is null)
        {
            return $"{firing.Name} is under fire, and this game's rules profile has no entry for the "
                + "rungs an Under Fire marker costs a fire-effectiveness check.";
        }

        return WhyAResponderMayNotFireOn(command.Firer, command.Target);
    }

    /// <summary>A platoon that gave up its activation to catch a mover is firing on that mover.</summary>
    /// <remarks>The frame beneath its own on the stack is the move it interrupted.</remarks>
    private string? WhyAResponderMayNotFireOn(UnitId firer, UnitId target)
    {
        if (Session.CurrentFrame is not { Kind: FrameKind.Reaction }
            || Session.InnermostWindow is not { Kind: DirtsideActivationPolicy.OpportunityFireWindow })
        {
            return null;
        }

        var mover = Session.FrameStack[^2].Unit;
        return target == mover
            ? null
            : $"{Unit(firer).Name} gave up its activation to fire on {Unit(mover).Name}, not {Unit(target).Name}.";
    }
}
