using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;

namespace ForceSignal.Application.Matches;

public sealed partial class InMemoryMatchService
{
    /// <inheritdoc />
    /// <remarks>
    /// Reads and answers. Notably it does not roll the firing die-off the way <see cref="FireWeapon"/>
    /// does for a restored match: settling turn order is a change to the match, and a question about
    /// a shot is not the moment to make one.
    /// </remarks>
    public FiringSolutionDto GetFiringSolution(Guid matchId, FiringSolutionRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var attacker = FindOwnedShip(match, participant.Id, request.AttackerShipId);
            var target = request.TargetShipId is { } targetId
                ? match.Ships.SingleOrDefault(s => s.Id == targetId)
                : null;
            var weapon = request.WeaponId is { } weaponId
                ? attacker.Weapons.SingleOrDefault(w => w.Id == weaponId)
                : null;

            var engagedTargetCount = match.FiringResults
                .Where(f => f.TurnNumber == match.TurnNumber && f.AttackerShipId == attacker.Id && f.WeaponKind != WeaponKind.NeedleBeam)
                .Select(f => f.TargetShipId)
                .Distinct()
                .Count();

            // With no target or no mount picked there is no shot to check yet, but the console still
            // wants the attacker's own numbers so it can show what it is working with.
            if (target is null || weapon is null)
            {
                return new FiringSolutionDto(
                    attacker.Id,
                    target?.Id,
                    false,
                    target is null ? "Pick a target." : "Pick a weapon mount.",
                    target is null ? null : FiringArcs.Describe(BearingToTarget(attacker, target)),
                    target is null ? 0m : MapRangeBetween(attacker, target),
                    false,
                    null,
                    WorkingFireControl(attacker),
                    engagedTargetCount,
                    target is null ? 0 : EffectiveScreens(target),
                    target is null ? [] : NeedleTargetsOn(target));
            }

            var shot = PrepareShot(match, participant, attacker, target, weapon, request.Range, null);
            var mapRange = MapRangeBetween(attacker, target);

            return new FiringSolutionDto(
                attacker.Id,
                target.Id,
                shot.Blocker is null,
                shot.Blocker,
                FiringArcs.Describe(shot.TargetArc),
                mapRange,
                RangeDisagreesWithMap(request.Range, mapRange, weapon.Kind),
                weapon.Kind == WeaponKind.PulseTorpedo ? FullThrustLightPulseTorpedoRules.ToHitNumber(request.Range) : null,
                WorkingFireControl(attacker),
                engagedTargetCount,
                EffectiveScreens(target),
                NeedleTargetsOn(target));
        }
    }

    /// <summary>
    /// Systems a needle beam could still snipe on a target: only live ones, since a needle takes a
    /// working system rather than finishing a dead one.
    /// </summary>
    private static List<SystemOptionDto> NeedleTargetsOn(ShipState target)
    {
        var options = new List<SystemOptionDto>();
        if (WorkingFireControl(target) > 0)
        {
            options.Add(new SystemOptionDto("firecon", "Fire control", "FireControl"));
        }

        if (target.ThrustRating > 0 && target.DriveDamage < target.ThrustRating)
        {
            options.Add(new SystemOptionDto("drive", "Drives", "Drive"));
        }

        if (EffectiveScreens(target) > 0)
        {
            options.Add(new SystemOptionDto("screen", "Screen generator", "Screen"));
        }

        if (target.FighterBays - target.FighterBayDamage > 0)
        {
            options.Add(new SystemOptionDto("bay", "Fighter bay", "FighterBay"));
        }

        options.AddRange(target.Weapons
            .Where(mount => !mount.IsDestroyed)
            .Select(mount => new SystemOptionDto($"mount-{mount.Id}", mount.Name, "Weapon", mount.Id)));

        return options;
    }

    /// <summary>
    /// Everything that has to be true before a shot may be taken, asked once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FireWeapon"/> turns a blocker into a refusal; the firing solution query reports it
    /// so the console can say why before the button is pressed. They must be the same checks or the
    /// screen offers shots the server then refuses - which is exactly what happened while the client
    /// carried its own partial copy of these rules, missing ammunition, mounts that had already
    /// fired, fighter endurance and the needle beam's claim on a fire control system.
    /// </para>
    /// <para>
    /// Only the first blocker is reported. A shot refused for three reasons is still one refusal,
    /// and the order here is the order a player would hit them in.
    /// </para>
    /// </remarks>
    private PreparedShot PrepareShot(
        MatchState match,
        ParticipantState participant,
        ShipState attacker,
        ShipState target,
        WeaponMountState weapon,
        int range,
        FiringArc? declaredArc)
    {
        // Geometry first: the arc a target bears in follows from the two positions and the firing
        // ship's course, and the answer is wanted even when something else blocks the shot.
        var targetArc = BearingToTarget(attacker, target);

        // A needle's reach is the layer's, not the mount's: the enhanced needle is a longer weapon,
        // and a mount recorded under one layer should not out-range the other.
        var reach = weapon.Kind == WeaponKind.NeedleBeam
            ? Math.Min(weapon.MaxRange, match.Rules.NeedleBeamRange)
            : weapon.MaxRange;
        var solution = new FiringSolution(
            new WeaponAttackProfile(weapon.Name, EffectiveAttackDice(attacker, weapon), reach, weapon.Arcs, weapon.Kind),
            range,
            EffectiveScreens(target),
            attacker.WeaponDamage,
            targetArc);
        // A pulse torpedo rolls to hit and then for damage, and screens do not touch it, so it
        // resolves through its own rules rather than the beam table.
        IFiringResolver resolver = weapon.Kind switch
        {
            WeaponKind.PulseTorpedo => _torpedoRules,
            WeaponKind.NeedleBeam => _needleRules,
            _ => _firingRules,
        };

        return new PreparedShot(
            DescribeBlocker(match, participant, attacker, target, weapon, solution, resolver, targetArc, declaredArc),
            solution,
            resolver,
            targetArc);
    }

    private static string? DescribeBlocker(
        MatchState match,
        ParticipantState participant,
        ShipState attacker,
        ShipState target,
        WeaponMountState weapon,
        FiringSolution solution,
        IFiringResolver resolver,
        FiringArc targetArc,
        FiringArc? declaredArc)
    {
        if (match.Phase != MatchPhase.Firing)
        {
            return "Weapons may only fire during the firing phase.";
        }

        if (attacker.Id == target.Id)
        {
            return "A ship cannot fire on itself.";
        }

        if (IsDestroyed(attacker))
        {
            return $"{attacker.Name} is destroyed and cannot fire.";
        }

        if (IsDestroyed(target))
        {
            return $"{target.Name} is already destroyed.";
        }

        if (weapon.IsDestroyed)
        {
            return $"{weapon.Name} was knocked out by a threshold check.";
        }

        if (weapon.AmmoMax > 0 && weapon.AmmoUsed >= weapon.AmmoMax)
        {
            return $"{weapon.Name} has no ammunition remaining.";
        }

        if (match.FiringResults.Any(f => f.TurnNumber == match.TurnNumber && f.AttackerShipId == attacker.Id && f.WeaponId == weapon.Id))
        {
            return $"{weapon.Name} has already fired this turn.";
        }

        if (match.ActivatedShipIds.Contains(attacker.Id))
        {
            return $"{attacker.Name} has already taken its turn to fire.";
        }

        // A match that has not settled its turn order yet blocks nothing on those grounds: firing
        // rolls the die-off first, so the answer would be stale the moment it was acted on.
        if (match.FiringParticipantId is not null)
        {
            if (match.FiringParticipantId != participant.Id)
            {
                var holder = match.Participants.SingleOrDefault(p => p.Id == match.FiringParticipantId);
                return $"It is {holder?.DisplayName ?? "the other player"}'s turn to fire.";
            }

            if (match.FiringShipId is { } firingShipId && firingShipId != attacker.Id)
            {
                var busy = match.Ships.SingleOrDefault(s => s.Id == firingShipId);
                return $"{busy?.Name ?? "Another ship"} is still firing. Finish its fire before starting another ship.";
            }
        }

        // Main batteries cannot engage fighters at all: that is what point defence is for, and it
        // answers a strike automatically when the group attacks. Fighters may shoot at each other.
        if (IsFighterGroupShip(target) && !IsFighterGroupShip(attacker))
        {
            return $"{weapon.Name} cannot engage fighters. Point defence answers a fighter strike when the group attacks.";
        }

        if (IsFighterGroupShip(attacker))
        {
            if (SurvivingFighters(attacker) == 0)
            {
                return $"{attacker.Name} has no fighters left to attack with.";
            }

            if (attacker.FighterEnduranceMax > 0 && attacker.FighterEnduranceUsed >= attacker.FighterEnduranceMax
                && !AlreadyInCombatThisTurn(match, attacker.Id))
            {
                return $"{attacker.Name} is out of combat endurance and must return to rearm before it attacks again.";
            }
        }

        // Fire control directs the guns: with none left a ship cannot shoot at all, and each
        // working system holds exactly one target ship for the turn.
        var workingFireControl = WorkingFireControl(attacker);
        if (workingFireControl == 0)
        {
            return $"{attacker.Name} has no working fire control and cannot fire.";
        }

        var shotsThisTurn = match.FiringResults
            .Where(f => f.TurnNumber == match.TurnNumber && f.AttackerShipId == attacker.Id)
            .ToArray();
        // A needle beam needs a fire control system all to itself, and that firecon cannot direct
        // anything else this turn, so each needle shot spends one outright.
        var needlesFired = shotsThisTurn.Count(f => f.WeaponKind == WeaponKind.NeedleBeam);
        var engagedTargetIds = shotsThisTurn
            .Where(f => f.WeaponKind != WeaponKind.NeedleBeam)
            .Select(f => f.TargetShipId)
            .Distinct()
            .ToArray();
        if (weapon.Kind == WeaponKind.NeedleBeam)
        {
            if (needlesFired + engagedTargetIds.Length >= workingFireControl)
            {
                return $"{attacker.Name} has no fire control free to direct {weapon.Name}: a needle beam needs one of its own.";
            }
        }
        else if (needlesFired > 0 && engagedTargetIds.Length + needlesFired >= workingFireControl
            && !engagedTargetIds.Contains(target.Id))
        {
            return $"{attacker.Name} has its fire control tied up directing needle fire this turn.";
        }
        else if (!engagedTargetIds.Contains(target.Id) && engagedTargetIds.Length + needlesFired >= workingFireControl)
        {
            var engagedNames = string.Join(", ", engagedTargetIds
                .Select(id => match.Ships.SingleOrDefault(s => s.Id == id)?.Name ?? "an unknown ship"));
            return $"{attacker.Name} has {workingFireControl} working fire control system{(workingFireControl == 1 ? string.Empty : "s")} and is already engaging {engagedNames}. Fire the rest of its weapons at {(workingFireControl == 1 ? "that target" : "those targets")}.";
        }

        if (declaredArc is { } declared && declared != targetArc)
        {
            return $"{target.Name} bears {FiringArcs.Describe(targetArc)} of {attacker.Name}, not {FiringArcs.Describe(declared)}. Fix the ship positions if the table disagrees.";
        }

        // The layer travels with the call rather than with the resolver: the resolvers are built
        // once for the service, while the layer is per-match state, so a captured profile would be
        // answering for whichever match happened to build the service.
        var validation = resolver.Validate(solution, match.Rules);
        return validation.IsValid ? null : string.Join(" ", validation.Errors);
    }

    /// <summary>Fire control systems still working. Each one holds a single target ship for the turn.</summary>
    private static int WorkingFireControl(ShipState ship) => Math.Max(0, ship.FireControlMax - ship.FireControlDamage);

    /// <summary>A shot's geometry and resolver, with the reason it cannot be taken if there is one.</summary>
    private sealed record PreparedShot(
        string? Blocker,
        FiringSolution Solution,
        IFiringResolver Resolver,
        FiringArc TargetArc);
}
