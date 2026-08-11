using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;
using ForceSignal.Modules.FullThrust.Damage;

namespace ForceSignal.Application.Matches;

/// <content>
/// The firing phase: whose turn it is to shoot, what a shot does, and what falls off the target
/// afterwards.
///
/// The rules themselves live in the Full Thrust module - beam falloff, torpedo ladders, threshold
/// checks, point defence. What is here is the match-level bookkeeping around them: the alternation
/// deciding who may fire, the fire control gating it, and the threshold checks a volley earns,
/// which are deliberately deferred until the firing ship has finished so one volley earns one
/// check against the deepest row it reached.
/// </content>
public sealed partial class InMemoryMatchService
{    public MatchSnapshotDto FireWeapon(Guid matchId, FireWeaponRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var attacker = FindOwnedShip(match, participant.Id, request.AttackerShipId);
            var target = match.Ships.SingleOrDefault(s => s.Id == request.TargetShipId)
                ?? throw new InvalidOperationException("Target ship was not found.");
            var weapon = attacker.Weapons.SingleOrDefault(w => w.Id == request.WeaponId)
                ?? throw new InvalidOperationException("Weapon mount was not found.");

            // A restored match arrives mid-phase with no turn order, so settle one before checking it.
            if (match.FiringParticipantId is null)
            {
                RollFiringInitiative(match);
            }

            var shot = PrepareShot(match, participant, attacker, target, weapon, request.Range, request.Arc);
            if (shot.Blocker is { } blocked)
            {
                throw new InvalidOperationException(blocked);
            }

            var targetArc = shot.TargetArc;
            var solution = shot.Solution;
            var resolver = shot.Resolver;

            // A needle names its system up front, and the shot only makes sense if that system is
            // there to take. This is a pure question about the target, so it is asked here with the
            // other pre-flight guards rather than after the state below has moved. Asking it later
            // wedged the match: naming a system the target no longer has - an ordinary mistake -
            // threw after the attacker had been marked as firing, which then refused every other
            // ship that player owned on the grounds that this one was still shooting, and nothing
            // ever cleared it. A fighter attacker had also already spent its endurance and already
            // been shot at by the target's point defence, so retrying re-rolled the interception
            // against the survivors.
            var needleTarget = weapon.Kind == WeaponKind.NeedleBeam
                ? PlanNeedleShot(target, request.TargetSystem, request.TargetSystemWeaponId)
                : null;

            match.FiringShipId = attacker.Id;

            // A fighter strike is met by the target's close-in fire on the way in, and whatever is
            // shot down never gets to roll. Endurance is spent by both sides of the engagement.
            if (IsFighterGroupShip(attacker))
            {
                SpendFighterEndurance(match, attacker);
                ResolvePointDefenseAgainstFighters(match, attacker, target, request.Range);
                if (SurvivingFighters(attacker) == 0)
                {
                    match.AddLog("Fire", match.Phase.ToString(), $"{DescribeShip(match, attacker)} was wiped out by point defence before it could attack.");
                    match.Touch("FighterStrikeStopped");
                    return ToSnapshot(match);
                }

                // Rebuild the solution: the group rolls one die per fighter still flying.
                solution = solution with
                {
                    Weapon = solution.Weapon with { AttackDice = SurvivingFighters(attacker) },
                };
            }
            else if (IsFighterGroupShip(target))
            {
                SpendFighterEndurance(match, target);
            }

            var result = resolver.Resolve(solution, match.Rules);
            var remainingDamage = result.Damage;
            var damageBefore = CaptureDamage(target);
            if (!match.PendingThresholds.ContainsKey(target.Id))
            {
                match.PendingThresholds[target.Id] = target.HullDamage;
            }

            var wasDestroyed = target.HullDamage >= target.HullMax;
            // A needle picks its way past plating rather than punching through it, so under a layer
            // whose needles draw blood that point goes straight to the hull with armour boxes still
            // standing. Every other weapon meets armour first.
            var ignoresArmor = weapon.Kind == WeaponKind.NeedleBeam && match.Rules.EnhancedNeedleBeams;
            var armorBefore = target.ArmorDamage;
            if (!ignoresArmor)
            {
                target.ArmorDamage = ClampDamage(target.ArmorDamage + remainingDamage, target.ArmorMax);
            }

            var armorApplied = target.ArmorDamage - armorBefore;
            remainingDamage -= armorApplied;
            var hullBefore = target.HullDamage;
            target.HullDamage = ClampDamage(target.HullDamage + remainingDamage, target.HullMax);
            var hullApplied = target.HullDamage - hullBefore;

            var mapRange = MapRangeBetween(attacker, target);
            var rangeDisagreed = RangeDisagreesWithMap(request.Range, mapRange, weapon.Kind, match.Rules);

            var firingResult = new FiringResultState(
                attacker.Id,
                target.Id,
                weapon.Id,
                weapon.Name,
                match.TurnNumber,
                request.Range,
                RangeBand(request.Range, weapon.MaxRange),
                targetArc,
                result.RawDice,
                result.RangePenalty,
                result.ScreenReduction,
                result.SystemPenalty,
                result.Damage,
                armorApplied,
                hullApplied,
                result.DiceRolls,
                weapon.Kind,
                result.ToHitNumber,
                result.IsHit,
                mapRange,
                rangeDisagreed);
            match.FiringResults.Add(firingResult);
            if (weapon.AmmoMax > 0)
            {
                weapon.AmmoUsed = Math.Clamp(weapon.AmmoUsed + 1, 0, weapon.AmmoMax);
            }

            var destroyedNote = !wasDestroyed && target.HullDamage >= target.HullMax ? " Target destroyed." : string.Empty;
            var ammoNote = weapon.AmmoMax > 0 ? $" Ammo {weapon.AmmoUsed}/{weapon.AmmoMax}." : string.Empty;
            var needleNote = string.Empty;
            if (needleTarget is not null)
            {
                needleNote = result.IsHit == true
                    ? $" {ApplySystemLoss(match, target, needleTarget, fromNeedle: true)}"
                    // An enhanced needle that drew blood without taking the system is not a miss,
                    // and reading "nothing hit" beside a point of hull damage would be a lie.
                    : result.Damage > 0 ? " system held, hull holed" : " nothing hit";
            }

            var rollNote = weapon.Kind == WeaponKind.NeedleBeam
                ? $"needed {result.ToHitNumber}, rolled {result.DiceRolls[0]} at the {needleTarget?.Name ?? "target"}:{needleNote}"
                : weapon.Kind == WeaponKind.PulseTorpedo
                ? result.IsHit == true
                    ? $"needed {result.ToHitNumber}+, rolled {result.DiceRolls[0]}, damage die {result.DiceRolls[^1]}"
                    : $"needed {result.ToHitNumber}+, rolled {result.DiceRolls[0]} and missed"
                : result.DiceRolls.Count == 0
                    ? "no dice left to roll"
                    : $"rolled {string.Join(",", result.DiceRolls)}";
            // An enhanced needle's point of damage lands with armour boxes still standing, which
            // would read as an accounting error without a word about it.
            var armorNote = ignoresArmor && result.Damage > 0 ? " ignoring armour" : string.Empty;
            var screenNote = weapon.Kind == WeaponKind.PulseTorpedo
                // Screens do not degrade a torpedo. Say so on a hit, where a reader might otherwise
                // wonder why a screened ship took the full damage, and stay quiet on a miss.
                ? EffectiveScreens(target) > 0 && result.IsHit == true ? " ignoring screens" : string.Empty
                : EffectiveScreens(target) switch
                {
                    > 0 when result.ScreenReduction > 0 => $" vs screens {EffectiveScreens(target)} (-{result.ScreenReduction})",
                    > 0 => $" vs screens {EffectiveScreens(target)}",
                    _ => string.Empty,
                };
            match.AddLog(
                "Fire",
                match.Phase.ToString(),
                $"{DescribeShip(match, attacker)} fired {weapon.Name} at {DescribeShip(match, target)} through {FiringArcs.Describe(targetArc)} arc at range {request.Range} ({firingResult.RangeBand}): {rollNote}{screenNote}{armorNote} for {result.Damage} damage ({armorApplied} armor, {hullApplied} hull). Target delta: {DescribeDamageDelta(damageBefore, CaptureDamage(target))}.{destroyedNote}{ammoNote}");
            if (rangeDisagreed)
            {
                match.AddLog(
                    "Range",
                    match.Phase.ToString(),
                    $"Range check: {DescribeShip(match, attacker)} declared {request.Range} to {DescribeShip(match, target)} but the map measures {mapRange:0.#}. The table decides, so the shot stands - correct the declared range or the ship positions if that gap is wrong.");
            }

            match.Touch("WeaponFired");
            return ToSnapshot(match);
        }
    }
    public MatchSnapshotDto CeaseFire(Guid matchId, CeaseFireRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            if (match.Phase != MatchPhase.Firing)
            {
                throw new InvalidOperationException("Fire can only be closed out during the firing phase.");
            }

            var ship = FindOwnedShip(match, participant.Id, request.ShipId);
            if (match.ActivatedShipIds.Contains(ship.Id))
            {
                throw new InvalidOperationException($"{ship.Name} has already taken its turn to fire.");
            }

            if (match.FiringParticipantId != participant.Id)
            {
                var holder = match.Participants.SingleOrDefault(p => p.Id == match.FiringParticipantId);
                throw new InvalidOperationException($"It is {holder?.DisplayName ?? "the other player"}'s turn to fire.");
            }

            if (match.FiringShipId is { } firingShipId && firingShipId != ship.Id)
            {
                var busy = match.Ships.SingleOrDefault(s => s.Id == firingShipId);
                throw new InvalidOperationException($"{busy?.Name ?? "Another ship"} is still firing. Finish its fire first.");
            }

            var fired = match.FiringShipId == ship.Id;
            match.ActivatedShipIds.Add(ship.Id);
            match.AddLog(
                "Fire",
                match.Phase.ToString(),
                fired
                    ? $"{DescribeShip(match, ship)} completed its fire for the turn."
                    : $"{DescribeShip(match, ship)} held its fire.");
            ResolvePendingThresholds(match);
            PassFiringInitiative(match, participant.Id);
            match.Touch("FireCompleted");
            return ToSnapshot(match);
        }
    }
    /// <summary>
    /// Works out which system a needle beam is sniping at, refusing a shot at something the target
    /// does not have. Hull boxes and dead systems are not on the menu: a needle takes a working system.
    /// </summary>
    private static ShipSystem PlanNeedleShot(ShipState target, ShipSystemKind? kind, Guid? weaponId)
    {
        if (kind is null)
        {
            throw new InvalidOperationException("A needle beam has to name the system it is shooting at.");
        }

        var candidates = SurvivingSystems(target);
        if (kind == ShipSystemKind.Weapon)
        {
            var mount = candidates.FirstOrDefault(system => system.Kind == ShipSystemKind.Weapon && system.WeaponId == weaponId)
                ?? throw new InvalidOperationException("That mount is not on the target, or is already knocked out.");
            return mount;
        }

        return candidates.FirstOrDefault(system => system.Kind == kind)
            ?? throw new InvalidOperationException($"{target.Name} has no working {kind} for a needle to take.");
    }
    /// <summary>
    /// Rolls the threshold check earned by a ship's fire against one target and applies what it
    /// knocked out. A target destroyed by that fire rolls nothing.
    /// </summary>
    private void ResolveThresholds(MatchState match, ShipState target, int hullBefore)
    {
        if (IsDestroyed(target))
        {
            return;
        }

        // How many rows the track has is the layer's call, and where a layer sizes it by class the
        // hull's size band decides. Both shipped layers draw four rows for every hull.
        var band = ShipClassBands.FromIconKey(target.IconKey);
        var rows = _thresholdRules.HullRows(target.HullMax, match.Rules, band);
        var rowsBefore = _thresholdRules.RowsCompleted(hullBefore, target.HullMax, match.Rules, band);
        var rowsAfter = _thresholdRules.RowsCompleted(target.HullDamage, target.HullMax, match.Rules, band);
        if (rowsAfter <= rowsBefore)
        {
            return;
        }

        // One check against the deepest row reached, one point worse per extra row torn through.
        var threshold = Math.Min(rowsAfter, FullThrustLightThresholdRules.DeepestThresholdFor(rows.Count));
        var extra = rowsAfter - rowsBefore - 1;
        var systems = SurvivingSystems(target);
        var result = _thresholdRules.Resolve(new ThresholdCheck(threshold, extra, systems), match.Rules);

        var losses = new List<string>();
        foreach (var system in result.Lost)
        {
            losses.Add(ApplySystemLoss(match, target, system));
        }

        var rollNote = result.Rolls.Count == 0
            ? "no systems left to roll"
            : $"rolled {string.Join(",", result.Rolls.Select(roll => roll.Die))}";
        var lossNote = losses.Count == 0 ? "nothing knocked out" : string.Join(", ", losses);
        var rowNote = extra > 0 ? $"threshold {result.Threshold} (+{extra} rows in one attack)" : $"threshold {result.Threshold}";
        match.AddLog(
            "Threshold",
            match.Phase.ToString(),
            $"{DescribeShip(match, target)} completed hull row {rowsAfter} of {rows.Count}: {rowNote}, systems lost on {result.LostOn} or less, {rollNote}. {lossNote}.");
    }
    /// <summary>
    /// Every system icon still working on a ship, one entry per die the threshold check will roll.
    /// Drives count once: the first hit halves thrust and a second finishes them.
    /// </summary>
    private static List<ShipSystem> SurvivingSystems(ShipState ship)
    {
        var systems = new List<ShipSystem>();
        if (ship.ThrustRating > 0 && ship.DriveDamage < ship.ThrustRating)
        {
            systems.Add(new ShipSystem(ShipSystemKind.Drive, "drives"));
        }

        for (var index = 0; index < ship.FireControlMax - ship.FireControlDamage; index++)
        {
            systems.Add(new ShipSystem(ShipSystemKind.FireControl, "fire control"));
        }

        // Each screen level is its own generator, so each rolls separately.
        for (var level = 0; level < EffectiveScreens(ship); level++)
        {
            systems.Add(new ShipSystem(ShipSystemKind.Screen, "screen generator"));
        }

        for (var bay = 0; bay < EffectiveBays(ship); bay++)
        {
            systems.Add(new ShipSystem(ShipSystemKind.FighterBay, "fighter bay"));
        }

        for (var party = 0; party < ship.DamageControlParties; party++)
        {
            systems.Add(new ShipSystem(ShipSystemKind.DamageControlParty, "damage control party"));
        }

        systems.AddRange(ship.Weapons
            .Where(weapon => !weapon.IsDestroyed)
            .Select(weapon => new ShipSystem(ShipSystemKind.Weapon, weapon.Name, weapon.Id)));
        return systems;
    }
    /// <summary>Applies one knocked-out system and describes it for the log.</summary>
    private static string ApplySystemLoss(MatchState match, ShipState ship, ShipSystem system, bool fromNeedle = false)
    {
        switch (system.Kind)
        {
            case ShipSystemKind.Drive:
                // First hit cuts thrust in half; a second leaves the ship drifting.
                var halved = (ship.ThrustRating + 1) / 2;
                if (fromNeedle)
                {
                    ship.NeedledDrives++;
                }

                if (ship.DriveDamage < halved)
                {
                    ship.DriveDamage = halved;
                    return $"drives cut to thrust {Math.Max(0, ship.ThrustRating - ship.DriveDamage)}";
                }

                ship.DriveDamage = ship.ThrustRating;
                return "drives knocked out";
            case ShipSystemKind.FireControl:
                ship.FireControlDamage = Math.Min(ship.FireControlMax, ship.FireControlDamage + 1);
                if (fromNeedle)
                {
                    ship.NeedledFireControl++;
                }

                return ship.FireControlDamage >= ship.FireControlMax
                    ? "last fire control lost"
                    : "fire control lost";
            case ShipSystemKind.Screen:
                ship.ScreenDamage = Math.Min(ship.ScreenRating, ship.ScreenDamage + 1);
                if (fromNeedle)
                {
                    ship.NeedledScreens++;
                }

                return EffectiveScreens(ship) == 0 ? "screens down" : $"screens dropped to level {EffectiveScreens(ship)}";
            case ShipSystemKind.DamageControlParty:
                ship.DamageControlParties = Math.Max(0, ship.DamageControlParties - 1);
                return "damage control party lost";
            case ShipSystemKind.FighterBay:
                ship.FighterBayDamage = Math.Min(ship.FighterBays, ship.FighterBayDamage + 1);
                if (fromNeedle)
                {
                    ship.NeedledBays++;
                }

                // A bay takes whatever was still sitting in it.
                var stranded = match.Ships.FirstOrDefault(candidate => candidate.HomeCarrierShipId == ship.Id
                    && IsFighterGroupShip(candidate)
                    && candidate.FighterStatus == "Docked"
                    && !IsDestroyed(candidate));
                if (stranded is not null)
                {
                    stranded.HullDamage = stranded.HullMax;
                    return $"fighter bay destroyed with {stranded.Name} aboard";
                }

                return "fighter bay destroyed";
            case ShipSystemKind.Weapon:
                var mount = ship.Weapons.SingleOrDefault(weapon => weapon.Id == system.WeaponId);
                if (mount is null)
                {
                    return system.Name;
                }

                mount.IsDestroyed = true;
                mount.IsNeedleKilled = mount.IsNeedleKilled || fromNeedle;
                return $"{mount.Name} knocked out";
            default:
                return system.Name;
        }
    }
    /// <summary>Distance between two ships on the map, rounded to a tenth of a unit.</summary>
    private static decimal MapRangeBetween(ShipState attacker, ShipState target) =>
        Math.Round((decimal)Math.Sqrt(
            Math.Pow((double)(target.PositionX - attacker.PositionX), 2)
            + Math.Pow((double)(target.PositionY - attacker.PositionY), 2)), 1);
    /// <summary>
    /// How wide a range band is for this weapon. Both widths are the player's; which one a weapon
    /// reads is the app's business.
    /// </summary>
    private static int RangeBandWidth(WeaponKind kind, RulesProfile rules) =>
        Math.Max(1, kind == WeaponKind.PulseTorpedo ? rules.TorpedoBandWidth : rules.BeamRangeBandWidth);
    /// <summary>
    /// Whether a declared range disagrees with the map enough to be worth saying. The table is the
    /// authority on distance, so this never refuses a shot - it flags the two cases a player would
    /// want to know about: the declared range sits in a different band than the map, which changes
    /// the dice, or the two numbers are simply far apart, which usually means a mistyped range or a
    /// ship that was never dragged to where it actually sits.
    /// </summary>
    private static bool RangeDisagreesWithMap(int declaredRange, decimal mapRange, WeaponKind kind, RulesProfile rules)
    {
        var bandWidth = RangeBandWidth(kind, rules);
        var declaredBand = Math.Max(0, declaredRange - 1) / bandWidth;
        var mappedBand = (int)Math.Max(0, Math.Ceiling(mapRange) - 1) / bandWidth;
        return declaredBand != mappedBand || Math.Abs(declaredRange - mapRange) > bandWidth / 2m;
    }
    /// <summary>
    /// The target's point defence firing at an incoming fighter group. Kills come straight off the
    /// group's strength. Point defence reaches 6mu and may fire through the aft arc, so nothing but
    /// distance limits it.
    /// </summary>
    private void ResolvePointDefenseAgainstFighters(MatchState match, ShipState fighters, ShipState target, int range)
    {
        if (target.PointDefenseSystems <= 0 || range > _pointDefenseRules.RangeFor(match.Rules))
        {
            return;
        }

        var incoming = SurvivingFighters(fighters);
        var defense = _pointDefenseRules.Resolve(target.PointDefenseSystems, incoming, match.Rules);
        if (defense.Kills > 0)
        {
            fighters.HullDamage = ClampDamage(fighters.HullDamage + defense.Kills, fighters.HullMax);
        }

        var overkillNote = defense.Overkill > 0 ? $" {defense.Overkill} kill(s) wasted." : string.Empty;
        match.AddLog(
            "PointDefense",
            match.Phase.ToString(),
            $"{DescribeShip(match, target)} point defence fired at {DescribeShip(match, fighters)} at range {range}: rolled {string.Join(",", defense.Rolls)} and shot down {defense.Kills} of {incoming} fighter(s).{overkillNote}");
    }
    /// <summary>
    /// Resolves every salvo counter on the table now that the ships have finished moving. A salvo
    /// attacks the closest enemy within its attack radius of the point of aim, and is wasted if
    /// nothing is in reach.
    /// </summary>
    private void ResolveSalvoMissiles(MatchState match)
    {
        foreach (var marker in match.OrdnanceMarkers.Where(IsActiveSalvo).ToArray())
        {
            var target = match.Ships
                .Where(ship => !IsDestroyed(ship)
                    && match.Fleets.Single(f => f.Id == ship.FleetId).OwnerParticipantId != marker.OwnerParticipantId)
                .Select(ship => (Ship: ship, Range: DistanceToMarker(marker, ship)))
                .Where(entry => entry.Range <= _salvoRules.AttackRadiusFor(match.Rules))
                .OrderBy(entry => entry.Range)
                .Select(entry => entry.Ship)
                .FirstOrDefault();

            if (target is null)
            {
                marker.Status = "Expired";
                match.AddLog(
                    "Ordnance",
                    match.Phase.ToString(),
                    $"{marker.Name} found nothing within {_salvoRules.AttackRadiusFor(match.Rules)} of its point of aim and was wasted.");
                continue;
            }

            var attack = _salvoRules.Resolve(target.PointDefenseSystems, match.Rules);
            var damageBefore = CaptureDamage(target);
            var hullBefore = target.HullDamage;
            var (armorApplied, hullApplied) = ApplyMissileDamage(target, attack.Damage);
            marker.Status = "Resolved";

            var defenseNote = target.PointDefenseSystems > 0
                ? $" Point defence rolled {string.Join(",", attack.PointDefense.Rolls)} and stopped {attack.PointDefense.Kills}."
                : " The target had no point defence.";
            match.AddLog(
                "Ordnance",
                match.Phase.ToString(),
                $"{marker.Name} struck {DescribeShip(match, target)}: {attack.MissilesArriving} of {attack.MissilesLaunched} missiles arrived on a {attack.ArrivalRoll}.{defenseNote} {attack.MissilesSurviving} got through rolling {(attack.DamageRolls.Count == 0 ? "nothing" : string.Join(",", attack.DamageRolls))} for {attack.Damage} damage ({armorApplied} armor, {hullApplied} hull), ignoring screens. Target delta: {DescribeDamageDelta(damageBefore, CaptureDamage(target))}.");

            // A salvo can finish a hull row like any other damage, so the check is owed at once -
            // there is no firing ship whose volley could still be open.
            ResolveThresholds(match, target, hullBefore);
        }
    }
    private static bool IsActiveSalvo(OrdnanceMarkerState marker) =>
        marker.Status == "Active" && IsSalvoMarkerType(marker.MarkerType);
    /// <summary>Whether a marker type names something that attacks as a salvo of missiles.</summary>
    private static bool IsSalvoMarkerType(string? markerType) =>
        markerType is not null
        && (markerType.Contains("salvo", StringComparison.OrdinalIgnoreCase)
            || markerType.Contains("missile", StringComparison.OrdinalIgnoreCase));
    private static decimal DistanceToMarker(OrdnanceMarkerState marker, ShipState ship) =>
        (decimal)Math.Sqrt(
            Math.Pow((double)(ship.PositionX - marker.PositionX), 2)
            + Math.Pow((double)(ship.PositionY - marker.PositionY), 2));

    /// <summary>
    /// Missile damage splits differently from a beam's: armour takes half, rounded up, and the rest
    /// goes straight to the hull even when armour boxes are still standing. Armour reduces a salvo
    /// rather than stopping it.
    /// </summary>
    private static (int ArmorApplied, int HullApplied) ApplyMissileDamage(ShipState target, int damage)
    {
        if (damage <= 0)
        {
            return (0, 0);
        }

        var armorRemaining = Math.Max(0, target.ArmorMax - target.ArmorDamage);
        var armorShare = armorRemaining > 0 ? Math.Min(armorRemaining, (damage + 1) / 2) : 0;
        var armorBefore = target.ArmorDamage;
        target.ArmorDamage = ClampDamage(target.ArmorDamage + armorShare, target.ArmorMax);
        var armorApplied = target.ArmorDamage - armorBefore;

        var hullBefore = target.HullDamage;
        target.HullDamage = ClampDamage(target.HullDamage + (damage - armorApplied), target.HullMax);
        return (armorApplied, target.HullDamage - hullBefore);
    }
    /// <summary>The arc the target lies in, relative to the firing ship's nose.</summary>
    private static FiringArc BearingToTarget(ShipState attacker, ShipState target) =>
        FiringArcs.Bearing(
            attacker.CurrentCourse,
            (double)(target.PositionX - attacker.PositionX),
            (double)(target.PositionY - attacker.PositionY));

    private static (decimal X, decimal Y) EstimatePositionFromResult(decimal x, decimal y, MovementResult result, int tableWidth, int tableDepth)
    {
        var (path, _) = WalkPath(x, y, result, tableWidth, tableDepth);
        return (path[^1].X, path[^1].Y);
    }

    /// <summary>
    /// Flies a resolved move leg by leg and records where the ship is at each turn of the helm:
    /// the starting position first, then the end of every segment. Also reports whether the table
    /// edge caught the ship somewhere along the way.
    /// </summary>
    /// <remarks>
    /// Movement resolution and the plotting preview both go through here, which is the point. The
    /// preview's whole job is to answer with the path the turn will actually take, and it can only
    /// promise that while there is one walk of the segments rather than two.
    /// </remarks>
    private static (List<(decimal X, decimal Y)> Path, bool Clamped) WalkPath(
        decimal x,
        decimal y,
        MovementResult result,
        int tableWidth,
        int tableDepth)
    {
        var segments = result.Segments is { Count: > 0 }
            ? result.Segments
            : [new MovementSegment(result.EndingCourse, result.EndingVelocity)];

        var path = new List<(decimal X, decimal Y)>(segments.Count + 1) { (x, y) };
        var currentX = x;
        var currentY = y;
        var clamped = false;
        foreach (var segment in segments)
        {
            bool stepClamped;
            (currentX, currentY, stepClamped) = StepPosition(currentX, currentY, segment.Distance, segment.Course, tableWidth, tableDepth);
            clamped |= stepClamped;
            path.Add((currentX, currentY));
        }

        return (path, clamped);
    }

    private static (decimal X, decimal Y) EstimatePosition(decimal x, decimal y, decimal distance, int course, int tableWidth, int tableDepth)
    {
        var (nextX, nextY, _) = StepPosition(x, y, distance, course, tableWidth, tableDepth);
        return (nextX, nextY);
    }

    /// <summary>
    /// Runs one leg and says whether the table edge cut it short. The flag is what lets the preview
    /// warn a player that their plot leaves the table without the client measuring anything.
    /// </summary>
    private static (decimal X, decimal Y, bool Clamped) StepPosition(
        decimal x,
        decimal y,
        decimal distance,
        int course,
        int tableWidth,
        int tableDepth)
    {
        var radians = course * Math.PI / 6;
        // Round to a thousandth of a measurement unit. The sine of a straight-down course is not
        // exactly zero in floating point, and without this the residue accumulates into positions
        // that read as 20.000000000000001 on a table measured in whole units.
        var nextX = Math.Round(x + ((decimal)Math.Sin(radians) * distance), 3);
        var nextY = Math.Round(y - ((decimal)Math.Cos(radians) * distance), 3);
        var clampedX = ClampPosition(nextX, tableWidth);
        var clampedY = ClampPosition(nextY, tableDepth);
        return (clampedX, clampedY, clampedX != nextX || clampedY != nextY);
    }
    /// <summary>
    /// Opens the firing phase with a die-off. Every player with a ship on the table rolls, highest
    /// takes the initiative, and ties are re-rolled. The winner fires one ship, then the players
    /// alternate a ship at a time.
    /// </summary>
    private void RollFiringInitiative(MatchState match)
    {
        var contenders = match.Participants
            .Where(p => p.IsClaimed && match.Ships.Any(s => !IsDestroyed(s)
                && match.Fleets.Single(f => f.Id == s.FleetId).OwnerParticipantId == p.Id))
            .ToArray();
        if (contenders.Length == 0)
        {
            match.FiringParticipantId = null;
            return;
        }

        if (contenders.Length == 1)
        {
            match.FiringParticipantId = contenders[0].Id;
            return;
        }

        // Re-roll ties a few times. The bound is a safety net against a die source that always
        // returns the same face, not a rules limit.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var rolls = contenders
                .Select(p => (Participant: p, Roll: Math.Clamp(_rollDie(), 1, 6)))
                .ToArray();
            var best = rolls.Max(entry => entry.Roll);
            var leaders = rolls.Where(entry => entry.Roll == best).ToArray();
            match.AddLog(
                "Initiative",
                match.Phase.ToString(),
                $"Firing initiative: {string.Join(", ", rolls.Select(entry => $"{entry.Participant.DisplayName} rolled {entry.Roll}"))}."
                    + (leaders.Length == 1
                        ? $" {leaders[0].Participant.DisplayName} fires first."
                        : " Tied, rolling again."));
            if (leaders.Length == 1)
            {
                match.FiringParticipantId = leaders[0].Participant.Id;
                return;
            }
        }

        match.FiringParticipantId = contenders[0].Id;
        match.AddLog("Initiative", match.Phase.ToString(), $"Initiative stayed tied; {contenders[0].DisplayName} fires first by seating order.");
    }
    /// <summary>True when a ship could still take a turn to fire this phase.</summary>
    private static bool CanTakeFiringTurn(MatchState match, ShipState ship) =>
        !IsDestroyed(ship)
        && !match.ActivatedShipIds.Contains(ship.Id)
        && ship.FireControlMax - ship.FireControlDamage > 0
        && ship.Weapons.Any(weapon => !weapon.IsDestroyed
            && (weapon.AmmoMax == 0 || weapon.AmmoUsed < weapon.AmmoMax)
            && !match.FiringResults.Any(f => f.TurnNumber == match.TurnNumber && f.AttackerShipId == ship.Id && f.WeaponId == weapon.Id));
    /// <summary>Participants who still have a ship that could fire.</summary>
    private static ParticipantState[] ParticipantsWithFireLeft(MatchState match) =>
        [.. match.Participants.Where(p => p.IsClaimed && match.Ships.Any(s =>
            match.Fleets.Single(f => f.Id == s.FleetId).OwnerParticipantId == p.Id && CanTakeFiringTurn(match, s)))];
    /// <summary>
    /// Hands the initiative to the next player with a ship left to fire, wrapping around the seating
    /// order. A single player keeps it and simply activates another ship.
    /// </summary>
    private static void PassFiringInitiative(MatchState match, Guid currentParticipantId)
    {
        var eligible = ParticipantsWithFireLeft(match);
        if (eligible.Length == 0)
        {
            match.FiringParticipantId = null;
            match.AddLog("Initiative", match.Phase.ToString(), "Every ship has fired or has nothing left to fire with.");
            return;
        }

        var order = match.Participants.Where(p => p.IsClaimed).ToArray();
        var startIndex = Array.FindIndex(order, p => p.Id == currentParticipantId);
        for (var step = 1; step <= order.Length; step++)
        {
            var candidate = order[(startIndex + step) % order.Length];
            if (eligible.Any(p => p.Id == candidate.Id))
            {
                match.FiringParticipantId = candidate.Id;
                match.AddLog("Initiative", match.Phase.ToString(), $"{candidate.DisplayName} fires next.");
                return;
            }
        }

        match.FiringParticipantId = eligible[0].Id;
    }
    /// <summary>
    /// Rolls the threshold checks owed by the ship that has been firing, then closes the volley.
    /// Called when a ship declares its fire complete, when another ship starts firing, and when
    /// the firing phase ends, so a check is never left unrolled.
    /// </summary>
    private void ResolvePendingThresholds(MatchState match)
    {
        foreach (var (targetId, hullBefore) in match.PendingThresholds.ToArray())
        {
            var target = match.Ships.SingleOrDefault(s => s.Id == targetId);
            if (target is not null)
            {
                ResolveThresholds(match, target, hullBefore);
            }
        }

        match.PendingThresholds.Clear();
        match.FiringShipId = null;
    }
    /// <summary>Names the 12mu dice band a shot falls in, matching the beam falloff.</summary>
    private static string RangeBand(int range, int maxRange)
    {
        if (range <= 12)
        {
            return "close";
        }

        if (range <= 24)
        {
            return "medium";
        }

        if (range <= Math.Max(36, maxRange))
        {
            return "long";
        }

        return "extreme";
    }
    /// <summary>
    /// Dice a mount actually rolls. A group rolls one die per surviving fighter rather than a fixed
    /// count, so it weakens as it is shot up.
    /// </summary>
    private static int EffectiveAttackDice(ShipState ship, WeaponMountState weapon) =>
        IsFighterGroupShip(ship) ? SurvivingFighters(ship) : weapon.AttackDice;
}
