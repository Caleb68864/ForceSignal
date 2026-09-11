using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Movement;

namespace ForceSignal.Application.Matches;

/// <content>
/// Rebuilding a match from a file someone kept, and getting the players back into their seats.
///
/// A snapshot is untrusted input - it was picked by hand and may have been edited - so everything
/// here is defensive, and the ceilings are enforced before a byte of it is turned into state. The
/// commitment salts are never exported, so a locked order cannot come back: a restore into a phase
/// that had orders sealed drops to order entry and says so.
/// </content>
public sealed partial class InMemoryMatchService
{    public MatchRestoredResponse RestoreMatch(MatchSnapshotDto snapshot, DateTimeOffset? savedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Participants is null or { Count: 0 })
        {
            throw new InvalidOperationException("Snapshot has no participants to restore.");
        }

        if (snapshot.Ships is null or { Count: 0 })
        {
            throw new InvalidOperationException("Snapshot has no ships to restore.");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.RulesProfileKey)
            && snapshot.RulesProfileKey != FullThrustLightCinematicRules.ProfileKey)
        {
            throw new InvalidOperationException($"Snapshot uses unknown rules profile: {snapshot.RulesProfileKey}.");
        }

        // A snapshot is a file a user hands over, so it is untrusted input that gets turned
        // straight into allocated state. Refuse an oversized one by name rather than letting it
        // quietly consume the server's memory.
        RequireWithin(snapshot.Participants.Count, MaxParticipantsPerMatch, "participants");
        RequireWithin(snapshot.Fleets?.Count ?? 0, MaxFleetsPerMatch, "fleets");
        RequireWithin(snapshot.Ships.Count, MaxShipsPerMatch, "ships");
        RequireWithin(snapshot.OrdnanceMarkers?.Count ?? 0, MaxOrdnanceMarkersPerMatch, "ordnance markers");
        RequireWithin(snapshot.MatchLog?.Count ?? 0, MaxLogEntriesPerMatch, "battle log entries");
        RequireWithin(snapshot.FiringResults?.Count ?? 0, MaxFiringResultsPerMatch, "firing records");
        foreach (var ship in snapshot.Ships)
        {
            RequireWithin(ship.Weapons?.Count ?? 0, MaxWeaponsPerShip, $"weapon mounts on {NormalizeText(ship.Name, "a ship")}");
        }

        // The count of shots is capped above, but each shot carries the dice it rolled, and that
        // list is kept for the after-action review and written back out inside every snapshot. Left
        // uncapped it is the one place in a restore file where a few kilobytes of shots can hold
        // however much the author felt like, and it gets re-serialised on every mutation thereafter.
        foreach (var firing in snapshot.FiringResults ?? [])
        {
            RequireWithin(
                firing.DiceRolls?.Count ?? 0,
                MaxDiceRollsPerFiringResult,
                $"dice on one shot from {NormalizeText(firing.WeaponName, "a weapon")}");
        }

        lock (_gate)
        {
            EvictIdleMatches();
            var matchId = Guid.NewGuid();
            var reusedJoinCode = !string.IsNullOrWhiteSpace(snapshot.JoinCode)
                && !_joinCodes.ContainsKey(snapshot.JoinCode);
            var joinCode = reusedJoinCode ? snapshot.JoinCode : CreateJoinCode();

            // Seat ids have to be unique: a seat is claimed by id, and two seats sharing one would
            // make the claim ambiguous. A blank or repeated id gets a fresh one.
            var usedSeatIds = new HashSet<Guid>();
            var seats = snapshot.Participants
                .Select(p => ParticipantState.CreateSeat(
                    p.Id != Guid.Empty && usedSeatIds.Add(p.Id) ? p.Id : FreshSeatId(usedSeatIds),
                    NormalizeText(p.DisplayName, "Admiral"),
                    p.Role == "Owner" ? "Owner" : "Player",
                    p.IsReady))
                .ToList();

            // Only the owner can advance a turn or change the table, so a snapshot whose owner was
            // edited out would restore into a match nobody can drive. Promote the first seat rather
            // than refusing the file - the players at the table can sort out who holds it.
            if (!seats.Any(seat => seat.Role == "Owner"))
            {
                seats[0] = ParticipantState.CreateSeat(seats[0].Id, seats[0].DisplayName, "Owner", seats[0].IsReady);
            }

            var match = new MatchState(matchId, joinCode, NormalizeText(snapshot.Name, "Space Fleet Match"), seats[0])
            {
                Rules = (snapshot.Rules ?? RulesProfile.Empty).Normalized(),
                TableWidth = Math.Clamp(snapshot.TableWidth, 24, 144),
                TableDepth = Math.Clamp(snapshot.TableDepth, 24, 96),
                TurnNumber = Math.Max(1, snapshot.TurnNumber),
                PointsLimit = ClampPoints(snapshot.PointsLimit)
            };
            foreach (var seat in seats.Skip(1))
            {
                match.Participants.Add(seat);
            }

            var seatIds = seats.Select(s => s.Id).ToHashSet();
            var fleetIdMap = NewIdMap(snapshot.Fleets?.Select(f => f.Id), "fleet");
            var shipIdMap = NewIdMap(snapshot.Ships.Select(s => s.Id), "ship");
            foreach (var fleet in snapshot.Fleets ?? [])
            {
                var ownerId = seatIds.Contains(fleet.OwnerParticipantId) ? fleet.OwnerParticipantId : seats[0].Id;
                match.Fleets.Add(new FleetState(
                    fleetIdMap[fleet.Id],
                    ownerId,
                    NormalizeText(fleet.Name, "Fleet"),
                    NormalizeOptionalText(fleet.Faction),
                    NormalizeFleetColor(fleet.FleetColor)));
            }

            foreach (var ship in snapshot.Ships)
            {
                if (!fleetIdMap.TryGetValue(ship.FleetId, out var restoredFleetId))
                {
                    throw new InvalidOperationException($"Ship {ship.Name} references a fleet that is not in the snapshot.");
                }

                var iconKey = NormalizeIconKey(ship.IconKey, ship.ClassName);
                var fighterEnduranceMax = NormalizeFighterEnduranceMax(ship.FighterEnduranceMax, iconKey, ship.ClassName);
                var restoredShip = new ShipState(
                    shipIdMap[ship.Id],
                    restoredFleetId,
                    NormalizeText(ship.Name, "Unnamed Ship"),
                    NormalizeOptionalText(ship.ClassName),
                    Math.Clamp(ship.ThrustRating, 0, 20),
                    Math.Max(0, ship.CurrentVelocity),
                    NormalizeCourse(ship.CurrentCourse),
                    Math.Clamp(ship.HullMax, 1, 80),
                    Math.Clamp(ship.ArmorMax, 0, 40),
                    ClampPosition(ship.PositionX, match.TableWidth),
                    ClampPosition(ship.PositionY, match.TableDepth),
                    Math.Clamp(ship.ScreenRating, 0, match.Rules.MaxScreenLevel),
                    NormalizeRestoredWeapons(ship.Weapons),
                    iconKey)
                {
                    FighterEnduranceMax = fighterEnduranceMax,
                    FighterEnduranceUsed = NormalizeFighterEnduranceUsed(ship.FighterEnduranceUsed, fighterEnduranceMax),
                    FighterMaxRange = NormalizeFighterMaxRange(ship.FighterMaxRange, iconKey, ship.ClassName),
                    FighterStatus = NormalizeFighterStatus(ship.FighterStatus, iconKey, ship.ClassName),
                    // A group the deck crews have not finished with stays that way across a save.
                    FighterRelaunchTurn = Math.Clamp(ship.FighterRelaunchTurn, 0, 9999),
                    FighterGroundedForGame = ship.FighterGroundedForGame,
                    PointsValue = ClampPoints(ship.PointsValue),
                    FireControlMax = ClampFireControl(ship.FireControlMax),
                    PointDefenseSystems = ClampPointDefense(ship.PointDefenseSystems),
                    FighterBays = ClampFighterBays(ship.FighterBays),
                    DamageControlParties = ClampDamageControl(ship.DamageControlParties),
                };
                restoredShip.HullDamage = ClampDamage(ship.HullDamage, restoredShip.HullMax);
                restoredShip.ArmorDamage = ClampDamage(ship.ArmorDamage, restoredShip.ArmorMax);
                restoredShip.FireControlDamage = ClampDamage(ship.FireControlDamage, restoredShip.FireControlMax);
                restoredShip.ScreenDamage = ClampDamage(ship.ScreenDamage, restoredShip.ScreenRating);
                restoredShip.FighterBayDamage = ClampDamage(ship.FighterBayDamage, restoredShip.FighterBays);
                restoredShip.DriveDamage = ClampDamage(ship.DriveDamage, restoredShip.ThrustRating);
                restoredShip.WeaponDamage = ClampDamage(ship.WeaponDamage, 12);
                // What a needle cut out is beyond damage control, and has to come back as such:
                // without this a restored ship could repair systems the rules say are gone for
                // good. A needled count can never exceed the damage it is part of.
                restoredShip.NeedledFireControl = ClampDamage(ship.NeedledFireControl, restoredShip.FireControlDamage);
                restoredShip.NeedledDrives = ClampDamage(ship.NeedledDrives, 2);
                restoredShip.NeedledScreens = ClampDamage(ship.NeedledScreens, restoredShip.ScreenDamage);
                restoredShip.NeedledBays = ClampDamage(ship.NeedledBays, restoredShip.FighterBayDamage);
                match.Ships.Add(restoredShip);
            }

            // Carrier links resolve only once every ship exists.
            foreach (var ship in snapshot.Ships.Where(s => s.HomeCarrierShipId is not null))
            {
                var restoredShip = match.Ships.SingleOrDefault(s => s.Id == shipIdMap[ship.Id]);
                if (restoredShip is not null)
                {
                    restoredShip.HomeCarrierShipId = ValidateCarrierId(match, MapShipId(shipIdMap, ship.HomeCarrierShipId));
                }
            }

            foreach (var marker in snapshot.OrdnanceMarkers ?? [])
            {
                var ownerId = seatIds.Contains(marker.OwnerParticipantId) ? marker.OwnerParticipantId : seats[0].Id;
                match.OrdnanceMarkers.Add(new OrdnanceMarkerState(
                    marker.Id == Guid.Empty ? Guid.NewGuid() : marker.Id,
                    ownerId,
                    NormalizeOrdnanceText(marker.Name, "Salvo"),
                    NormalizeOrdnanceText(marker.MarkerType, "Missile"),
                    MapShipId(shipIdMap, marker.SourceShipId),
                    MapShipId(shipIdMap, marker.TargetShipId),
                    ClampPosition(marker.PositionX, match.TableWidth),
                    ClampPosition(marker.PositionY, match.TableDepth),
                    NormalizeCourse(marker.Course),
                    Math.Clamp(marker.Speed, 0, 72),
                    Math.Clamp(marker.EnduranceRemaining, 0, 24),
                    Math.Clamp(marker.AttackDice, 0, 24),
                    Math.Clamp(marker.MaxRange, 0, 120),
                    NormalizeOrdnanceStatus(marker.Status)));
            }

            // Normalized like every other string in the file. The count of entries was already
            // capped; each one's text has to be too, or a log line is a place to park a megabyte.
            foreach (var entry in snapshot.MatchLog ?? [])
            {
                match.AddRestoredLog(new MatchLogEntryState(
                    entry.Sequence,
                    entry.Timestamp,
                    entry.TurnNumber,
                    NormalizeText(entry.Phase, match.Phase.ToString()),
                    NormalizeText(entry.Category, "Log"),
                    NormalizeLogMessage(entry.Message)));
            }

            foreach (var firing in snapshot.FiringResults ?? [])
            {
                if (!shipIdMap.ContainsKey(firing.AttackerShipId) || !shipIdMap.ContainsKey(firing.TargetShipId))
                {
                    continue;
                }

                match.FiringResults.Add(new FiringResultState(
                    shipIdMap[firing.AttackerShipId],
                    shipIdMap[firing.TargetShipId],
                    firing.WeaponId,
                    NormalizeText(firing.WeaponName, "Weapon"),
                    firing.TurnNumber,
                    firing.Range,
                    NormalizeText(firing.RangeBand, "close"),
                    firing.Arc,
                    firing.RawDice,
                    firing.RangePenalty,
                    firing.ScreenReduction,
                    firing.SystemPenalty,
                    firing.Damage,
                    firing.ArmorDamageApplied,
                    firing.HullDamageApplied,
                    firing.DiceRolls ?? [],
                    firing.WeaponKind,
                    firing.ToHitNumber,
                    firing.IsHit,
                    firing.MapRange,
                    firing.RangeDisagreed));
            }

            var (phase, lockedOrdersDropped) = RestorePhase(snapshot.Phase);
            match.Phase = phase;
            if (match.Phase == MatchPhase.Firing)
            {
                RestoreFiringTurnOrder(match, snapshot, seatIds, shipIdMap);
            }
            RestoreRevealedCommitments(match, snapshot, phase, shipIdMap);

            var savedNote = savedAt is null ? "an exported snapshot" : $"a snapshot saved {savedAt:u}";
            var droppedNote = lockedOrdersDropped
                ? " Locked orders could not be restored; re-lock to continue."
                : string.Empty;
            match.AddLog("Session", match.Phase.ToString(), $"Match restored from {savedNote} into room {joinCode}.{droppedNote}");

            Register(match);
            match.Touch("MatchRestored");
            return new MatchRestoredResponse(
                matchId,
                joinCode,
                reusedJoinCode,
                match.Phase.ToString(),
                lockedOrdersDropped,
                BuildSeats(match),
                ToSnapshot(match));
        }
    }
    /// <summary>
    /// Rebuilds commitments from the snapshot's revealed orders and movement results. Both are
    /// public once revealed, so nothing secret is reconstructed. This is what lets a restored
    /// Movement phase apply its orders exactly once, and a restored Firing phase keep its trails.
    /// </summary>
    private void RestoreRevealedCommitments(
        MatchState match,
        MatchSnapshotDto snapshot,
        MatchPhase phase,
        Dictionary<Guid, Guid> shipIdMap)
    {
        if (phase is not (MatchPhase.Movement or MatchPhase.Firing))
        {
            return;
        }

        foreach (var revealed in snapshot.RevealedOrders ?? [])
        {
            var ship = shipIdMap.TryGetValue(revealed.ShipId, out var restoredShipId)
                ? match.Ships.SingleOrDefault(s => s.Id == restoredShipId)
                : null;
            var fleet = ship is null ? null : match.Fleets.SingleOrDefault(f => f.Id == ship.FleetId);
            if (ship is null || fleet is null)
            {
                continue;
            }

            var order = new MovementOrder(
                revealed.VelocityDelta,
                revealed.TurnSteps,
                revealed.TurnDirection,
                revealed.TurnManeuvers);
            var result = snapshot.MovementResults?.SingleOrDefault(m => m.ShipId == revealed.ShipId);
            match.Commitments[ship.Id] = new OrderCommitmentState(
                ship.Id,
                fleet.OwnerParticipantId,
                _commitments.CreateHash(_rules.Normalize(order), Guid.NewGuid().ToString("n")),
                true,
                false,
                order,
                result is null
                    ? null
                    : new MovementResult(
                        result.StartingVelocity,
                        result.StartingCourse,
                        result.EndingVelocity,
                        result.EndingCourse,
                        result.Segments));
        }
    }
    /// <summary>
    /// Puts the firing phase back where it was, rather than starting it again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The snapshot does carry the firing turn order - whose turn it is, which ships have already
    /// taken theirs, and which one is part-way through a volley - and this used to ignore all three
    /// and roll a fresh die-off. That handed the initiative to whoever the dice liked rather than
    /// whoever held it, and, worse, forgot which ships had already fired: every ship that had taken
    /// its turn before the export got another one. A restore is meant to put the table back, not to
    /// give one side a second round of shooting.
    /// </para>
    /// <para>
    /// Ship ids are reissued on restore, so they come back through the map. A participant kept its
    /// id unless the snapshot forced a fresh one, so an id that is no longer a seat is dropped. If
    /// the snapshot turns out to carry no usable turn order at all - an older export, or one
    /// hand-edited - a die-off is rolled as before, because leaving nobody able to shoot is worse.
    /// </para>
    /// </remarks>
    private void RestoreFiringTurnOrder(
        MatchState match,
        MatchSnapshotDto snapshot,
        HashSet<Guid> seatIds,
        Dictionary<Guid, Guid> shipIdMap)
    {
        foreach (var activated in snapshot.ActivatedShipIds ?? [])
        {
            if (shipIdMap.TryGetValue(activated, out var restoredShipId))
            {
                match.ActivatedShipIds.Add(restoredShipId);
            }
        }

        match.FiringShipId = MapShipId(shipIdMap, snapshot.FiringShipId);

        if (snapshot.FiringParticipantId is Guid holder && seatIds.Contains(holder))
        {
            match.FiringParticipantId = holder;
            return;
        }

        RollFiringInitiative(match);
    }

    /// <summary>Mints a seat id that no other seat in this restore is already using.</summary>
    private static Guid FreshSeatId(HashSet<Guid> used)
    {
        Guid id;
        do
        {
            id = Guid.NewGuid();
        } while (!used.Add(id));

        return id;
    }
    /// <summary>Maps every id in a restored collection to a fresh id, rejecting duplicates.</summary>
    private static Dictionary<Guid, Guid> NewIdMap(IEnumerable<Guid>? ids, string label)
    {
        var map = new Dictionary<Guid, Guid>();
        foreach (var id in ids ?? [])
        {
            if (!map.TryAdd(id, Guid.NewGuid()))
            {
                throw new InvalidOperationException($"Snapshot contains duplicate {label} ids.");
            }
        }

        return map;
    }
    private static Guid? MapShipId(Dictionary<Guid, Guid> shipIdMap, Guid? sourceId) =>
        sourceId is Guid id && shipIdMap.TryGetValue(id, out var mapped) ? mapped : null;

    private static (MatchPhase Phase, bool LockedOrdersDropped) RestorePhase(string? exported) => exported switch
    {
        nameof(MatchPhase.FleetSetup) => (MatchPhase.FleetSetup, false),
        nameof(MatchPhase.Movement) => (MatchPhase.Movement, false),
        nameof(MatchPhase.Firing) => (MatchPhase.Firing, false),
        // Commitment salts are never exported, so locked orders cannot come back.
        nameof(MatchPhase.OrdersLocked) or nameof(MatchPhase.Reveal) => (MatchPhase.OrderEntry, true),
        _ => (MatchPhase.OrderEntry, false),
    };
    public IReadOnlyList<MatchSeatDto> GetSeats(Guid matchId, string joinCode)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            // Seat ids are what a claim is addressed to, so handing them out to anyone who knows
            // the match id is what let a stranger take a seat. The room code is the thing a
            // returning player actually has.
            RequireJoinCode(match, joinCode);
            return BuildSeats(match);
        }
    }
    /// <summary>
    /// Checks the room code a caller presented against the match. Used where there is no
    /// participant token to check yet, which is the whole point of the restore-and-claim flow.
    /// </summary>
    /// <remarks>
    /// Compared in constant time, like a participant token. A room code is short and the comparison
    /// is one of many things a request does, so timing it is far-fetched - but it is a credential,
    /// and this is the one place a credential was compared with an early-out. Case is folded first,
    /// because the code is read aloud and typed back by someone who may not reach for shift.
    /// </remarks>
    private static void RequireJoinCode(MatchState match, string? joinCode)
    {
        var presented = System.Text.Encoding.UTF8.GetBytes(NormalizeText(joinCode, string.Empty).ToUpperInvariant());
        var expected = System.Text.Encoding.UTF8.GetBytes(match.JoinCode.ToUpperInvariant());
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(presented, expected))
        {
            throw new UnauthorizedAccessException("The room code does not match this match.");
        }
    }
    public MatchJoinedResponse ClaimSeat(Guid matchId, Guid participantId, ClaimSeatRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);

            // Claiming a seat mints a full participant token, and a restored match has no prior
            // token to present - that is the whole point of the flow. So the room code stands in
            // for one. Without it, knowing a match id was enough to take any unclaimed seat,
            // including the owner's, which meant the table's controls and the legitimate player
            // locked out with no way back.
            RequireJoinCode(match, request.JoinCode);

            var seat = match.Participants.SingleOrDefault(p => p.Id == participantId)
                ?? throw new NotFoundException("Seat was not found.");
            if (seat.IsClaimed)
            {
                throw new InvalidOperationException($"{seat.DisplayName} has already been claimed on another device.");
            }

            var token = seat.Claim();
            seat.IsConnected = false;
            // The seat keeps the name it was saved with unless the claiming device offers one. The
            // contract always said it could; the service used to take the field and drop it.
            seat.DisplayName = NormalizeText(request.DisplayName, seat.DisplayName);
            match.AddLog("Session", match.Phase.ToString(), $"{seat.DisplayName} claimed their seat in the restored match.");
            match.Touch("SeatClaimed");
            return new MatchJoinedResponse(match.Id, match.JoinCode, seat.Id, token);
        }
    }
    private static MatchSeatDto[] BuildSeats(MatchState match) =>
        match.Participants.Select(p =>
        {
            var fleets = match.Fleets.Where(f => f.OwnerParticipantId == p.Id).ToArray();
            return new MatchSeatDto(
                p.Id,
                p.DisplayName,
                p.Role,
                p.IsClaimed,
                fleets.Length,
                match.Ships.Count(s => fleets.Any(f => f.Id == s.FleetId)));
        }).ToArray();
    public MatchIdentityDto FindMatchByCode(string joinCode)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(joinCode) || !_joinCodes.TryGetValue(joinCode.Trim(), out var matchId))
            {
                throw new NotFoundException("Room code was not found.");
            }

            var match = _matches[matchId];
            match.LastActivity = DateTimeOffset.UtcNow;
            return new MatchIdentityDto(match.Id, match.JoinCode, match.Participants.Any(p => !p.IsClaimed));
        }
    }
}
