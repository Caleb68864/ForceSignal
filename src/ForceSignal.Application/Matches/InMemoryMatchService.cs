using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;
using ForceSignal.Modules.FullThrust.Damage;
using ForceSignal.Modules.FullThrust.Movement;

namespace ForceSignal.Application.Matches;

/// <summary>Application boundary for match, fleet, ship, order, movement, and firing workflows.</summary>
public interface IMatchService
{
    /// <summary>Creates a match and owner session.</summary>
    MatchCreatedResponse CreateMatch(CreateMatchRequest request);

    /// <summary>Joins an existing match and creates a participant session.</summary>
    MatchJoinedResponse JoinMatch(JoinMatchRequest request);

    /// <summary>Gets the authoritative match snapshot.</summary>
    MatchSnapshotDto GetSnapshot(Guid matchId);

    /// <summary>Returns true when the token belongs to a participant of the match.</summary>
    bool IsMatchParticipant(Guid matchId, string participantToken);

    /// <summary>Records realtime connection state for a participant. Returns null when unknown.</summary>
    MatchSnapshotDto? SetParticipantConnection(Guid matchId, string participantToken, bool isConnected);

    /// <summary>Rebuilds a match from an exported snapshot. Participants return as unclaimed seats.</summary>
    MatchRestoredResponse RestoreMatch(MatchSnapshotDto snapshot, DateTimeOffset? savedAt);

    /// <summary>Lists seats for a match, showing which are still claimable.</summary>
    IReadOnlyList<MatchSeatDto> GetSeats(Guid matchId);

    /// <summary>Claims an unclaimed seat in a restored match and issues a participant token.</summary>
    MatchJoinedResponse ClaimSeat(Guid matchId, Guid participantId, ClaimSeatRequest request);

    /// <summary>Resolves a room code to a match id and whether seats are still claimable.</summary>
    MatchIdentityDto FindMatchByCode(string joinCode);

    /// <summary>Updates participant readiness during setup.</summary>
    MatchSnapshotDto SetReady(Guid matchId, string participantToken, bool isReady);

    /// <summary>Updates table dimensions.</summary>
    MatchSnapshotDto UpdateTable(Guid matchId, UpdateMatchTableRequest request);

    /// <summary>Sets the agreed points ceiling per player. Zero means unlimited.</summary>
    MatchSnapshotDto UpdatePointsLimit(Guid matchId, UpdateMatchPointsLimitRequest request);

    /// <summary>Creates a fleet in a match.</summary>
    MatchSnapshotDto CreateFleet(Guid matchId, CreateFleetRequest request);

    /// <summary>Creates a ship in an owned fleet.</summary>
    MatchSnapshotDto CreateShip(Guid fleetId, CreateShipRequest request);

    /// <summary>Duplicates an owned ship.</summary>
    MatchSnapshotDto DuplicateShip(Guid shipId, DuplicateShipRequest request);

    /// <summary>Updates editable ship profile fields.</summary>
    MatchSnapshotDto UpdateShipProfile(Guid shipId, UpdateShipProfileRequest request);

    /// <summary>Updates tracked ship damage.</summary>
    MatchSnapshotDto UpdateShipDamage(Guid shipId, UpdateShipDamageRequest request);

    /// <summary>Updates fighter launch, recovery, and endurance tracking.</summary>
    MatchSnapshotDto UpdateFighterOperations(Guid shipId, UpdateFighterOperationsRequest request);

    /// <summary>Adds a launched ordnance marker to a match.</summary>
    MatchSnapshotDto CreateOrdnanceMarker(Guid matchId, CreateOrdnanceMarkerRequest request);

    /// <summary>Updates a launched ordnance marker.</summary>
    MatchSnapshotDto UpdateOrdnanceMarker(Guid markerId, UpdateOrdnanceMarkerRequest request);

    /// <summary>Removes a launched ordnance marker.</summary>
    MatchSnapshotDto RemoveOrdnanceMarker(Guid markerId, RemoveOrdnanceMarkerRequest request);

    /// <summary>Declares a participant has finished plotting, leaving unordered ships to hold course.</summary>
    MatchSnapshotDto DeclareOrdersComplete(Guid matchId, DeclareOrdersCompleteRequest request);

    /// <summary>Commits a hidden movement order.</summary>
    MatchSnapshotDto CommitOrder(Guid matchId, CommitOrderRequest request);

    /// <summary>Reveals and verifies a hidden movement order.</summary>
    MatchSnapshotDto RevealOrder(Guid matchId, RevealOrderRequest request);

    /// <summary>Resolves one weapon attack.</summary>
    MatchSnapshotDto FireWeapon(Guid matchId, FireWeaponRequest request);

    /// <summary>Ends a ship's fire for the turn and rolls any threshold checks it earned.</summary>
    MatchSnapshotDto CeaseFire(Guid matchId, CeaseFireRequest request);

    /// <summary>Advances match phase or starts the next turn.</summary>
    MatchSnapshotDto AdvanceTurn(Guid matchId, string participantToken);
}

/// <summary>In-memory implementation of match orchestration for local and early self-hosted play.</summary>
/// <param name="rollDie">Die source for firing resolution, injectable so tests are deterministic.</param>
public sealed class InMemoryMatchService(Func<int>? rollDie = null) : IMatchService
{
    private readonly FullThrustLightCinematicRules _rules = new();
    private readonly FullThrustLightFiringRules _firingRules = new(rollDie);
    private readonly FullThrustLightThresholdRules _thresholdRules = new(rollDie);
    private readonly Sha256CommitmentService _commitments = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, MatchState> _matches = [];
    private readonly Dictionary<string, Guid> _joinCodes = new(StringComparer.OrdinalIgnoreCase);

    public MatchCreatedResponse CreateMatch(CreateMatchRequest request)
    {
        lock (_gate)
        {
            var matchId = Guid.NewGuid();
            var participant = ParticipantState.Create(NormalizeText(request.DisplayName, "Admiral"), "Owner");
            var joinCode = CreateJoinCode();
            var match = new MatchState(matchId, joinCode, NormalizeText(request.MatchName, "Space Fleet Match"), participant)
            {
                TableWidth = Math.Clamp(request.TableWidth, 24, 144),
                TableDepth = Math.Clamp(request.TableDepth, 24, 96)
            };
            match.AddLog("Setup", "FleetSetup", "Match created.");
            _matches.Add(matchId, match);
            _joinCodes.Add(joinCode, matchId);
            return new MatchCreatedResponse(matchId, joinCode, participant.Id, participant.Token);
        }
    }

    public MatchJoinedResponse JoinMatch(JoinMatchRequest request)
    {
        lock (_gate)
        {
            if (!_joinCodes.TryGetValue(request.JoinCode, out var matchId))
            {
                throw new InvalidOperationException("Room code was not found.");
            }

            var match = _matches[matchId];
            if (match.Participants.Any(p => !p.IsClaimed))
            {
                throw new InvalidOperationException("This match was restored from a backup. Claim your seat instead of joining.");
            }

            var participant = ParticipantState.Create(NormalizeText(request.DisplayName, "Player"), "Player");
            match.Participants.Add(participant);
            match.AddLog("Setup", match.Phase.ToString(), $"{participant.DisplayName} joined the match.");
            match.Touch("ParticipantJoined");
            return new MatchJoinedResponse(match.Id, match.JoinCode, participant.Id, participant.Token);
        }
    }

    public MatchSnapshotDto GetSnapshot(Guid matchId)
    {
        lock (_gate)
        {
            return ToSnapshot(FindMatch(matchId));
        }
    }

    public bool IsMatchParticipant(Guid matchId, string participantToken)
    {
        if (string.IsNullOrWhiteSpace(participantToken))
        {
            return false;
        }

        lock (_gate)
        {
            return _matches.TryGetValue(matchId, out var match)
                && match.Participants.Any(p => p.IsClaimed && p.Token == participantToken);
        }
    }

    public MatchSnapshotDto? SetParticipantConnection(Guid matchId, string participantToken, bool isConnected)
    {
        lock (_gate)
        {
            if (!_matches.TryGetValue(matchId, out var match))
            {
                return null;
            }

            var participant = match.Participants.SingleOrDefault(p => p.IsClaimed && p.Token == participantToken);
            if (participant is null || participant.IsConnected == isConnected)
            {
                return null;
            }

            participant.IsConnected = isConnected;
            match.AddLog("Session", match.Phase.ToString(), $"{participant.DisplayName} {(isConnected ? "connected" : "disconnected")}.");
            match.Touch("ParticipantConnectionChanged");
            return ToSnapshot(match);
        }
    }

    public MatchRestoredResponse RestoreMatch(MatchSnapshotDto snapshot, DateTimeOffset? savedAt)
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

