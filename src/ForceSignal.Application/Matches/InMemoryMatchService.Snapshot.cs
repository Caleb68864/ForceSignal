using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Damage;
using ForceSignal.Modules.FullThrust.Movement;

namespace ForceSignal.Application.Matches;

/// <content>
/// Projecting a match onto the wire, and writing the log a table reads afterwards.
///
/// The snapshot is a projection rather than the state itself, which is what lets it withhold what
/// a player must not see - a commitment hash and its salt never leave the server, and a movement
/// order appears only once it has been revealed and verified.
/// </content>
public sealed partial class InMemoryMatchService
{    private static MatchSnapshotDto ToSnapshot(MatchState match) => new(
        match.Id,
        match.JoinCode,
        match.Name,
        match.Phase.ToString(),
        match.TurnNumber,
        FullThrustLightCinematicRules.ProfileKey,
        match.Rules.Layer.ToString(),
        match.TableWidth,
        match.TableDepth,
        match.Participants.Select(p => new ParticipantDto(p.Id, p.DisplayName, p.Role, p.IsReady, p.IsConnected, p.OrdersComplete)).ToArray(),
        match.Fleets.Select(f => new FleetDto(f.Id, f.OwnerParticipantId, f.Name, f.Faction, f.FleetColor)).ToArray(),
        match.Ships.Select(s => new ShipDto(
            s.Id,
            s.FleetId,
            s.Name,
            s.ClassName,
            s.ThrustRating,
            s.CurrentVelocity,
            s.CurrentCourse,
            s.PositionX,
            s.PositionY,
            s.HullMax,
            s.HullDamage,
            s.ArmorMax,
            s.ArmorDamage,
            s.FireControlMax,
            s.FireControlDamage,
            s.PointDefenseSystems,
            s.FighterBays,
            s.FighterBayDamage,
            s.DamageControlParties,
            s.DriveDamage,
            s.WeaponDamage,
            s.ScreenRating,
            s.ScreenDamage,
            s.Weapons.Select(w => new WeaponMountDto(w.Id, w.Name, w.AttackDice, w.MaxRange, w.Arcs, w.AmmoMax, w.AmmoUsed, w.ReloadTurns, w.IsDestroyed, w.Kind, w.IsNeedleKilled)).ToArray(),
            s.HullDamage >= s.HullMax,
            // The damage track drawn on screen is the one the match's layer says the ship has, so
            // the row count comes through the same seam the threshold check uses.
            FullThrustLightThresholdRules.HullRowsFor(s.HullMax, RowCountFor(match, s)),
            FullThrustLightThresholdRules.RowsCompletedFor(s.HullDamage, s.HullMax, RowCountFor(match, s)),
            s.IconKey,
            s.FighterEnduranceMax,
            s.FighterEnduranceUsed,
            s.FighterMaxRange,
            s.FighterStatus,
            s.HomeCarrierShipId,
            s.PointsValue,
            s.NeedledFireControl,
            s.NeedledDrives,
            s.NeedledScreens,
            s.NeedledBays,
            s.FighterRelaunchTurn,
            s.FighterGroundedForGame,
            EffectiveScreens(s),
            WorkingFireControl(s),
            FighterReach(s),
            RepairableSystemsOn(s))).ToArray(),
        match.Ships.Select(s =>
        {
            match.Commitments.TryGetValue(s.Id, out var commitment);
            var fleet = match.Fleets.Single(f => f.Id == s.FleetId);
            return new OrderStatusDto(s.Id, fleet.OwnerParticipantId, commitment is not null, commitment?.IsRevealed == true, commitment?.VerificationFailed == true);
        }).ToArray(),
        match.Commitments.Values.Where(c => c.IsRevealed && c.RevealedOrder is not null)
            .Select(c => new RevealedOrderDto(c.ShipId, c.RevealedOrder!.VelocityDelta, c.RevealedOrder.TurnSteps, c.RevealedOrder.TurnDirection, c.RevealedOrder.TurnManeuvers))
            .ToArray(),
        match.Commitments.Values.Where(c => c.Result is not null)
            .Select(c => new MovementResultDto(c.ShipId, c.Result!.StartingVelocity, c.Result.StartingCourse, c.Result.EndingVelocity, c.Result.EndingCourse, c.Result.Segments))
            .ToArray(),
        match.FiringResults.Select(f => new FiringResultDto(
            f.AttackerShipId,
            f.TargetShipId,
            f.WeaponId,
            f.WeaponName,
            f.TurnNumber,
            f.Range,
            f.RangeBand,
            f.Arc,
            f.RawDice,
            f.RangePenalty,
            f.ScreenReduction,
            f.SystemPenalty,
            f.Damage,
            f.ArmorDamageApplied,
            f.HullDamageApplied,
            f.DiceRolls,
            f.WeaponKind,
            f.ToHitNumber,
            f.IsHit,
            f.MapRange,
            f.RangeDisagreed)).ToArray(),
        match.OrdnanceMarkers.Select(o => new OrdnanceMarkerDto(
            o.Id,
            o.OwnerParticipantId,
            o.Name,
            o.MarkerType,
            o.SourceShipId,
            o.TargetShipId,
            o.PositionX,
            o.PositionY,
            o.Course,
            o.Speed,
            o.EnduranceRemaining,
            o.AttackDice,
            o.MaxRange,
            o.Status)).ToArray(),
        match.MatchLog.Select(l => new MatchLogEntryDto(l.Sequence, l.Timestamp, l.TurnNumber, l.Phase, l.Category, l.Message)).ToArray(),
        match.FiringShipId,
        match.FiringParticipantId,
        [.. match.ActivatedShipIds],
        match.Version,
        match.PointsLimit);
    /// <summary>Rows this match's layer draws for one ship's damage track.</summary>
    private static int RowCountFor(MatchState match, ShipState ship) =>
        FullThrustLightThresholdRules.RowCountFor(match.Rules, ShipClassBands.FromIconKey(ship.IconKey));
    private static string DescribeShip(MatchState match, ShipState ship)
    {
        var fleet = match.Fleets.Single(f => f.Id == ship.FleetId);
        var owner = match.Participants.Single(p => p.Id == fleet.OwnerParticipantId);
        var faction = string.IsNullOrWhiteSpace(fleet.Faction) ? fleet.Name : $"{fleet.Name}/{fleet.Faction}";
        return $"{ship.Name} [{faction}, {owner.DisplayName}]";
    }
    private static void AddShipStateSnapshot(MatchState match, string label)
    {
        foreach (var ship in match.Ships)
        {
            var destroyed = ship.HullDamage >= ship.HullMax ? "destroyed" : "operational";
            match.AddLog(
                "Snapshot",
                match.Phase.ToString(),
                $"{label}: {DescribeShip(match, ship)} pos {ship.PositionX:0.#},{ship.PositionY:0.#} on {match.TableWidth}x{match.TableDepth}, v{ship.CurrentVelocity}/c{ship.CurrentCourse}, hull {ship.HullDamage}/{ship.HullMax}, armor {ship.ArmorDamage}/{ship.ArmorMax}, screens {EffectiveScreens(ship)}, {destroyed}.");
        }
    }
    private static void AddFleetSummary(MatchState match, string label)
    {
        foreach (var fleet in match.Fleets)
        {
            var ships = match.Ships.Where(s => s.FleetId == fleet.Id).ToArray();
            if (ships.Length == 0)
            {
                continue;
            }

            var destroyed = ships.Count(s => s.HullDamage >= s.HullMax);
            var crippled = ships.Count(s => s.HullDamage > 0 && s.HullDamage < s.HullMax);
            var hullLost = ships.Sum(s => s.HullDamage);
            var hullTotal = ships.Sum(s => s.HullMax);
            match.AddLog("Summary", match.Phase.ToString(), $"{label}: {fleet.Name} has {ships.Length - destroyed} operational, {crippled} damaged, {destroyed} destroyed; hull lost {hullLost}/{hullTotal}.");
        }
    }
    private static void AddNoFireTelemetry(MatchState match)
    {
        foreach (var ship in match.Ships)
        {
            if (ship.HullDamage >= ship.HullMax)
            {
                match.AddLog("Fire", match.Phase.ToString(), $"{DescribeShip(match, ship)} cannot fire: destroyed.");
                continue;
            }

            if (ship.Weapons.Count == 0)
            {
                match.AddLog("Fire", match.Phase.ToString(), $"{DescribeShip(match, ship)} has no weapons mounted.");
            }
        }
    }
    private static DamageSnapshot CaptureDamage(ShipState ship) => new(
        ship.HullDamage,
        ship.ArmorDamage,
        ship.FireControlDamage,
        ship.DriveDamage,
        ship.WeaponDamage);
    private static string DescribeDamageDelta(DamageSnapshot before, DamageSnapshot after)
    {
        var parts = new List<string>();
        AddDelta(parts, "hull", before.Hull, after.Hull);
        AddDelta(parts, "armor", before.Armor, after.Armor);
        AddDelta(parts, "firecon", before.FireControl, after.FireControl);
        AddDelta(parts, "drive", before.Drive, after.Drive);
        AddDelta(parts, "weapons", before.Weapons, after.Weapons);
        return parts.Count == 0 ? "no change" : string.Join(", ", parts);
    }
    private static string DescribeTurnSequence(MovementOrder order)
    {
        var maneuvers = order.TurnManeuvers is { Count: > 0 }
            ? order.TurnManeuvers.Where(m => m.Steps > 0 && m.Direction != TurnDirection.None)
                .Select(m => $"{(m.Direction == TurnDirection.Port ? "P" : "S")}{m.Steps}")
                .ToArray()
            : order.TurnSteps > 0 && order.TurnDirection != TurnDirection.None
                ? [$"{(order.TurnDirection == TurnDirection.Port ? "P" : "S")}{order.TurnSteps}"]
                : [];

        return maneuvers.Length == 0 ? "no turn" : string.Join(", ", maneuvers);
    }
    private static void AddDelta(List<string> parts, string label, int before, int after)
    {
        if (before == after)
        {
            return;
        }

        var sign = after > before ? "+" : string.Empty;
        parts.Add($"{label} {before}->{after} ({sign}{after - before})");
    }
    private static string PositionEdgeNote(ShipState ship, MatchState match)
    {
        var nearEdge = ship.PositionX <= 3 || ship.PositionY <= 3 || ship.PositionX >= match.TableWidth - 3 || ship.PositionY >= match.TableDepth - 3;
        return nearEdge ? " Near table edge." : string.Empty;
    }
}