        lock (_gate)
        {
            var matchId = Guid.NewGuid();
            var reusedJoinCode = !string.IsNullOrWhiteSpace(snapshot.JoinCode)
                && !_joinCodes.ContainsKey(snapshot.JoinCode);
            var joinCode = reusedJoinCode ? snapshot.JoinCode : CreateJoinCode();

            var seats = snapshot.Participants
                .Select(p => ParticipantState.CreateSeat(
                    p.Id == Guid.Empty ? Guid.NewGuid() : p.Id,
                    NormalizeText(p.DisplayName, "Admiral"),
                    p.Role == "Owner" ? "Owner" : "Player",
                    p.IsReady))
                .ToList();

            var match = new MatchState(matchId, joinCode, NormalizeText(snapshot.Name, "Space Fleet Match"), seats[0])
            {
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
                    Math.Clamp(ship.ScreenRating, 0, 3),
                    NormalizeRestoredWeapons(ship.Weapons),
                    iconKey)
                {
                    FighterEnduranceMax = fighterEnduranceMax,
                    FighterEnduranceUsed = NormalizeFighterEnduranceUsed(ship.FighterEnduranceUsed, fighterEnduranceMax),
                    FighterMaxRange = NormalizeFighterMaxRange(ship.FighterMaxRange, iconKey, ship.ClassName),
                    FighterStatus = NormalizeFighterStatus(ship.FighterStatus, iconKey, ship.ClassName),
                    PointsValue = ClampPoints(ship.PointsValue),
                    FireControlMax = ClampFireControl(ship.FireControlMax),
                };
                restoredShip.HullDamage = ClampDamage(ship.HullDamage, restoredShip.HullMax);
                restoredShip.ArmorDamage = ClampDamage(ship.ArmorDamage, restoredShip.ArmorMax);
                restoredShip.FireControlDamage = ClampDamage(ship.FireControlDamage, restoredShip.FireControlMax);
                restoredShip.DriveDamage = ClampDamage(ship.DriveDamage, restoredShip.ThrustRating);
                restoredShip.WeaponDamage = ClampDamage(ship.WeaponDamage, 12);
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

            foreach (var entry in snapshot.MatchLog ?? [])
            {
                match.AddRestoredLog(new MatchLogEntryState(
                    entry.Sequence,
                    entry.Timestamp,
                    entry.TurnNumber,
                    entry.Phase,
                    entry.Category,
                    entry.Message));
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
                    firing.DiceRolls ?? []));
            }

            var (phase, lockedOrdersDropped) = RestorePhase(snapshot.Phase);
            match.Phase = phase;
            RestoreRevealedCommitments(match, snapshot, phase, shipIdMap);

            var savedNote = savedAt is null ? "an exported snapshot" : $"a snapshot saved {savedAt:u}";
            var droppedNote = lockedOrdersDropped
                ? " Locked orders could not be restored; re-lock to continue."
                : string.Empty;
            match.AddLog("Session", match.Phase.ToString(), $"Match restored from {savedNote} into room {joinCode}.{droppedNote}");

            _matches.Add(matchId, match);
            _joinCodes[joinCode] = matchId;
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

    public MatchIdentityDto FindMatchByCode(string joinCode)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(joinCode) || !_joinCodes.TryGetValue(joinCode.Trim(), out var matchId))
            {
                throw new InvalidOperationException("Room code was not found.");
            }

            var match = _matches[matchId];
            return new MatchIdentityDto(match.Id, match.JoinCode, match.Participants.Any(p => !p.IsClaimed));
        }
    }

    public IReadOnlyList<MatchSeatDto> GetSeats(Guid matchId)
    {
        lock (_gate)
        {
            return BuildSeats(FindMatch(matchId));
        }
    }

    public MatchJoinedResponse ClaimSeat(Guid matchId, Guid participantId, ClaimSeatRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var seat = match.Participants.SingleOrDefault(p => p.Id == participantId)
                ?? throw new InvalidOperationException("Seat was not found.");
            if (seat.IsClaimed)
            {
                throw new InvalidOperationException($"{seat.DisplayName} has already been claimed on another device.");
            }

            var token = seat.Claim();
            seat.IsConnected = false;
            match.AddLog("Session", match.Phase.ToString(), $"{seat.DisplayName} claimed their seat in the restored match.");
            match.Touch("SeatClaimed");
            return new MatchJoinedResponse(match.Id, match.JoinCode, seat.Id, token);
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

    public MatchSnapshotDto SetReady(Guid matchId, string participantToken, bool isReady)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, participantToken);
            // An over-strength force cannot declare itself ready; the owner can raise or clear the
            // limit when both sides agree to a mismatch.
            var overBy = isReady ? PointsOverLimit(match, participant.Id) : 0;
            if (overBy > 0)
            {
                throw new InvalidOperationException(
                    $"{participant.DisplayName} is {overBy} points over the {match.PointsLimit} point limit. Trim the fleet or ask the owner to change the limit.");
            }

            participant.IsReady = isReady;
            // A single-device local match (one admiral tracking the table) must be able to start,
            // so readiness gates on ships being present rather than on a second participant.
            if (match.Phase == MatchPhase.FleetSetup && match.Participants.All(p => p.IsReady) && match.Ships.Count > 0 && match.Ships.Count >= match.Participants.Count)
            {
                match.Phase = MatchPhase.OrderEntry;
                match.AddLog("Phase", match.Phase.ToString(), "All crews ready. Order entry opened.");
                AddShipStateSnapshot(match, "Start of order entry");
            }

            match.Touch("ParticipantReadyChanged");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto UpdateTable(Guid matchId, UpdateMatchTableRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            if (participant.Role != "Owner")
            {
                throw new UnauthorizedAccessException("Only the owner can update table dimensions.");
            }

            match.TableWidth = Math.Clamp(request.TableWidth, 24, 144);
            match.TableDepth = Math.Clamp(request.TableDepth, 24, 96);
            match.AddLog("Setup", match.Phase.ToString(), $"Table set to {match.TableWidth} x {match.TableDepth} inches.");
            match.Touch("TableUpdated");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto UpdatePointsLimit(Guid matchId, UpdateMatchPointsLimitRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            if (participant.Role != "Owner")
            {
                throw new UnauthorizedAccessException("Only the owner can set the points limit.");
            }

            match.PointsLimit = ClampPoints(request.PointsLimit);
            match.AddLog(
                "Setup",
                match.Phase.ToString(),
                match.PointsLimit == 0
                    ? "Points limit cleared: fleets may be any size."
                    : $"Points limit set to {match.PointsLimit} per player.");
            match.Touch("PointsLimitUpdated");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto CreateFleet(Guid matchId, CreateFleetRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var fleetName = NormalizeText(request.Name, "Fleet");
            match.Fleets.Add(new FleetState(Guid.NewGuid(), participant.Id, fleetName, request.Faction, NormalizeFleetColor(request.FleetColor)));
            match.AddLog("Setup", match.Phase.ToString(), $"{fleetName} fleet created for {participant.DisplayName}.");
            match.Touch("FleetCreated");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto CreateShip(Guid fleetId, CreateShipRequest request)
    {
        lock (_gate)
        {
            var match = _matches.Values.SingleOrDefault(m => m.Fleets.Any(f => f.Id == fleetId))
                ?? throw new InvalidOperationException("Fleet was not found.");
            var participant = FindParticipant(match, request.ParticipantToken);
            var fleet = match.Fleets.Single(f => f.Id == fleetId);
            if (fleet.OwnerParticipantId != participant.Id)
            {
                throw new UnauthorizedAccessException("You can only add ships to your own fleet.");
            }

            var thrustRating = Math.Clamp(request.ThrustRating, 0, 20);
            var shipState = new ShipMovementState(request.InitialVelocity, request.InitialCourse);
            var validation = _rules.Validate(shipState, thrustRating, new MovementOrder(0, 0, TurnDirection.None));
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(" ", validation.Errors));
            }

            var iconKey = NormalizeIconKey(request.IconKey, request.ClassName);
            var fighterEnduranceMax = NormalizeFighterEnduranceMax(request.FighterEnduranceMax, iconKey, request.ClassName);
            match.Ships.Add(new ShipState(
                Guid.NewGuid(),
                fleet.Id,
                NormalizeText(request.Name, "Unnamed Ship"),
                NormalizeOptionalText(request.ClassName),
                thrustRating,
                request.InitialVelocity,
                request.InitialCourse,
                Math.Clamp(request.HullMax, 1, 80),
                Math.Clamp(request.ArmorMax, 0, 40),
                ClampPosition(request.StartX, match.TableWidth),
                ClampPosition(request.StartY, match.TableDepth),
                Math.Clamp(request.ScreenRating, 0, 3),
                NormalizeWeapons(request.Weapons),
                iconKey)
            {
                FighterEnduranceMax = fighterEnduranceMax,
                FighterEnduranceUsed = NormalizeFighterEnduranceUsed(request.FighterEnduranceUsed, fighterEnduranceMax),
                FighterMaxRange = NormalizeFighterMaxRange(request.FighterMaxRange, iconKey, request.ClassName),
                FighterStatus = NormalizeFighterStatus(request.FighterStatus, iconKey, request.ClassName),
                HomeCarrierShipId = ValidateCarrierId(match, request.HomeCarrierShipId),
                PointsValue = ClampPoints(request.PointsValue),
                FireControlMax = ClampFireControl(request.FireControlMax),
            });
            match.AddLog("Setup", match.Phase.ToString(), $"{DescribeShip(match, match.Ships[^1])} added to {fleet.Name}.");
            match.Touch("ShipCreated");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto UpdateShipProfile(Guid shipId, UpdateShipProfileRequest request)
    {
        lock (_gate)
        {
            var match = _matches.Values.SingleOrDefault(m => m.Ships.Any(s => s.Id == shipId))
                ?? throw new InvalidOperationException("Ship was not found.");
            var participant = FindParticipant(match, request.ParticipantToken);
            var ship = FindOwnedShip(match, participant.Id, shipId);

            var shipState = new ShipMovementState(request.CurrentVelocity, request.CurrentCourse);
            var validation = _rules.Validate(shipState, request.ThrustRating, new MovementOrder(0, 0, TurnDirection.None));
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(" ", validation.Errors));
            }

            ship.Name = string.IsNullOrWhiteSpace(request.Name) ? ship.Name : request.Name.Trim();
            ship.ClassName = string.IsNullOrWhiteSpace(request.ClassName) ? null : request.ClassName.Trim();
            ship.ThrustRating = Math.Clamp(request.ThrustRating, 0, 20);
            ship.CurrentVelocity = request.CurrentVelocity;
            ship.CurrentCourse = request.CurrentCourse;
            ship.HullMax = Math.Clamp(request.HullMax, 1, 80);
            ship.ArmorMax = Math.Clamp(request.ArmorMax, 0, 40);
            ship.PositionX = ClampPosition(request.PositionX, match.TableWidth);
            ship.PositionY = ClampPosition(request.PositionY, match.TableDepth);
            ship.ScreenRating = Math.Clamp(request.ScreenRating, 0, 3);
            ship.IconKey = NormalizeIconKey(request.IconKey, request.ClassName);
            ship.Weapons.Clear();
            ship.Weapons.AddRange(NormalizeWeapons(request.Weapons));
            ship.FighterEnduranceMax = NormalizeFighterEnduranceMax(request.FighterEnduranceMax, ship.IconKey, ship.ClassName);
            ship.FighterEnduranceUsed = NormalizeFighterEnduranceUsed(request.FighterEnduranceUsed, ship.FighterEnduranceMax);
            ship.FighterMaxRange = NormalizeFighterMaxRange(request.FighterMaxRange, ship.IconKey, ship.ClassName);
            ship.FighterStatus = NormalizeFighterStatus(request.FighterStatus, ship.IconKey, ship.ClassName);
            ship.HomeCarrierShipId = ValidateCarrierId(match, request.HomeCarrierShipId);
            ship.PointsValue = ClampPoints(request.PointsValue);
            ship.FireControlMax = ClampFireControl(request.FireControlMax);
            ship.FireControlDamage = ClampDamage(ship.FireControlDamage, ship.FireControlMax);
            ship.HullDamage = ClampDamage(ship.HullDamage, ship.HullMax);
            ship.ArmorDamage = ClampDamage(ship.ArmorDamage, ship.ArmorMax);
            ship.DriveDamage = ClampDamage(ship.DriveDamage, ship.ThrustRating);
            match.AddLog("Admin", match.Phase.ToString(), $"{DescribeShip(match, ship)} profile updated.");
            match.Touch("ShipProfileUpdated");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto DuplicateShip(Guid shipId, DuplicateShipRequest request)
    {
        lock (_gate)
        {
            var match = _matches.Values.SingleOrDefault(m => m.Ships.Any(s => s.Id == shipId))
                ?? throw new InvalidOperationException("Ship was not found.");
            var participant = FindParticipant(match, request.ParticipantToken);
            var source = FindOwnedShip(match, participant.Id, shipId);
            var copyName = string.IsNullOrWhiteSpace(request.Name)
                ? NextCopyName(source.Name, match.Ships.Where(s => s.FleetId == source.FleetId).Select(s => s.Name))
                : request.Name.Trim();

            match.Ships.Add(new ShipState(
                Guid.NewGuid(),
                source.FleetId,
                copyName,
                source.ClassName,
                source.ThrustRating,
                source.CurrentVelocity,
                source.CurrentCourse,
                source.HullMax,
                source.ArmorMax,
                source.PositionX,
                source.PositionY,
                source.ScreenRating,
                source.Weapons.Select(w => new WeaponMountState(Guid.NewGuid(), w.Name, w.AttackDice, w.MaxRange, w.Arcs, w.AmmoMax, w.AmmoUsed, w.ReloadTurns) { IsDestroyed = w.IsDestroyed }).ToList(),
                source.IconKey)
            {
                FighterEnduranceMax = source.FighterEnduranceMax,
                FighterEnduranceUsed = source.FighterEnduranceUsed,
                FighterMaxRange = source.FighterMaxRange,
                FighterStatus = source.FighterStatus,
                HomeCarrierShipId = source.HomeCarrierShipId,
                PointsValue = source.PointsValue,
            });
            match.AddLog("Setup", match.Phase.ToString(), $"{DescribeShip(match, source)} duplicated as {copyName}.");
            match.Touch("ShipDuplicated");
            return ToSnapshot(match);
        }
    }


    public MatchSnapshotDto UpdateShipDamage(Guid shipId, UpdateShipDamageRequest request)
    {
        lock (_gate)
        {
            var match = _matches.Values.SingleOrDefault(m => m.Ships.Any(s => s.Id == shipId))
                ?? throw new InvalidOperationException("Ship was not found.");
            var participant = FindParticipant(match, request.ParticipantToken);
            var ship = FindOwnedShip(match, participant.Id, shipId);

            var before = CaptureDamage(ship);
            ship.HullDamage = ClampDamage(request.HullDamage, ship.HullMax);
            ship.ArmorDamage = ClampDamage(request.ArmorDamage, ship.ArmorMax);
            ship.FireControlDamage = ClampDamage(request.FireControlDamage, ship.FireControlMax);
            ship.DriveDamage = ClampDamage(request.DriveDamage, ship.ThrustRating);
            ship.WeaponDamage = ClampDamage(request.WeaponDamage, 12);
            match.AddLog("Damage", match.Phase.ToString(), $"{DescribeShip(match, ship)} damage record updated: {DescribeDamageDelta(before, CaptureDamage(ship))}.");
            match.Touch("ShipDamageUpdated");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto UpdateFighterOperations(Guid shipId, UpdateFighterOperationsRequest request)
    {
        lock (_gate)
        {
            var match = _matches.Values.SingleOrDefault(m => m.Ships.Any(s => s.Id == shipId))
                ?? throw new InvalidOperationException("Ship was not found.");
            var participant = FindParticipant(match, request.ParticipantToken);
            var ship = FindOwnedShip(match, participant.Id, shipId);
            if (!IsFighterGroup(ship.IconKey, ship.ClassName))
            {
                throw new InvalidOperationException("Fighter operations can only be tracked on fighter groups.");
            }

            ship.FighterEnduranceMax = NormalizeFighterEnduranceMax(request.FighterEnduranceMax, ship.IconKey, ship.ClassName);
            ship.FighterEnduranceUsed = NormalizeFighterEnduranceUsed(request.FighterEnduranceUsed, ship.FighterEnduranceMax);
            ship.FighterMaxRange = NormalizeFighterMaxRange(request.FighterMaxRange, ship.IconKey, ship.ClassName);
            ship.FighterStatus = NormalizeFighterStatus(request.FighterStatus, ship.IconKey, ship.ClassName);
            ship.HomeCarrierShipId = ValidateCarrierId(match, request.HomeCarrierShipId);

            var homeCarrier = ship.HomeCarrierShipId is Guid carrierId
                ? match.Ships.SingleOrDefault(s => s.Id == carrierId)
                : null;
            var homeNote = homeCarrier is null ? "no home carrier assigned" : $"home carrier {homeCarrier.Name}";
            match.AddLog("Fighters", match.Phase.ToString(), $"{DescribeShip(match, ship)} fighter ops: {ship.FighterStatus}, endurance {ship.FighterEnduranceUsed}/{ship.FighterEnduranceMax}, range {ship.FighterMaxRange}, {homeNote}.");
            match.Touch("FighterOperationsUpdated");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto CreateOrdnanceMarker(Guid matchId, CreateOrdnanceMarkerRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var source = request.SourceShipId is Guid sourceId
                ? FindOwnedShip(match, participant.Id, sourceId)
                : null;
            var target = request.TargetShipId is Guid targetId
                ? match.Ships.SingleOrDefault(s => s.Id == targetId) ?? throw new InvalidOperationException("Target ship was not found.")
                : null;

            var marker = new OrdnanceMarkerState(
                Guid.NewGuid(),
                participant.Id,
                NormalizeOrdnanceText(request.Name, "Salvo"),
                NormalizeOrdnanceText(request.MarkerType, "Missile"),
                source?.Id,
                target?.Id,
                ClampPosition(request.PositionX, match.TableWidth),
                ClampPosition(request.PositionY, match.TableDepth),
                NormalizeCourse(request.Course),
                Math.Clamp(request.Speed, 0, 72),
                Math.Clamp(request.EnduranceRemaining, 0, 24),
                Math.Clamp(request.AttackDice, 0, 24),
                Math.Clamp(request.MaxRange, 0, 120),
                NormalizeOrdnanceStatus(request.Status));
            match.OrdnanceMarkers.Add(marker);
            var targetNote = target is null ? "no target" : $"targeting {target.Name}";
            match.AddLog("Ordnance", match.Phase.ToString(), $"{marker.Name} {marker.MarkerType} marker launched at {marker.PositionX:0.#},{marker.PositionY:0.#}, C{marker.Course}/V{marker.Speed}, {targetNote}.");
            match.Touch("OrdnanceMarkerCreated");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto UpdateOrdnanceMarker(Guid markerId, UpdateOrdnanceMarkerRequest request)
    {
        lock (_gate)
        {
            var match = _matches.Values.SingleOrDefault(m => m.OrdnanceMarkers.Any(o => o.Id == markerId))
                ?? throw new InvalidOperationException("Ordnance marker was not found.");
            var participant = FindParticipant(match, request.ParticipantToken);
            var marker = FindOwnedOrdnanceMarker(match, participant.Id, markerId);
            var target = request.TargetShipId is Guid targetId
                ? match.Ships.SingleOrDefault(s => s.Id == targetId) ?? throw new InvalidOperationException("Target ship was not found.")
                : null;

            marker.Name = NormalizeOrdnanceText(request.Name, marker.Name);
            marker.MarkerType = NormalizeOrdnanceText(request.MarkerType, marker.MarkerType);
            marker.TargetShipId = target?.Id;
            marker.PositionX = ClampPosition(request.PositionX, match.TableWidth);
            marker.PositionY = ClampPosition(request.PositionY, match.TableDepth);
            marker.Course = NormalizeCourse(request.Course);
            marker.Speed = Math.Clamp(request.Speed, 0, 72);
            marker.EnduranceRemaining = Math.Clamp(request.EnduranceRemaining, 0, 24);
            marker.AttackDice = Math.Clamp(request.AttackDice, 0, 24);
            marker.MaxRange = Math.Clamp(request.MaxRange, 0, 120);
            marker.Status = NormalizeOrdnanceStatus(request.Status);
            match.AddLog("Ordnance", match.Phase.ToString(), $"{marker.Name} marker updated: {marker.Status}, endurance {marker.EnduranceRemaining}, position {marker.PositionX:0.#},{marker.PositionY:0.#}.");
            match.Touch("OrdnanceMarkerUpdated");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto RemoveOrdnanceMarker(Guid markerId, RemoveOrdnanceMarkerRequest request)
    {
        lock (_gate)
        {
            var match = _matches.Values.SingleOrDefault(m => m.OrdnanceMarkers.Any(o => o.Id == markerId))
                ?? throw new InvalidOperationException("Ordnance marker was not found.");
            var participant = FindParticipant(match, request.ParticipantToken);
            var marker = FindOwnedOrdnanceMarker(match, participant.Id, markerId);
            match.OrdnanceMarkers.Remove(marker);
            match.AddLog("Ordnance", match.Phase.ToString(), $"{marker.Name} marker removed from the table.");
            match.Touch("OrdnanceMarkerRemoved");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto DeclareOrdersComplete(Guid matchId, DeclareOrdersCompleteRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            if (match.Phase is not (MatchPhase.OrderEntry or MatchPhase.OrdersLocked))
            {
                throw new InvalidOperationException("Plotting can only be closed out during order entry.");
            }

            participant.OrdersComplete = true;
            var drifting = DriftingShipIds(match, participant.Id).Count;
            match.AddLog(
                "Orders",
                match.Phase.ToString(),
                drifting == 0
                    ? $"{participant.DisplayName} finished plotting."
                    : $"{participant.DisplayName} finished plotting. {drifting} ship{(drifting == 1 ? " holds" : "s hold")} course and speed.");
            AdvanceOrderEntryPhase(match);
            match.Touch("OrdersDeclaredComplete");
            return ToSnapshot(match);
        }
    }

    /// <summary>
    /// Live ships belonging to a participant with no order written this turn. They hold their
    /// course and speed rather than sitting still.
    /// </summary>
    private static List<Guid> DriftingShipIds(MatchState match, Guid? ownerParticipantId = null) =>
    [
        .. LiveShipIds(match).Where(id =>
        {
            if (match.Commitments.ContainsKey(id))
            {
                return false;
            }

            if (ownerParticipantId is not { } owner)
            {
                return true;
            }

            var ship = match.Ships.Single(s => s.Id == id);
            return match.Fleets.Single(f => f.Id == ship.FleetId).OwnerParticipantId == owner;
        })
    ];

    /// <summary>
    /// Moves order entry along once everyone with ships has finished plotting. With nothing locked
    /// there is nothing to reveal, so the turn goes straight to movement.
    /// </summary>
    private static void AdvanceOrderEntryPhase(MatchState match)
    {
        var plotters = match.Participants
            .Where(p => p.IsClaimed && match.Fleets.Any(f => f.OwnerParticipantId == p.Id
                && match.Ships.Any(s => s.FleetId == f.Id && !IsDestroyed(s))))
            .ToArray();
        if (plotters.Length == 0 || !plotters.All(p => p.OrdersComplete))
        {
            return;
        }

        if (match.Commitments.Count == 0)
        {
            match.Phase = MatchPhase.Movement;
            match.AddLog("Phase", match.Phase.ToString(), "No orders were written; every ship holds course and speed.");
            return;
        }

        match.Phase = MatchPhase.OrdersLocked;
        match.AddLog("Phase", match.Phase.ToString(), "All movement orders locked.");
    }

    public MatchSnapshotDto CommitOrder(Guid matchId, CommitOrderRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var ship = FindOwnedShip(match, participant.Id, request.ShipId);
            // A ship whose reveal failed verification must be able to re-lock during the reveal
            // phase, otherwise the mismatch deadlocks the turn.
            var isFailedRevealRepair = match.Phase == MatchPhase.Reveal
                && match.Commitments.TryGetValue(ship.Id, out var priorCommitment)
                && priorCommitment.VerificationFailed == true;
            if (match.Phase is not (MatchPhase.OrderEntry or MatchPhase.OrdersLocked) && !isFailedRevealRepair)
            {
                throw new InvalidOperationException("Movement orders can only be locked during order entry.");
            }

            if (IsDestroyed(ship))
            {
                throw new InvalidOperationException($"{ship.Name} is destroyed and cannot receive movement orders.");
            }

            var validation = _rules.Validate(new ShipMovementState(ship.CurrentVelocity, ship.CurrentCourse), UsableThrust(ship), request.Order);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(" ", validation.Errors));
            }

            var normalized = _rules.Normalize(request.Order);
            var commitment = _commitments.CreateHash(normalized, request.Salt);
            var isRelock = match.Commitments.ContainsKey(ship.Id);
            match.Commitments[ship.Id] = new OrderCommitmentState(ship.Id, participant.Id, commitment, false, null, null, null);
            // Order contents stay hidden until reveal, so only the lock event itself is logged.
            match.AddLog("Orders", match.Phase.ToString(), $"{DescribeShip(match, ship)} {(isRelock ? "re-locked" : "locked")} a hidden movement order.");
            var liveShipIds = LiveShipIds(match);
            if (liveShipIds.Count > 0 && liveShipIds.All(match.Commitments.ContainsKey))
            {
                match.Phase = MatchPhase.OrdersLocked;
                match.AddLog("Phase", match.Phase.ToString(), "All movement orders locked.");
            }
            else
            {
                AdvanceOrderEntryPhase(match);
            }

            match.Touch("OrderCommitted");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto RevealOrder(Guid matchId, RevealOrderRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            if (match.Phase is not (MatchPhase.OrdersLocked or MatchPhase.Reveal))
            {
                throw new InvalidOperationException("Orders can only be revealed after every live ship has locked its order.");
            }

            var ship = FindOwnedShip(match, participant.Id, request.ShipId);
            if (!match.Commitments.TryGetValue(ship.Id, out var commitment))
            {
                throw new InvalidOperationException("Ship has not been locked.");
            }

            var normalized = _rules.Normalize(request.Order);
            var isValid = _commitments.Verify(commitment.CommitmentHash, normalized, request.Salt);
            var revealed = commitment with
            {
                IsRevealed = isValid,
                VerificationFailed = !isValid,
                RevealedOrder = isValid ? request.Order : null,
                Result = isValid ? _rules.Resolve(new ShipMovementState(ship.CurrentVelocity, ship.CurrentCourse), request.Order) : null
            };
            match.Commitments[ship.Id] = revealed;
            match.AddLog(
                "Orders",
                match.Phase.ToString(),
                isValid
                    ? $"{DescribeShip(match, ship)} revealed dV {request.Order.VelocityDelta}, helm {DescribeTurnSequence(request.Order)}."
                    : $"{DescribeShip(match, ship)} reveal did not match its locked order. Re-lock and reveal again before movement.");

            // Only locked orders need revealing: a ship without one is holding course, and there is
            // nothing hidden about that.
            if (match.Commitments.Values.All(c => c.IsRevealed))
            {
                match.Phase = MatchPhase.Movement;
                match.AddLog("Phase", match.Phase.ToString(), "All movement orders revealed.");
            }
            else if (match.Commitments.Values.Any(c => c.IsRevealed))
            {
                match.Phase = MatchPhase.Reveal;
            }

            match.Touch(isValid ? "OrderRevealVerified" : "OrderRevealFailed");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto FireWeapon(Guid matchId, FireWeaponRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            if (match.Phase != MatchPhase.Firing)
            {
                throw new InvalidOperationException("Weapons may only fire during the firing phase.");
            }

            var attacker = FindOwnedShip(match, participant.Id, request.AttackerShipId);
            var target = match.Ships.SingleOrDefault(s => s.Id == request.TargetShipId)
                ?? throw new InvalidOperationException("Target ship was not found.");
            if (attacker.Id == target.Id)
            {
                throw new InvalidOperationException("A ship cannot fire on itself.");
            }

            if (IsDestroyed(attacker))
            {
                throw new InvalidOperationException($"{attacker.Name} is destroyed and cannot fire.");
            }

            if (IsDestroyed(target))
            {
                throw new InvalidOperationException($"{target.Name} is already destroyed.");
            }

            var weapon = attacker.Weapons.SingleOrDefault(w => w.Id == request.WeaponId)
                ?? throw new InvalidOperationException("Weapon mount was not found.");
            if (weapon.IsDestroyed)
            {
                throw new InvalidOperationException($"{weapon.Name} was knocked out by a threshold check.");
            }

            if (weapon.AmmoMax > 0 && weapon.AmmoUsed >= weapon.AmmoMax)
            {
                throw new InvalidOperationException($"{weapon.Name} has no ammunition remaining.");
            }

            if (match.FiringResults.Any(f => f.TurnNumber == match.TurnNumber && f.AttackerShipId == attacker.Id && f.WeaponId == weapon.Id))
            {
                throw new InvalidOperationException($"{weapon.Name} has already fired this turn.");
            }

            // Fire control directs the guns: with none left a ship cannot shoot at all, and each
            // working system holds exactly one target ship for the turn.
            var workingFireControl = Math.Max(0, attacker.FireControlMax - attacker.FireControlDamage);
            if (workingFireControl == 0)
            {
                throw new InvalidOperationException($"{attacker.Name} has no working fire control and cannot fire.");
            }

            var engagedTargetIds = match.FiringResults
                .Where(f => f.TurnNumber == match.TurnNumber && f.AttackerShipId == attacker.Id)
                .Select(f => f.TargetShipId)
                .Distinct()
                .ToArray();
            if (!engagedTargetIds.Contains(target.Id) && engagedTargetIds.Length >= workingFireControl)
            {
                var engagedNames = string.Join(", ", engagedTargetIds
                    .Select(id => match.Ships.SingleOrDefault(s => s.Id == id)?.Name ?? "an unknown ship"));
                throw new InvalidOperationException(
                    $"{attacker.Name} has {workingFireControl} working fire control system{(workingFireControl == 1 ? string.Empty : "s")} and is already engaging {engagedNames}. Fire the rest of its weapons at {(workingFireControl == 1 ? "that target" : "those targets")}.");
            }

            // Which arc the target sits in is geometry, not a choice: it follows from the firing
            // ship's course and where the two ships are on the table.
            var targetArc = BearingToTarget(attacker, target);
            if (request.Arc is { } declaredArc && declaredArc != targetArc)
            {
                throw new InvalidOperationException(
                    $"{target.Name} bears {FiringArcs.Describe(targetArc)} of {attacker.Name}, not {FiringArcs.Describe(declaredArc)}. Fix the ship positions if the table disagrees.");
            }

            var solution = new FiringSolution(
                new WeaponAttackProfile(weapon.Name, weapon.AttackDice, weapon.MaxRange, weapon.Arcs),
                request.Range,
                target.ScreenRating,
                attacker.WeaponDamage,
                targetArc);
            var validation = _firingRules.Validate(solution);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(" ", validation.Errors));
            }

            // A threshold check covers everything one ship fires, so the volley opens here and the
            // rows it completes are measured from the hull as it stood before this ship started.
            if (match.FiringShipId is { } previousAttacker && previousAttacker != attacker.Id)
            {
                ResolvePendingThresholds(match);
            }

            match.FiringShipId = attacker.Id;
            var result = _firingRules.Resolve(solution);
            var remainingDamage = result.Damage;
            var damageBefore = CaptureDamage(target);
            if (!match.PendingThresholds.ContainsKey(target.Id))
            {
                match.PendingThresholds[target.Id] = target.HullDamage;
            }

            var wasDestroyed = target.HullDamage >= target.HullMax;
            var armorBefore = target.ArmorDamage;
            target.ArmorDamage = ClampDamage(target.ArmorDamage + remainingDamage, target.ArmorMax);
            var armorApplied = target.ArmorDamage - armorBefore;
            remainingDamage -= armorApplied;
            var hullBefore = target.HullDamage;
            target.HullDamage = ClampDamage(target.HullDamage + remainingDamage, target.HullMax);
            var hullApplied = target.HullDamage - hullBefore;

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
                result.DiceRolls);
            match.FiringResults.Add(firingResult);
            if (weapon.AmmoMax > 0)
            {
                weapon.AmmoUsed = Math.Clamp(weapon.AmmoUsed + 1, 0, weapon.AmmoMax);
            }

            var destroyedNote = !wasDestroyed && target.HullDamage >= target.HullMax ? " Target destroyed." : string.Empty;
            var ammoNote = weapon.AmmoMax > 0 ? $" Ammo {weapon.AmmoUsed}/{weapon.AmmoMax}." : string.Empty;
            var rollNote = result.DiceRolls.Count == 0
                ? "no dice left to roll"
                : $"rolled {string.Join(",", result.DiceRolls)}";
            var screenNote = target.ScreenRating switch
            {
                > 0 when result.ScreenReduction > 0 => $" vs screens {target.ScreenRating} (-{result.ScreenReduction})",
                > 0 => $" vs screens {target.ScreenRating}",
                _ => string.Empty,
            };
            match.AddLog(
                "Fire",
                match.Phase.ToString(),
                $"{DescribeShip(match, attacker)} fired {weapon.Name} at {DescribeShip(match, target)} through {FiringArcs.Describe(targetArc)} arc at range {request.Range} ({firingResult.RangeBand}): {rollNote}{screenNote} for {result.Damage} damage ({armorApplied} armor, {hullApplied} hull). Target delta: {DescribeDamageDelta(damageBefore, CaptureDamage(target))}.{destroyedNote}{ammoNote}");
            match.Touch("WeaponFired");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto AdvanceTurn(Guid matchId, string participantToken)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, participantToken);
            if (participant.Role != "Owner")
            {
                throw new UnauthorizedAccessException("Only the owner can advance the turn in this MVP.");
            }

            if (match.Phase == MatchPhase.Firing)
            {
                ResolvePendingThresholds(match);
                match.TurnNumber++;
                match.Phase = MatchPhase.OrderEntry;
                match.Commitments.Clear();
                match.FiringResults.Clear();
                foreach (var plotter in match.Participants)
                {
                    plotter.OrdersComplete = false;
                }
                AdvanceOrdnanceMarkers(match);
                AddFleetSummary(match, "End of firing phase");
                AddShipStateSnapshot(match, "End of turn");
                match.AddLog("Phase", match.Phase.ToString(), $"Turn {match.TurnNumber} order entry opened.");
                AddShipStateSnapshot(match, "Start of order entry");
                match.Touch("TurnAdvanced");
                return ToSnapshot(match);
            }

            if (match.Phase != MatchPhase.Movement || match.Commitments.Values.Any(c => !c.IsRevealed || c.Result is null))
            {
                throw new InvalidOperationException("All valid orders must be revealed before advancing.");
            }

            foreach (var commitment in match.Commitments.Values)
            {
                var ship = match.Ships.Single(s => s.Id == commitment.ShipId);
                if (IsDestroyed(ship))
                {
                    match.AddLog("Movement", match.Phase.ToString(), $"{DescribeShip(match, ship)} was destroyed before movement resolved; its order was discarded.");
                    continue;
                }

                var startingVelocity = ship.CurrentVelocity;
                var startingCourse = ship.CurrentCourse;
                var startingX = ship.PositionX;
                var startingY = ship.PositionY;
                ship.CurrentVelocity = commitment.Result!.EndingVelocity;
                ship.CurrentCourse = commitment.Result.EndingCourse;
                (ship.PositionX, ship.PositionY) = EstimatePositionFromResult(ship.PositionX, ship.PositionY, commitment.Result, match.TableWidth, match.TableDepth);
                var order = commitment.RevealedOrder!;
                match.AddLog("Movement", match.Phase.ToString(), $"{DescribeShip(match, ship)} plotted dV {order.VelocityDelta}, helm {DescribeTurnSequence(order)}; moved v{startingVelocity}/c{startingCourse} to v{ship.CurrentVelocity}/c{ship.CurrentCourse}, position {startingX:0.#},{startingY:0.#} to {ship.PositionX:0.#},{ship.PositionY:0.#}.{PositionEdgeNote(ship, match)}");
            }

            // A ship with no order written keeps the course and speed it already had, and still
            // travels its full velocity.
            foreach (var shipId in DriftingShipIds(match))
            {
                var ship = match.Ships.Single(s => s.Id == shipId);
                var startingX = ship.PositionX;
                var startingY = ship.PositionY;
                (ship.PositionX, ship.PositionY) = EstimatePosition(ship.PositionX, ship.PositionY, ship.CurrentVelocity, ship.CurrentCourse, match.TableWidth, match.TableDepth);
                match.AddLog(
                    "Movement",
                    match.Phase.ToString(),
                    $"{DescribeShip(match, ship)} had no order and held course: v{ship.CurrentVelocity}/c{ship.CurrentCourse}, position {startingX:0.#},{startingY:0.#} to {ship.PositionX:0.#},{ship.PositionY:0.#}.{PositionEdgeNote(ship, match)}");
            }

            match.Phase = MatchPhase.Firing;
            match.AddLog("Phase", match.Phase.ToString(), $"Turn {match.TurnNumber} firing phase opened.");
            AddShipStateSnapshot(match, "Start of firing phase");
            AddNoFireTelemetry(match);
            match.Touch("TurnAdvanced");
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
            if (match.FiringShipId != ship.Id)
            {
                // Nothing owed: either the ship never fired, or its checks already rolled.
                return ToSnapshot(match);
            }

            match.AddLog("Fire", match.Phase.ToString(), $"{DescribeShip(match, ship)} completed its fire for the turn.");
            ResolvePendingThresholds(match);
            match.Touch("FireCompleted");
            return ToSnapshot(match);
        }
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

    private MatchState FindMatch(Guid matchId) =>
        _matches.TryGetValue(matchId, out var match) ? match : throw new InvalidOperationException("Match was not found.");

    private static ParticipantState FindParticipant(MatchState match, string token) =>
        string.IsNullOrWhiteSpace(token)
            ? throw new UnauthorizedAccessException("Participant token is invalid.")
            : match.Participants.SingleOrDefault(p => p.IsClaimed && p.Token == token)
                ?? throw new UnauthorizedAccessException("Participant token is invalid.");

    private static ShipState FindOwnedShip(MatchState match, Guid participantId, Guid shipId)
    {
        var ship = match.Ships.SingleOrDefault(s => s.Id == shipId) ?? throw new InvalidOperationException("Ship was not found.");
        var fleet = match.Fleets.Single(f => f.Id == ship.FleetId);
        if (fleet.OwnerParticipantId != participantId)
        {
            throw new UnauthorizedAccessException("You can only issue orders for your own ships.");
        }

        return ship;
    }

    private static OrdnanceMarkerState FindOwnedOrdnanceMarker(MatchState match, Guid participantId, Guid markerId)
    {
        var marker = match.OrdnanceMarkers.SingleOrDefault(o => o.Id == markerId)
            ?? throw new InvalidOperationException("Ordnance marker was not found.");
        if (marker.OwnerParticipantId != participantId)
        {
            throw new UnauthorizedAccessException("You can only update your own ordnance markers.");
        }

        return marker;
    }

    private string CreateJoinCode()
    {
        string[] words = ["BLUE", "COMET", "SEVEN", "IRON", "ORBIT", "NOVA", "VECTOR", "LANCE", "DRIFT", "EMBER"];
        string code;
        do
        {
            code = string.Join("-", Random.Shared.GetItems(words, 3));
        } while (_joinCodes.ContainsKey(code));

        return code;
    }

    private static MatchSnapshotDto ToSnapshot(MatchState match) => new(
        match.Id,
        match.JoinCode,
        match.Name,
        match.Phase.ToString(),
        match.TurnNumber,
        FullThrustLightCinematicRules.ProfileKey,
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
            s.DriveDamage,
            s.WeaponDamage,
            s.ScreenRating,
            s.Weapons.Select(w => new WeaponMountDto(w.Id, w.Name, w.AttackDice, w.MaxRange, w.Arcs, w.AmmoMax, w.AmmoUsed, w.ReloadTurns, w.IsDestroyed)).ToArray(),
            s.HullDamage >= s.HullMax,
            FullThrustLightThresholdRules.HullRowsFor(s.HullMax),
            FullThrustLightThresholdRules.RowsCompletedFor(s.HullDamage, s.HullMax),
            s.IconKey,
            s.FighterEnduranceMax,
            s.FighterEnduranceUsed,
            s.FighterMaxRange,
            s.FighterStatus,
            s.HomeCarrierShipId,
            s.PointsValue)).ToArray(),
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
            f.DiceRolls)).ToArray(),
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
        match.Version,
        match.PointsLimit);

    private sealed class MatchState(Guid id, string joinCode, string name, ParticipantState owner)
    {
        public Guid Id { get; } = id;
        public string JoinCode { get; } = joinCode;
        public string Name { get; } = name;
        public MatchPhase Phase { get; set; } = MatchPhase.FleetSetup;

        /// <summary>The ship part-way through its fire, if any. Its threshold checks are still owed.</summary>
        public Guid? FiringShipId { get; set; }

        /// <summary>Hull damage each target had before the firing ship opened up, by target id.</summary>
        public Dictionary<Guid, int> PendingThresholds { get; } = [];
        public int TurnNumber { get; set; } = 1;
        public int TableWidth { get; set; } = 72;
        public int TableDepth { get; set; } = 48;
        public int PointsLimit { get; set; }
        public List<ParticipantState> Participants { get; } = [owner];
        public List<FleetState> Fleets { get; } = [];
        public List<ShipState> Ships { get; } = [];
        public List<OrdnanceMarkerState> OrdnanceMarkers { get; } = [];
        public Dictionary<Guid, OrderCommitmentState> Commitments { get; } = [];
        public List<FiringResultState> FiringResults { get; } = [];
        public List<MatchLogEntryState> MatchLog { get; } = [];
        public long Version { get; private set; } = 1;
        public void Touch(string _) => Version++;
        public void AddLog(string category, string phase, string message) => MatchLog.Add(new MatchLogEntryState(MatchLog.Count + 1, DateTimeOffset.UtcNow, TurnNumber, phase, category, message));
        public void AddRestoredLog(MatchLogEntryState entry) => MatchLog.Add(entry);
    }

    private sealed class ParticipantState
    {
        public Guid Id { get; init; }
        public required string Token { get; set; }
        public required string DisplayName { get; init; }
        public required string Role { get; init; }
        public bool IsReady { get; set; }
        // False until a realtime hub connection joins the match group for this token.
        public bool IsConnected { get; set; }

        /// <summary>True once this participant says its plotting is done for the turn.</summary>
        public bool OrdersComplete { get; set; }

        /// <summary>A restored seat holds no token until a device claims it.</summary>
        public bool IsClaimed => !string.IsNullOrWhiteSpace(Token);

        public static ParticipantState Create(string displayName, string role) => new()
        {
            Id = Guid.NewGuid(),
            Token = _commitmentSafeToken(),
            DisplayName = displayName,
            Role = role
        };

        public static ParticipantState CreateSeat(Guid id, string displayName, string role, bool isReady) => new()
        {
            Id = id,
            Token = string.Empty,
            DisplayName = displayName,
            Role = role,
            IsReady = isReady
        };

        public string Claim() => Token = _commitmentSafeToken();

        private static string _commitmentSafeToken() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    private sealed record FleetState(Guid Id, Guid OwnerParticipantId, string Name, string? Faction, string FleetColor);

    private sealed class ShipState(Guid id, Guid fleetId, string name, string? className, int thrustRating, int currentVelocity, int currentCourse, int hullMax, int armorMax, decimal positionX, decimal positionY, int screenRating, IReadOnlyList<WeaponMountState> weapons, string iconKey)
    {
        public Guid Id { get; } = id;
        public Guid FleetId { get; } = fleetId;
        public string Name { get; set; } = name;
        public string? ClassName { get; set; } = className;
        public int ThrustRating { get; set; } = thrustRating;
        public int CurrentVelocity { get; set; } = currentVelocity;
        public int CurrentCourse { get; set; } = currentCourse;
        public decimal PositionX { get; set; } = positionX;
        public decimal PositionY { get; set; } = positionY;
        public int HullMax { get; set; } = hullMax;
        public int HullDamage { get; set; }
        public int ArmorMax { get; set; } = armorMax;
        public int ArmorDamage { get; set; }
        public int FireControlMax { get; set; } = 1;
        public int FireControlDamage { get; set; }
        public int DriveDamage { get; set; }
        public int WeaponDamage { get; set; }
        public int ScreenRating { get; set; } = screenRating;
        public List<WeaponMountState> Weapons { get; } = [.. weapons];
        public string IconKey { get; set; } = iconKey;
        public int FighterEnduranceMax { get; set; }
        public int FighterEnduranceUsed { get; set; }
        public int FighterMaxRange { get; set; }
        public string FighterStatus { get; set; } = "Docked";
        public Guid? HomeCarrierShipId { get; set; }
        public int PointsValue { get; set; }
    }

    private sealed class WeaponMountState(Guid id, string name, int attackDice, int maxRange, IReadOnlyList<FiringArc> arcs, int ammoMax, int ammoUsed, int reloadTurns)
    {
        public Guid Id { get; } = id;
        public string Name { get; } = name;
        public int AttackDice { get; } = attackDice;
        public int MaxRange { get; } = maxRange;
        public IReadOnlyList<FiringArc> Arcs { get; } = arcs;
        public bool IsDestroyed { get; set; }
        public int AmmoMax { get; } = ammoMax;
        public int AmmoUsed { get; set; } = ammoUsed;
        public int ReloadTurns { get; } = reloadTurns;
    }

    private sealed class OrdnanceMarkerState(Guid id, Guid ownerParticipantId, string name, string markerType, Guid? sourceShipId, Guid? targetShipId, decimal positionX, decimal positionY, int course, int speed, int enduranceRemaining, int attackDice, int maxRange, string status)
    {
        public Guid Id { get; } = id;
        public Guid OwnerParticipantId { get; } = ownerParticipantId;
        public string Name { get; set; } = name;
        public string MarkerType { get; set; } = markerType;
        public Guid? SourceShipId { get; } = sourceShipId;
        public Guid? TargetShipId { get; set; } = targetShipId;
        public decimal PositionX { get; set; } = positionX;
        public decimal PositionY { get; set; } = positionY;
        public int Course { get; set; } = course;
        public int Speed { get; set; } = speed;
        public int EnduranceRemaining { get; set; } = enduranceRemaining;
        public int AttackDice { get; set; } = attackDice;
        public int MaxRange { get; set; } = maxRange;
        public string Status { get; set; } = status;
    }

    private sealed record FiringResultState(
        Guid AttackerShipId,
        Guid TargetShipId,
        Guid WeaponId,
        string WeaponName,
        int TurnNumber,
        int Range,
        string RangeBand,
        FiringArc Arc,
        int RawDice,
        int RangePenalty,
        int ScreenReduction,
        int SystemPenalty,
        int Damage,
        int ArmorDamageApplied,
        int HullDamageApplied,
        IReadOnlyList<int> DiceRolls);

    private sealed record MatchLogEntryState(long Sequence, DateTimeOffset Timestamp, int TurnNumber, string Phase, string Category, string Message);

    private static int ClampFireControl(int value) => Math.Clamp(value, 0, 6);

    private static int ClampPoints(int value) => Math.Clamp(value, 0, 99999);

    /// <summary>Points a participant's fleets exceed the match limit by, or zero when inside it.</summary>
    private static int PointsOverLimit(MatchState match, Guid participantId)
    {
        if (match.PointsLimit <= 0)
        {
            return 0;
        }

        var fleetIds = match.Fleets.Where(f => f.OwnerParticipantId == participantId).Select(f => f.Id).ToHashSet();
        var total = match.Ships.Where(s => fleetIds.Contains(s.FleetId)).Sum(s => s.PointsValue);
        return Math.Max(0, total - match.PointsLimit);
    }

    private static bool IsDestroyed(ShipState ship) => ship.HullDamage >= ship.HullMax;

    /// <summary>Thrust actually available for plotting after drive damage.</summary>
    private static int UsableThrust(ShipState ship) => Math.Max(0, ship.ThrustRating - ship.DriveDamage);

    private static List<Guid> LiveShipIds(MatchState match) =>
        match.Ships.Where(s => !IsDestroyed(s)).Select(s => s.Id).ToList();

    private static int ClampDamage(int value, int max) => Math.Clamp(value, 0, max);

    private static decimal ClampPosition(decimal value, int max) => Math.Clamp(value, 0, max);

    private static string NormalizeFleetColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "#47f1ff";
        }

        var color = value.Trim();
        return color.Length == 7
            && color[0] == '#'
            && color.Skip(1).All(Uri.IsHexDigit)
                ? color
                : "#47f1ff";
    }

    private static string NormalizeIconKey(string? iconKey, string? className)
    {
        var normalized = NormalizeIconText(iconKey);
        if (IsKnownIcon(normalized))
        {
            return normalized;
        }

        var classText = NormalizeIconText(className);
        if (classText.Contains("escort", StringComparison.Ordinal))
        {
            return "escort";
        }

        if (classText.Contains("frigate", StringComparison.Ordinal))
        {
            return "frigate";
        }

        if (classText.Contains("destroyer", StringComparison.Ordinal))
        {
            return "destroyer";
        }

        if (classText.Contains("carrier", StringComparison.Ordinal))
        {
            return "carrier";
        }

        if (classText.Contains("dreadnought", StringComparison.Ordinal) || classText.Contains("battleship", StringComparison.Ordinal))
        {
            return "dreadnought";
        }

        if (classText.Contains("fighter", StringComparison.Ordinal))
        {
            return "fighter-group";
        }

        if (classText.Contains("station", StringComparison.Ordinal) || classText.Contains("base", StringComparison.Ordinal))
        {
            return "station";
        }

        return "cruiser";
    }

    private static string NormalizeIconText(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().ToLowerInvariant().Replace(" ", "-").Replace("_", "-");

    private static bool IsKnownIcon(string iconKey) => iconKey is
        "escort" or
        "frigate" or
        "destroyer" or
        "cruiser" or
        "carrier" or
        "dreadnought" or
        "fighter-group" or
        "station";

    private static bool IsFighterGroup(string iconKey, string? className) =>
        iconKey == "fighter-group" || NormalizeIconText(className).Contains("fighter", StringComparison.Ordinal);

    private static int NormalizeFighterEnduranceMax(int value, string iconKey, string? className) =>
        IsFighterGroup(iconKey, className)
            ? Math.Clamp(value <= 0 ? 6 : value, 1, 24)
            : 0;

    private static int NormalizeFighterEnduranceUsed(int value, int max) =>
        Math.Clamp(value, 0, Math.Max(0, max));

    private static int NormalizeFighterMaxRange(int value, string iconKey, string? className) =>
        IsFighterGroup(iconKey, className)
            ? Math.Clamp(value <= 0 ? 24 : value, 1, 120)
            : 0;

    private static string NormalizeFighterStatus(string? value, string iconKey, string? className)
    {
        if (!IsFighterGroup(iconKey, className))
        {
            return "Docked";
        }

        return value?.Trim().ToLowerInvariant() switch
        {
            "airborne" or "launched" or "active" => "Airborne",
            "recovering" or "returning" or "return" => "Recovering",
            _ => "Docked",
        };
    }

    private static Guid? ValidateCarrierId(MatchState match, Guid? carrierId)
    {
        if (carrierId is null)
        {
            return null;
        }

        // Ship ids are per-match, so imported fleet files routinely carry a carrier id from another
        // match. Treat an unknown id as "unassigned" instead of failing the whole import.
        var carrier = match.Ships.SingleOrDefault(s => s.Id == carrierId.Value);
        if (carrier is null)
        {
            return null;
        }

        if (carrier.IconKey != "carrier" && !NormalizeIconText(carrier.ClassName).Contains("carrier", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Home carrier must be a carrier ship.");
        }

        return carrier.Id;
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

        var rowsBefore = _thresholdRules.RowsCompleted(hullBefore, target.HullMax);
        var rowsAfter = _thresholdRules.RowsCompleted(target.HullDamage, target.HullMax);
        if (rowsAfter <= rowsBefore)
        {
            return;
        }

        // One check against the deepest row reached, one point worse per extra row torn through.
        var threshold = Math.Min(rowsAfter, FullThrustLightThresholdRules.DeepestThreshold);
        var extra = rowsAfter - rowsBefore - 1;
        var systems = SurvivingSystems(target);
        var result = _thresholdRules.Resolve(new ThresholdCheck(threshold, extra, systems));

        var losses = new List<string>();
        foreach (var system in result.Lost)
        {
            losses.Add(ApplySystemLoss(target, system));
        }

        var rollNote = result.Rolls.Count == 0
            ? "no systems left to roll"
            : $"rolled {string.Join(",", result.Rolls.Select(roll => roll.Die))}";
        var lossNote = losses.Count == 0 ? "nothing knocked out" : string.Join(", ", losses);
        var rowNote = extra > 0 ? $"threshold {result.Threshold} (+{extra} rows in one attack)" : $"threshold {result.Threshold}";
        match.AddLog(
            "Threshold",
            match.Phase.ToString(),
            $"{DescribeShip(match, target)} completed hull row {rowsAfter} of {_thresholdRules.HullRows(target.HullMax).Count}: {rowNote}, systems lost on {result.LostOn} or less, {rollNote}. {lossNote}.");
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
        for (var level = 0; level < ship.ScreenRating; level++)
        {
            systems.Add(new ShipSystem(ShipSystemKind.Screen, "screen generator"));
        }

        systems.AddRange(ship.Weapons
            .Where(weapon => !weapon.IsDestroyed)
            .Select(weapon => new ShipSystem(ShipSystemKind.Weapon, weapon.Name, weapon.Id)));
        return systems;
    }

    /// <summary>Applies one knocked-out system and describes it for the log.</summary>
    private static string ApplySystemLoss(ShipState ship, ShipSystem system)
    {
        switch (system.Kind)
        {
            case ShipSystemKind.Drive:
                // First hit cuts thrust in half; a second leaves the ship drifting.
                var halved = (ship.ThrustRating + 1) / 2;
                if (ship.DriveDamage < halved)
                {
                    ship.DriveDamage = halved;
                    return $"drives cut to thrust {Math.Max(0, ship.ThrustRating - ship.DriveDamage)}";
                }

                ship.DriveDamage = ship.ThrustRating;
                return "drives knocked out";
            case ShipSystemKind.FireControl:
                ship.FireControlDamage = Math.Min(ship.FireControlMax, ship.FireControlDamage + 1);
                return ship.FireControlDamage >= ship.FireControlMax
                    ? "last fire control lost"
                    : "fire control lost";
            case ShipSystemKind.Screen:
                ship.ScreenRating = Math.Max(0, ship.ScreenRating - 1);
                return ship.ScreenRating == 0 ? "screens down" : $"screens dropped to level {ship.ScreenRating}";
            case ShipSystemKind.Weapon:
                var mount = ship.Weapons.SingleOrDefault(weapon => weapon.Id == system.WeaponId);
                if (mount is null)
                {
                    return system.Name;
                }

                mount.IsDestroyed = true;
                return $"{mount.Name} knocked out";
            default:
                return system.Name;
        }
    }

    /// <summary>The arc the target lies in, relative to the firing ship's nose.</summary>
    private static FiringArc BearingToTarget(ShipState attacker, ShipState target) =>
        FiringArcs.Bearing(
            attacker.CurrentCourse,
            (double)(target.PositionX - attacker.PositionX),
            (double)(target.PositionY - attacker.PositionY));

    private static (decimal X, decimal Y) EstimatePositionFromResult(decimal x, decimal y, MovementResult result, int tableWidth, int tableDepth)
    {
        var segments = result.Segments is { Count: > 0 }
            ? result.Segments
            : [new MovementSegment(result.EndingCourse, result.EndingVelocity)];

        var currentX = x;
        var currentY = y;
        foreach (var segment in segments)
        {
            (currentX, currentY) = EstimatePosition(currentX, currentY, segment.Distance, segment.Course, tableWidth, tableDepth);
        }

        return (currentX, currentY);
    }

    private static (decimal X, decimal Y) EstimatePosition(decimal x, decimal y, decimal distance, int course, int tableWidth, int tableDepth)
    {
        var radians = course * Math.PI / 6;
        var nextX = x + ((decimal)Math.Sin(radians) * distance);
        var nextY = y - ((decimal)Math.Cos(radians) * distance);
        // Round to a thousandth of a measurement unit. The sine of a straight-down course is not
        // exactly zero in floating point, and without this the residue accumulates into positions
        // that read as 20.000000000000001 on a table measured in whole units.
        return (
            ClampPosition(Math.Round(nextX, 3), tableWidth),
            ClampPosition(Math.Round(nextY, 3), tableDepth));
    }

    private static void AdvanceOrdnanceMarkers(MatchState match)
    {
        foreach (var marker in match.OrdnanceMarkers.Where(o => o.Status == "Active").ToArray())
        {
            (marker.PositionX, marker.PositionY) = EstimatePosition(marker.PositionX, marker.PositionY, marker.Speed, marker.Course, match.TableWidth, match.TableDepth);
            marker.EnduranceRemaining = Math.Max(0, marker.EnduranceRemaining - 1);
            if (marker.EnduranceRemaining == 0)
            {
                marker.Status = "Expired";
                match.AddLog("Ordnance", match.Phase.ToString(), $"{marker.Name} marker expired at {marker.PositionX:0.#},{marker.PositionY:0.#}.");
            }
        }
    }

    private static string PositionEdgeNote(ShipState ship, MatchState match)
    {
        var nearEdge = ship.PositionX <= 3 || ship.PositionY <= 3 || ship.PositionX >= match.TableWidth - 3 || ship.PositionY >= match.TableDepth - 3;
        return nearEdge ? " Near table edge." : string.Empty;
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

    private static int NormalizeCourse(int course)
    {
        var zeroBased = ((course - 1) % 12 + 12) % 12;
        return zeroBased + 1;
    }

    private static string NormalizeOrdnanceText(string? value, string fallback) => NormalizeText(value, fallback);

    /// <summary>Trims caller-supplied display text, falling back when it is blank.</summary>
    private static string NormalizeText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    /// <summary>Trims optional display text, collapsing blank input to null.</summary>
    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeOrdnanceStatus(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "spent" or "resolved" or "hit" => "Resolved",
            "expired" or "ended" => "Expired",
            _ => "Active",
        };

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
                $"{label}: {DescribeShip(match, ship)} pos {ship.PositionX:0.#},{ship.PositionY:0.#} on {match.TableWidth}x{match.TableDepth}, v{ship.CurrentVelocity}/c{ship.CurrentCourse}, hull {ship.HullDamage}/{ship.HullMax}, armor {ship.ArmorDamage}/{ship.ArmorMax}, screens {ship.ScreenRating}, {destroyed}.");
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

    /// <summary>
    /// Normalizes weapons for a restore. NormalizeWeapons drops blank-named mounts, which is right
    /// for a form row a user left empty but is silent data loss when recovering a fleet, so every
    /// mount is kept and an unnamed one gets a placeholder name instead.
    /// </summary>
    private static WeaponMountState[] NormalizeRestoredWeapons(IReadOnlyList<WeaponMountDto>? weapons) =>
        weapons is null or { Count: 0 }
            ? NormalizeWeapons(weapons)
            : NormalizeWeapons([.. weapons.Select(w => w with { Name = NormalizeText(w.Name, "Unnamed Mount") })]);

    /// <summary>
    /// Resolves the arcs a mount bears through. An explicit set wins; otherwise the legacy
    /// four-arc name is expanded. The aft arc is always removed, because every weapon has it
    /// blacked out, and a mount left with nothing is treated as bearing fore.
    /// </summary>
    private static FiringArc[] NormalizeArcs(WeaponMountDto weapon)
    {
        var arcs = weapon.Arcs is { Count: > 0 }
            ? weapon.Arcs
            : ExpandLegacyArc(weapon.Arc);
        // Keep the canonical clockwise order however the caller listed them.
        var firable = FiringArcs.Firable.Where(arcs.Contains).ToArray();
        return firable.Length > 0 ? firable : [FiringArc.Fore];
    }

    /// <summary>
    /// Expands a pre-six-arc mount name. The old arcs were 90 degrees wide, so each side arc
    /// becomes the two 60 degree arcs on that side, and the old aft arc becomes the two quarters
    /// either side of the blind spot.
    /// </summary>
    private static IReadOnlyList<FiringArc> ExpandLegacyArc(string? legacyArc)
    {
        var name = legacyArc?.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return name switch
        {
            "all" => FiringArcs.Firable,
            "port" => [FiringArc.ForePort, FiringArc.AftPort],
            "starboard" => [FiringArc.ForeStarboard, FiringArc.AftStarboard],
            "aft" => [FiringArc.AftPort, FiringArc.AftStarboard],
            null or "" => [FiringArc.Fore],
            _ => FiringArcJsonConverter.TryParse(name, out var arc) && FiringArcs.CanFireThrough(arc)
                ? [arc]
                : [FiringArc.Fore],
        };
    }

    private static WeaponMountState[] NormalizeWeapons(IReadOnlyList<WeaponMountDto>? weapons)
    {
        if (weapons is null || weapons.Count == 0)
        {
            return
            [
                new WeaponMountState(Guid.NewGuid(), "Class-2 Beam", 2, 24, [FiringArc.Fore], 0, 0, 0)
            ];
        }

        return weapons
            .Where(w => !string.IsNullOrWhiteSpace(w.Name))
            .Select(w => new WeaponMountState(
                w.Id == Guid.Empty ? Guid.NewGuid() : w.Id,
                w.Name.Trim(),
                Math.Clamp(w.AttackDice, 1, 12),
                Math.Clamp(w.MaxRange, 1, 72),
                NormalizeArcs(w),
                Math.Clamp(w.AmmoMax, 0, 99),
                Math.Clamp(w.AmmoUsed, 0, Math.Max(0, w.AmmoMax)),
                Math.Clamp(w.ReloadTurns, 0, 12)) { IsDestroyed = w.IsDestroyed })
            .ToArray();
    }

    private static string NextCopyName(string sourceName, IEnumerable<string> existingNames)
    {
        var index = 2;
        string candidate;
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        do
        {
            candidate = $"{sourceName} {index}";
            index++;
        } while (names.Contains(candidate));

        return candidate;
    }

    private sealed record OrderCommitmentState(
        Guid ShipId,
        Guid OwnerParticipantId,
        string CommitmentHash,
        bool IsRevealed,
        bool? VerificationFailed,
        MovementOrder? RevealedOrder,
        MovementResult? Result);

    private sealed record DamageSnapshot(int Hull, int Armor, int FireControl, int Drive, int Weapons);

    private enum MatchPhase
    {
        FleetSetup,
        OrderEntry,
        OrdersLocked,
        Reveal,
        Movement,
        Firing
    }
}
