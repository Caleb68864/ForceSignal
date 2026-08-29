using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;
using ForceSignal.Modules.FullThrust.Damage;
using ForceSignal.Modules.FullThrust.Fighters;
using ForceSignal.Modules.FullThrust.Movement;
using ForceSignal.Modules.FullThrust.Ordnance;

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

    /// <summary>Lists seats for a match, showing which are still claimable. Needs the room code.</summary>
    IReadOnlyList<MatchSeatDto> GetSeats(Guid matchId, string joinCode);

    /// <summary>Claims an unclaimed seat in a restored match and issues a participant token.</summary>
    MatchJoinedResponse ClaimSeat(Guid matchId, Guid participantId, ClaimSeatRequest request);

    /// <summary>Resolves a room code to a match id and whether seats are still claimable.</summary>
    MatchIdentityDto FindMatchByCode(string joinCode);

    /// <summary>Updates participant readiness during setup.</summary>
    MatchSnapshotDto SetReady(Guid matchId, string participantToken, bool isReady);

    /// <summary>Updates table dimensions.</summary>
    MatchSnapshotDto UpdateTable(Guid matchId, UpdateMatchTableRequest request);

    /// <summary>Switches the rules layer the match is played under.</summary>
    MatchSnapshotDto UpdateRulesProfile(Guid matchId, UpdateRulesProfileRequest request);

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

    /// <summary>Puts a ship's damage control parties to work on systems lost to threshold checks.</summary>
    MatchSnapshotDto AttemptRepairs(Guid shipId, AttemptRepairsRequest request);

    /// <summary>Flies a fighter group up to its move allowance in any direction.</summary>
    MatchSnapshotDto MoveFighterGroup(Guid matchId, MoveFighterGroupRequest request);

    /// <summary>Declares a participant has finished plotting, leaving unordered ships to hold course.</summary>
    MatchSnapshotDto DeclareOrdersComplete(Guid matchId, DeclareOrdersCompleteRequest request);

    /// <summary>
    /// Resolves a draft movement order without committing it, so a client can show where a plot
    /// lands without owning a second copy of the movement rules.
    /// </summary>
    OrderPreviewDto PreviewOrder(Guid matchId, PreviewOrderRequest request);

    /// <summary>Commits a hidden movement order.</summary>
    MatchSnapshotDto CommitOrder(Guid matchId, CommitOrderRequest request);

    /// <summary>Reveals and verifies a hidden movement order.</summary>
    MatchSnapshotDto RevealOrder(Guid matchId, RevealOrderRequest request);

    /// <summary>
    /// Answers whether a shot could be taken and what it would need, without taking it. Held to the
    /// same checks as <see cref="FireWeapon"/>, so the console cannot offer a refused shot.
    /// </summary>
    FiringSolutionDto GetFiringSolution(Guid matchId, FiringSolutionRequest request);

    /// <summary>Resolves one weapon attack.</summary>
    MatchSnapshotDto FireWeapon(Guid matchId, FireWeaponRequest request);

    /// <summary>Ends a ship's fire for the turn and rolls any threshold checks it earned.</summary>
    MatchSnapshotDto CeaseFire(Guid matchId, CeaseFireRequest request);

    /// <summary>Advances match phase or starts the next turn.</summary>
    MatchSnapshotDto AdvanceTurn(Guid matchId, string participantToken);

    /// <summary>The stored rows that could not be brought back at startup, so the host can say so.</summary>
    IReadOnlyList<SkippedSave> SkippedSaves { get; }
}

/// <summary>In-memory implementation of match orchestration for local and early self-hosted play.</summary>
/// <param name="rollDie">Die source for firing resolution, injectable so tests are deterministic.</param>
/// <param name="store">
/// Where matches are written so they survive a restart. Defaults to keeping nothing, which is what
/// the tests and a throwaway session want.
/// </param>
public sealed partial class InMemoryMatchService(Func<int>? rollDie = null, IMatchStore? store = null) : IMatchService
{
    private readonly IMatchStore _store = store ?? NoMatchStore.Instance;
    private readonly FullThrustLightCinematicRules _rules = new();
    private readonly FullThrustLightFiringRules _firingRules = new(rollDie);
    private readonly FullThrustLightPulseTorpedoRules _torpedoRules = new(rollDie);
    private readonly FullThrustNeedleBeamRules _needleRules = new(rollDie);
    private readonly FullThrustLightThresholdRules _thresholdRules = new(rollDie);
    private readonly FullThrustDamageControlRules _repairRules = new(rollDie);
    private readonly FullThrustPointDefenseRules _pointDefenseRules = new(rollDie);
    private readonly FullThrustSalvoMissileRules _salvoRules = new(rollDie);
    private readonly FullThrustCarrierOperationRules _carrierRules = new(rollDie);
    // The firing initiative die-off is the service's own roll rather than any rules module's.
    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));
    private readonly Sha256CommitmentService _commitments = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, MatchState> _matches = [];
    private readonly Dictionary<string, Guid> _joinCodes = new(StringComparer.OrdinalIgnoreCase);

    // Ships, fleets, and ordnance markers are addressed by their own id, without the match they
    // belong to. Finding the owning match used to mean scanning every match's collections on every
    // such call, which is work proportional to everything the server is hosting for an operation
    // that touches one ship. These map an entity straight to its match instead.
    private readonly Dictionary<Guid, Guid> _shipToMatch = [];
    private readonly Dictionary<Guid, Guid> _fleetToMatch = [];
    private readonly Dictionary<Guid, Guid> _markerToMatch = [];

    /// <summary>
    /// Builds the service and brings back whatever the store was holding, so a restart resumes a
    /// game rather than ending it.
    /// </summary>
    /// <remarks>
    /// This exists only because a primary constructor has no body to put the load in. It runs
    /// before the first request can arrive, since the service is a singleton built during startup,
    /// and it takes the lock anyway so a store that is slow cannot race the first player in.
    /// </remarks>
    /// <param name="rollDie">Die source for firing resolution.</param>
    /// <param name="store">Where matches are written.</param>
    /// <param name="loadPersisted">
    /// False to start empty and ignore what is stored. Only a test that wants a clean service over
    /// a populated store has any use for this.
    /// </param>
    public InMemoryMatchService(Func<int>? rollDie, IMatchStore? store, bool loadPersisted)
        : this(rollDie, store)
    {
        if (!loadPersisted)
        {
            return;
        }

        lock (_gate)
        {
            LoadPersistedMatches();
        }
    }

    // Ceilings on how much state one match may hold. None of these is a rules limit - they are far
    // above any real game - they exist so that a mistyped number, a runaway client, or a crafted
    // snapshot cannot turn into unbounded memory on a machine someone is running off a laptop at
    // the table.
    private const int MaxParticipantsPerMatch = 16;
    private const int MaxFleetsPerMatch = 32;
    private const int MaxShipsPerMatch = 400;
    private const int MaxWeaponsPerShip = 40;
    private const int MaxOrdnanceMarkersPerMatch = 400;
    private const int MaxFiringResultsPerMatch = 5000;

    /// <summary>
    /// How many battle log entries a match keeps. The log is replayed in full inside every
    /// snapshot, and a snapshot goes out on every mutation, so an uncapped log makes each turn of
    /// a long game slower than the last. The oldest entries are dropped once the cap is reached;
    /// the export a player takes for the after-action review is written as the game goes.
    /// </summary>
    private const int MaxLogEntriesPerMatch = 4000;

    /// <summary>Most repair jobs one request may carry. Far above the parties any ship has.</summary>
    private const int MaxRepairJobs = 32;

    /// <summary>Longest caller-supplied display string kept. Longer text is truncated, not refused.</summary>
    private const int MaxDisplayTextLength = 120;

    /// <summary>Longest battle-log line a restored snapshot may carry. See <c>NormalizeLogMessage</c>.</summary>
    private const int MaxLogMessageLength = 1000;

    /// <summary>
    /// How long a match with nobody touching it is kept before its memory is reclaimed. A game can
    /// sit idle over a lunch break or an argument about a range measurement, so the window is long
    /// enough that no real table ever hits it - it exists so a server left running for a month does
    /// not still be holding every match anyone ever opened on it.
    /// </summary>
    private static readonly TimeSpan IdleMatchRetention = TimeSpan.FromHours(24);

    /// <summary>Matches held at once. Reached only if that many are opened inside the retention window.</summary>
    private const int MaxConcurrentMatches = 500;

    /// <summary>Refuses one more of something when a match is already holding its ceiling.</summary>
    private static void RequireRoom(int current, int max, string what)
    {
        if (current >= max)
        {
            throw new InvalidOperationException($"This match already holds {max} {what}, which is as many as it tracks.");
        }
    }

    /// <summary>Refuses a restored collection that is larger than the match ceiling allows.</summary>
    private static void RequireWithin(int count, int max, string what)
    {
        if (count > max)
        {
            throw new InvalidOperationException($"Snapshot carries {count} {what}, past the {max} a match can hold.");
        }
    }

    public MatchCreatedResponse CreateMatch(CreateMatchRequest request)
    {
        lock (_gate)
        {
            EvictIdleMatches();
            var matchId = Guid.NewGuid();
            var participant = ParticipantState.Create(NormalizeText(request.DisplayName, "Admiral"), "Owner");
            var joinCode = CreateJoinCode();
            var match = new MatchState(matchId, joinCode, NormalizeText(request.MatchName, "Space Fleet Match"), participant)
            {
                Rules = (request.Rules ?? RulesProfile.Empty).Normalized(),
                TableWidth = Math.Clamp(request.TableWidth, 24, 144),
                TableDepth = Math.Clamp(request.TableDepth, 24, 96)
            };
            match.AddLog("Setup", "FleetSetup", "Match created.");
            _matches.Add(matchId, match);
            _joinCodes.Add(joinCode, matchId);
            match.Persist = Persist;
            Persist(match);
            return new MatchCreatedResponse(matchId, joinCode, participant.Id, participant.Token);
        }
    }

    public MatchJoinedResponse JoinMatch(JoinMatchRequest request)
    {
        lock (_gate)
        {
            // Trimmed, as the lookup trims: a code pasted with a stray space resolved to a match
            // one call earlier and then failed to join it, which read as the server changing its
            // mind.
            if (string.IsNullOrWhiteSpace(request.JoinCode) || !_joinCodes.TryGetValue(request.JoinCode.Trim(), out var matchId))
            {
                throw new NotFoundException("Room code was not found.");
            }

            var match = _matches[matchId];
            match.LastActivity = DateTimeOffset.UtcNow;
            if (match.Participants.Any(p => !p.IsClaimed))
            {
                throw new InvalidOperationException("This match was restored from a backup. Claim your seat instead of joining.");
            }

            RequireRoom(match.Participants.Count, MaxParticipantsPerMatch, "players");
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
                && match.Participants.Any(p => p.IsClaimed && TokensMatch(p.Token, participantToken));
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

            var participant = match.Participants.SingleOrDefault(p => p.IsClaimed && TokensMatch(p.Token, participantToken));
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

    public MatchSnapshotDto UpdateRulesProfile(Guid matchId, UpdateRulesProfileRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            if (participant.Role != "Owner")
            {
                throw new UnauthorizedAccessException("Only the owner can change the rules layer.");
            }

            if (match.Phase != MatchPhase.FleetSetup)
            {
                throw new InvalidOperationException("The rules layer is settled during fleet setup, before a shot is fired.");
            }

            var supplied = (request.Rules ?? RulesProfile.Empty).Normalized();
            if (supplied.Validate() is { Count: > 0 } gaps)
            {
                throw new InvalidOperationException(
                    "That profile is not complete enough to play against. " + string.Join(" ", gaps));
            }

            match.Rules = supplied;
            // Screens above the new profile's ceiling come down with it, so no ship keeps a level
            // the profile does not have.
            foreach (var ship in match.Ships)
            {
                ship.ScreenRating = Math.Min(ship.ScreenRating, match.Rules.MaxScreenLevel);
                ship.ScreenDamage = ClampDamage(ship.ScreenDamage, ship.ScreenRating);
            }

            // The log reports the numbers back rather than describing them, so it stays a record of
            // what this match is being played against and never becomes a copy of anyone's rulebook.
            match.AddLog(
                "Setup",
                match.Phase.ToString(),
                $"Playing against '{match.Rules.Name}': d{match.Rules.DieFaces}, screens to level "
                    + $"{match.Rules.MaxScreenLevel}, beams band every {match.Rules.BeamRangeBandWidth}, "
                    + $"hulls in {match.Rules.ThresholdRowCount} rows, fighter groups fly "
                    + $"{match.Rules.FighterMoveAllowance}, needles reach {match.Rules.NeedleBeamRange}"
                    + (match.Rules.EnhancedNeedleBeams
                        ? $" and hole the hull on {match.Rules.NeedleHullDamageRoll} or better"
                        : string.Empty)
                    + (match.Rules.CarrierRatesFollowBays
                        ? ", flight operations following the bays"
                        : $", carriers working {match.Rules.TrueCarrierAllowance} groups a turn and other ships "
                            + $"{match.Rules.OtherShipAllowance}")
                    + (match.Rules.CarrierTurnaroundRoll ? ", and a recovered group rolls for turnaround" : string.Empty)
                    + ".");
            match.Touch("RulesProfileChanged");
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
            RequireRoom(match.Fleets.Count, MaxFleetsPerMatch, "fleets");
            var fleetName = NormalizeText(request.Name, "Fleet");
            var fleet = new FleetState(Guid.NewGuid(), participant.Id, fleetName, NormalizeOptionalText(request.Faction), NormalizeFleetColor(request.FleetColor));
            match.Fleets.Add(fleet);
            _fleetToMatch[fleet.Id] = match.Id;
            match.AddLog("Setup", match.Phase.ToString(), $"{fleetName} fleet created for {participant.DisplayName}.");
            match.Touch("FleetCreated");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto CreateShip(Guid fleetId, CreateShipRequest request)
    {
        lock (_gate)
        {
            var match = FindMatchByFleet(fleetId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var fleet = match.Fleets.Single(f => f.Id == fleetId);
            if (fleet.OwnerParticipantId != participant.Id)
            {
                throw new UnauthorizedAccessException("You can only add ships to your own fleet.");
            }

            RequireRoom(match.Ships.Count, MaxShipsPerMatch, "ships");
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
                Math.Clamp(request.ScreenRating, 0, match.Rules.MaxScreenLevel),
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
                PointDefenseSystems = ClampPointDefense(request.PointDefenseSystems),
                FighterBays = ClampFighterBays(request.FighterBays),
                DamageControlParties = ClampDamageControl(request.DamageControlParties),
            });
            _shipToMatch[match.Ships[^1].Id] = match.Id;
            match.AddLog("Setup", match.Phase.ToString(), $"{DescribeShip(match, match.Ships[^1])} added to {fleet.Name}.");
            match.Touch("ShipCreated");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto UpdateShipProfile(Guid shipId, UpdateShipProfileRequest request)
    {
        lock (_gate)
        {
            var match = FindMatchByShip(shipId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var ship = FindOwnedShip(match, participant.Id, shipId);

            var shipState = new ShipMovementState(request.CurrentVelocity, request.CurrentCourse);
            var validation = _rules.Validate(shipState, request.ThrustRating, new MovementOrder(0, 0, TurnDirection.None));
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(" ", validation.Errors));
            }

            ship.Name = NormalizeText(request.Name, ship.Name);
            ship.ClassName = NormalizeOptionalText(request.ClassName);
            ship.ThrustRating = Math.Clamp(request.ThrustRating, 0, 20);
            ship.CurrentVelocity = request.CurrentVelocity;
            ship.CurrentCourse = request.CurrentCourse;
            ship.HullMax = Math.Clamp(request.HullMax, 1, 80);
            ship.ArmorMax = Math.Clamp(request.ArmorMax, 0, 40);
            ship.PositionX = ClampPosition(request.PositionX, match.TableWidth);
            ship.PositionY = ClampPosition(request.PositionY, match.TableDepth);
            ship.ScreenRating = Math.Clamp(request.ScreenRating, 0, match.Rules.MaxScreenLevel);
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
            ship.PointDefenseSystems = ClampPointDefense(request.PointDefenseSystems);
            ship.FighterBays = ClampFighterBays(request.FighterBays);
            ship.DamageControlParties = ClampDamageControl(request.DamageControlParties);
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
            var match = FindMatchByShip(shipId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var source = FindOwnedShip(match, participant.Id, shipId);
            RequireRoom(match.Ships.Count, MaxShipsPerMatch, "ships");
            var copyName = NormalizeText(
                request.Name,
                NextCopyName(source.Name, match.Ships.Where(s => s.FleetId == source.FleetId).Select(s => s.Name)));

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
                source.Weapons.Select(w => new WeaponMountState(Guid.NewGuid(), w.Name, w.AttackDice, w.MaxRange, w.Arcs, w.AmmoMax, w.AmmoUsed, w.ReloadTurns, w.Kind) { IsDestroyed = w.IsDestroyed }).ToList(),
                source.IconKey)
            {
                FighterEnduranceMax = source.FighterEnduranceMax,
                FighterEnduranceUsed = source.FighterEnduranceUsed,
                FighterMaxRange = source.FighterMaxRange,
                FighterStatus = source.FighterStatus,
                PointDefenseSystems = source.PointDefenseSystems,
                FighterBays = source.FighterBays,
                DamageControlParties = source.DamageControlParties,
                HomeCarrierShipId = source.HomeCarrierShipId,
                PointsValue = source.PointsValue,
            });
            _shipToMatch[match.Ships[^1].Id] = match.Id;
            match.AddLog("Setup", match.Phase.ToString(), $"{DescribeShip(match, source)} duplicated as {copyName}.");
            match.Touch("ShipDuplicated");
            return ToSnapshot(match);
        }
    }


    public MatchSnapshotDto UpdateShipDamage(Guid shipId, UpdateShipDamageRequest request)
    {
        lock (_gate)
        {
            var match = FindMatchByShip(shipId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var ship = FindOwnedShip(match, participant.Id, shipId);

            var before = CaptureDamage(ship);
            ship.HullDamage = ClampDamage(request.HullDamage, ship.HullMax);
            ship.ArmorDamage = ClampDamage(request.ArmorDamage, ship.ArmorMax);
            ship.FireControlDamage = ClampDamage(request.FireControlDamage, ship.FireControlMax);
            ship.ScreenDamage = ClampDamage(request.ScreenDamage, ship.ScreenRating);
            ship.FighterBayDamage = ClampDamage(request.FighterBayDamage, ship.FighterBays);
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
            var match = FindMatchByShip(shipId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var ship = FindOwnedShip(match, participant.Id, shipId);
            if (!IsFighterGroup(ship.IconKey, ship.ClassName))
            {
                throw new InvalidOperationException("Fighter operations can only be tracked on fighter groups.");
            }

            // Launching or recovering is a carrier operation with rules attached, so a status change
            // that means either one is checked before anything is written.
            var nextStatus = NormalizeFighterStatus(request.FighterStatus, ship.IconKey, ship.ClassName);
            var isLaunch = ship.FighterStatus == "Docked" && nextStatus != "Docked";
            var isRecovery = ship.FighterStatus != "Docked" && nextStatus == "Docked";
            if (isLaunch || isRecovery)
            {
                ResolveCarrierOperation(match, ship, request.HomeCarrierShipId, isLaunch);
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
            RequireRoom(match.OrdnanceMarkers.Count, MaxOrdnanceMarkersPerMatch, "ordnance markers");
            var source = request.SourceShipId is Guid sourceId
                ? FindOwnedShip(match, participant.Id, sourceId)
                : null;
            var target = request.TargetShipId is Guid targetId
                ? match.Ships.SingleOrDefault(s => s.Id == targetId) ?? throw new NotFoundException("Target ship was not found.")
                : null;

            // A salvo is thrown at a point of aim within the launcher's reach - 24mu for a standard
            // salvo, 36 for extended range - so the placement is checked against the firing ship.
            var launchReach = Math.Clamp(request.MaxRange <= 0 ? 24 : request.MaxRange, 1, 120);
            if (source is not null && IsSalvoMarkerType(request.MarkerType))
            {
                var aimRange = Math.Sqrt(
                    Math.Pow((double)(ClampPosition(request.PositionX, match.TableWidth) - source.PositionX), 2)
                    + Math.Pow((double)(ClampPosition(request.PositionY, match.TableDepth) - source.PositionY), 2));
                if (aimRange > launchReach)
                {
                    throw new InvalidOperationException(
                        $"That point of aim is {aimRange:0.#} from {source.Name}, past the {launchReach} this salvo can reach.");
                }
            }

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
            _markerToMatch[marker.Id] = match.Id;
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
            var match = FindMatchByMarker(markerId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var marker = FindOwnedOrdnanceMarker(match, participant.Id, markerId);
            var target = request.TargetShipId is Guid targetId
                ? match.Ships.SingleOrDefault(s => s.Id == targetId) ?? throw new NotFoundException("Target ship was not found.")
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
            var match = FindMatchByMarker(markerId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var marker = FindOwnedOrdnanceMarker(match, participant.Id, markerId);
            match.OrdnanceMarkers.Remove(marker);
            _markerToMatch.Remove(marker.Id);
            match.AddLog("Ordnance", match.Phase.ToString(), $"{marker.Name} marker removed from the table.");
            match.Touch("OrdnanceMarkerRemoved");
            return ToSnapshot(match);
        }
    }

    public MatchSnapshotDto AttemptRepairs(Guid shipId, AttemptRepairsRequest request)
    {
        lock (_gate)
        {
            var match = FindMatchByShip(shipId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var ship = FindOwnedShip(match, participant.Id, shipId);
            if (match.Phase is not (MatchPhase.OrderEntry or MatchPhase.OrdersLocked))
            {
                throw new InvalidOperationException("Damage control works between turns, while orders are being written.");
            }

            if (IsDestroyed(ship))
            {
                throw new InvalidOperationException($"{ship.Name} is a wreck.");
            }

            if (match.RepairedShipIds.Contains(ship.Id))
            {
                throw new InvalidOperationException($"{ship.Name} has already worked its damage control this turn.");
            }

            var jobs = request.Jobs ?? [];
            if (jobs.Count == 0)
            {
                throw new InvalidOperationException("No repair jobs were assigned.");
            }

            RequireWithin(jobs.Count, MaxRepairJobs, "repair jobs");

            // Two parties cannot both be put on the same job by listing it twice - the second roll
            // would be against a system the first already brought back, so the parties would be
            // spent for nothing.
            if (jobs.Select(job => (job.Kind, job.WeaponId)).Distinct().Count() != jobs.Count)
            {
                throw new InvalidOperationException("The same system was assigned twice. Put the parties on one job instead.");
            }

            if (ship.DamageControlParties <= 0)
            {
                throw new InvalidOperationException($"{ship.Name} has no damage control parties left.");
            }

            // Count what will actually be used, not what was asked for. A job may only take so many
            // parties, so budgeting against the requested number silently debited a ship for parties
            // that then sat idle - the player was penalised for a number the form had accepted.
            // Summing as a long also keeps two absurd numbers from overflowing into a passing check.
            var assigned = jobs.Sum(job => (long)PartiesFor(job, match.Rules));
            if (assigned > ship.DamageControlParties)
            {
                throw new InvalidOperationException(
                    $"{ship.Name} has {ship.DamageControlParties} damage control part{(ship.DamageControlParties == 1 ? "y" : "ies")} and {assigned} were assigned.");
            }

            // Every job is checked before a single die is rolled, so a bad assignment cannot half-run.
            var planned = jobs.Select(job => PlanRepair(ship, job, match.Rules)).ToArray();

            var outcomes = new List<string>();
            foreach (var job in planned)
            {
                var attempt = _repairRules.Resolve(job, match.Rules);
                var name = DescribeRepairTarget(ship, job);
                if (attempt.IsRepaired)
                {
                    ApplyRepair(ship, job);
                    outcomes.Add($"{name} back online (needed {attempt.Needed}, rolled {attempt.Roll})");
                }
                else
                {
                    outcomes.Add($"{name} still down (needed {attempt.Needed}, rolled {attempt.Roll})");
                }
            }

            match.RepairedShipIds.Add(ship.Id);
            match.AddLog(
                "Repair",
                match.Phase.ToString(),
                $"{DescribeShip(match, ship)} damage control: {string.Join("; ", outcomes)}.");
            match.Touch("RepairsAttempted");
            return ToSnapshot(match);
        }
    }

    /// <summary>
    /// Turns a requested job into one the rules can roll, refusing anything that is not actually
    /// broken. Hull damage and lost damage control parties are never repairable, and screens and
    /// fighter bays are not yet, because ForceSignal does not record what they started at.
    /// </summary>
    /// <summary>
    /// How many parties a requested job actually takes. One place, so the budget check and the roll
    /// can never disagree about it.
    /// </summary>
    private int PartiesFor(RepairJobDto job, RulesProfile rules) =>
        Math.Clamp(job.Parties <= 0 ? 1 : job.Parties, 1, _repairRules.MaxPartiesPerJob(rules));

    private RepairJob PlanRepair(ShipState ship, RepairJobDto job, RulesProfile rules)
    {
        var parties = PartiesFor(job, rules);
        switch (job.Kind)
        {
            case ShipSystemKind.FireControl:
                RequireRepairable(ship, ship.FireControlDamage, ship.NeedledFireControl, "fire control");
                return new RepairJob(job.Kind, null, parties);
            case ShipSystemKind.Drive:
                // Drives are lost in two steps, so the needled steps count against the same ladder.
                RequireRepairable(ship, ship.DriveDamage > 0 ? ship.DriveDamage >= ship.ThrustRating ? 2 : 1 : 0, ship.NeedledDrives, "drive damage");
                return new RepairJob(job.Kind, null, parties);
            case ShipSystemKind.Screen:
                RequireRepairable(ship, ship.ScreenDamage, ship.NeedledScreens, "screen damage");
                return new RepairJob(job.Kind, null, parties);
            case ShipSystemKind.FighterBay:
                RequireRepairable(ship, ship.FighterBayDamage, ship.NeedledBays, "bay damage");
                return new RepairJob(job.Kind, null, parties);
            case ShipSystemKind.Weapon:
                var mount = ship.Weapons.SingleOrDefault(weapon => weapon.Id == job.WeaponId)
                    ?? throw new NotFoundException("That weapon mount was not found.");
                if (!mount.IsDestroyed)
                {
                    throw new InvalidOperationException($"{mount.Name} is already working.");
                }

                if (mount.IsNeedleKilled)
                {
                    throw new InvalidOperationException($"{mount.Name} was cut out by a needle beam and is beyond damage control.");
                }

                return new RepairJob(job.Kind, mount.Id, parties);
            default:
                throw new InvalidOperationException(
                    $"{job.Kind} cannot be repaired by damage control: hull damage and lost parties never come back.");
        }
    }

    /// <summary>
    /// Refuses a job with nothing left to fix. Needle-beam losses count against the total, because a
    /// needled system is cut out rather than broken and no party can jury-rig it back.
    /// </summary>
    private static void RequireRepairable(ShipState ship, int damage, int needled, string what)
    {
        if (damage <= 0)
        {
            throw new InvalidOperationException($"{ship.Name} has no {what} to repair.");
        }

        if (damage - needled <= 0)
        {
            throw new InvalidOperationException($"{ship.Name} lost its {what} to needle fire, which is beyond damage control.");
        }
    }

    private static string DescribeRepairTarget(ShipState ship, RepairJob job) => job.Kind switch
    {
        ShipSystemKind.FireControl => "fire control",
        ShipSystemKind.Drive => "drives",
        ShipSystemKind.Screen => "screens",
        ShipSystemKind.FighterBay => "fighter bay",
        ShipSystemKind.Weapon => ship.Weapons.SingleOrDefault(weapon => weapon.Id == job.WeaponId)?.Name ?? "weapon mount",
        _ => job.Kind.ToString(),
    };

    /// <summary>Brings one system back. Drives come back in halves, as they were lost.</summary>
    private static void ApplyRepair(ShipState ship, RepairJob job)
    {
        switch (job.Kind)
        {
            case ShipSystemKind.FireControl:
                ship.FireControlDamage = Math.Max(0, ship.FireControlDamage - 1);
                break;
            case ShipSystemKind.Screen:
                ship.ScreenDamage = Math.Max(0, ship.ScreenDamage - 1);
                break;
            case ShipSystemKind.FighterBay:
                ship.FighterBayDamage = Math.Max(0, ship.FighterBayDamage - 1);
                break;
            case ShipSystemKind.Drive:
                // One success on dead drives gets half the thrust back; a second clears the rest.
                var halved = (ship.ThrustRating + 1) / 2;
                ship.DriveDamage = ship.DriveDamage > halved ? halved : 0;
                break;
            case ShipSystemKind.Weapon:
                var mount = ship.Weapons.SingleOrDefault(weapon => weapon.Id == job.WeaponId);
                if (mount is not null)
                {
                    mount.IsDestroyed = false;
                }

                break;
            default:
                break;
        }
    }

    public MatchSnapshotDto MoveFighterGroup(Guid matchId, MoveFighterGroupRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            if (match.Phase is MatchPhase.FleetSetup or MatchPhase.Firing)
            {
                throw new InvalidOperationException("Fighter groups fly between plotting and the firing phase.");
            }

            var group = FindOwnedShip(match, participant.Id, request.ShipId);
            if (!IsFighterGroupShip(group))
            {
                throw new InvalidOperationException($"{group.Name} is not a fighter group. Plot a course for it instead.");
            }

            if (IsDestroyed(group))
            {
                throw new InvalidOperationException($"{group.Name} has been wiped out.");
            }

            if (match.MovedFighterGroupIds.Contains(group.Id))
            {
                throw new InvalidOperationException($"{group.Name} has already flown this turn.");
            }

            var destinationX = ClampPosition(request.PositionX, match.TableWidth);
            var destinationY = ClampPosition(request.PositionY, match.TableDepth);
            var distance = Math.Round((decimal)Math.Sqrt(
                Math.Pow((double)(destinationX - group.PositionX), 2)
                + Math.Pow((double)(destinationY - group.PositionY), 2)), 1);
            var allowance = match.Rules.FighterMoveAllowance;
            if (distance > allowance)
            {
                throw new InvalidOperationException(
                    $"That is {distance:0.#} away, past the {allowance} {group.Name} can fly in a turn.");
            }

            var startingX = group.PositionX;
            var startingY = group.PositionY;
            group.PositionX = destinationX;
            group.PositionY = destinationY;
            // The stand points the way the group flew, which is what its fore arc is measured from.
            if (distance > 0)
            {
                group.CurrentCourse = CourseTowards(startingX, startingY, destinationX, destinationY);
            }

            match.MovedFighterGroupIds.Add(group.Id);
            match.AddLog(
                "Fighters",
                match.Phase.ToString(),
                $"{DescribeShip(match, group)} flew {distance:0.#} from {startingX:0.#},{startingY:0.#} to {destinationX:0.#},{destinationY:0.#}, now facing course {group.CurrentCourse}.{PositionEdgeNote(group, match)}");
            match.Touch("FighterGroupMoved");
            return ToSnapshot(match);
        }
    }

    /// <summary>
    /// Checks and books a launch or a recovery. A carrier doing either must hold course and speed for
    /// the turn, may only work so many groups a turn, and only has room for as many groups as it has
    /// bays left. On success the group is placed on the carrier: a launch deploys from it, and a
    /// recovery has the group meet it.
    /// </summary>
    private void ResolveCarrierOperation(MatchState match, ShipState group, Guid? requestedCarrierId, bool isLaunch)
    {
        var carrierId = requestedCarrierId ?? group.HomeCarrierShipId;
        var carrier = carrierId is Guid id ? match.Ships.SingleOrDefault(s => s.Id == id) : null;
        if (carrier is null)
        {
            // A group with no carrier assigned is being deployed or tidied up by hand rather than
            // flown off a deck, so there is no carrier operation to police.
            return;
        }

        if (IsDestroyed(carrier))
        {
            throw new InvalidOperationException($"{carrier.Name} is gone; {group.Name} has nowhere to land.");
        }

        if (EffectiveBays(carrier) <= 0)
        {
            throw new InvalidOperationException($"{carrier.Name} has no working fighter bays.");
        }

        if (isLaunch)
        {
            RequireTurnedAround(match, group);
        }

        // Holding course and velocity is the price of flight operations. No order at all counts,
        // because an unordered ship holds both.
        if (match.Commitments.TryGetValue(carrier.Id, out var order))
        {
            if (!order.IsRevealed || order.RevealedOrder is null)
            {
                throw new InvalidOperationException(
                    $"{carrier.Name} has orders still sealed. Reveal them, or leave it unordered, before flight operations.");
            }

            var plotted = order.RevealedOrder;
            var turning = plotted.TurnManeuvers is { Count: > 0 }
                ? plotted.TurnManeuvers.Sum(m => m.Steps) > 0
                : plotted.TurnSteps > 0;
            if (plotted.VelocityDelta != 0 || turning)
            {
                throw new InvalidOperationException(
                    $"{carrier.Name} is manoeuvring this turn. A carrier must hold course and speed to launch or recover.");
            }
        }

        RequireFlightAllowance(match, carrier, isLaunch);

        if (isLaunch)
        {
            group.PositionX = carrier.PositionX;
            group.PositionY = carrier.PositionY;
            group.CurrentCourse = carrier.CurrentCourse;
            // Off the deck and clear of the deck crews: whatever the last turnaround imposed is spent.
            group.FighterRelaunchTurn = 0;
        }
        else
        {
            var reach = Math.Sqrt(
                Math.Pow((double)(group.PositionX - carrier.PositionX), 2)
                + Math.Pow((double)(group.PositionY - carrier.PositionY), 2));
            if (reach > match.Rules.FighterMoveAllowance)
            {
                throw new InvalidOperationException(
                    $"{group.Name} is {reach:0.#} from {carrier.Name}, too far to make the rendezvous this turn.");
            }

            var docked = match.Ships.Count(candidate => candidate.Id != group.Id
                && candidate.HomeCarrierShipId == carrier.Id
                && IsFighterGroupShip(candidate)
                && candidate.FighterStatus == "Docked"
                && !IsDestroyed(candidate));
            var bays = EffectiveBays(carrier);
            if (docked >= bays)
            {
                throw new InvalidOperationException(
                    $"{carrier.Name} has {bays} bay{(bays == 1 ? string.Empty : "s")} and they are full.");
            }

            group.PositionX = carrier.PositionX;
            group.PositionY = carrier.PositionY;
        }

        var ledger = isLaunch ? match.CarrierLaunchesThisTurn : match.CarrierRecoveriesThisTurn;
        ledger[carrier.Id] = (ledger.TryGetValue(carrier.Id, out var worked) ? worked : 0) + 1;
        match.AddLog(
            "Fighters",
            match.Phase.ToString(),
            isLaunch
                ? $"{DescribeShip(match, group)} launched from {carrier.Name}, which holds course and speed this turn."
                : $"{DescribeShip(match, group)} landed aboard {carrier.Name}, which holds course and speed this turn.");

        if (!isLaunch)
        {
            RollTurnaround(match, group);
        }
    }

    /// <summary>
    /// Refuses a launch or a recovery the carrier has no allowance left for. Under the older layer
    /// launches and recoveries share one budget, so they are counted together and reported as the
    /// groups the ship has handled; the Fleet Book gives each its own and names which one ran out.
    /// </summary>
    private static void RequireFlightAllowance(MatchState match, ShipState carrier, bool isLaunch)
    {
        // Only the older layer asks whether a ship is a carrier by trade. The Fleet Book rate
        // follows the bays, which is exactly why it stops needing the word to mean anything.
        var isTrueCarrier = NormalizeIconText(carrier.ClassName).Contains("carrier", StringComparison.Ordinal)
            || carrier.IconKey == "carrier";
        var allowance = FullThrustCarrierOperationRules.AllowanceFor(match.Rules, EffectiveBays(carrier), isTrueCarrier);
        var launched = match.CarrierLaunchesThisTurn.TryGetValue(carrier.Id, out var alreadyLaunched) ? alreadyLaunched : 0;
        var recovered = match.CarrierRecoveriesThisTurn.TryGetValue(carrier.Id, out var alreadyRecovered) ? alreadyRecovered : 0;

        if (allowance.SharedAllowance)
        {
            var worked = launched + recovered;
            if (worked >= (isLaunch ? allowance.Launches : allowance.Recoveries))
            {
                throw new InvalidOperationException(
                    $"{carrier.Name} has already handled {worked} group{(worked == 1 ? string.Empty : "s")} this turn.");
            }

            return;
        }

        var used = isLaunch ? launched : recovered;
        var cap = isLaunch ? allowance.Launches : allowance.Recoveries;
        if (used >= cap)
        {
            var what = isLaunch ? "launched" : "recovered";
            throw new InvalidOperationException(
                $"{carrier.Name} has already {what} {used} group{(used == 1 ? string.Empty : "s")} this turn, which is all its {EffectiveBays(carrier)} working bays allow.");
        }
    }

    /// <summary>
    /// Refuses a launch by a group the deck crews have not finished with. Only a layer that rolls
    /// for turnaround can leave a group in that state.
    /// </summary>
    private static void RequireTurnedAround(MatchState match, ShipState group)
    {
        if (group.FighterGroundedForGame)
        {
            throw new InvalidOperationException(
                $"{group.Name} was written off on recovery and will not fly again this game.");
        }

        if (group.FighterRelaunchTurn > match.TurnNumber)
        {
            throw new InvalidOperationException(
                $"{group.Name} is still being turned around and cannot launch before turn {group.FighterRelaunchTurn}.");
        }
    }

    /// <summary>
    /// Rolls how long the deck crews need with a group that has just landed, where the layer uses
    /// the turnaround rule. Under the older layer a recovered group is ready whenever a bay is.
    /// </summary>
    private void RollTurnaround(MatchState match, ShipState group)
    {
        if (!match.Rules.CarrierTurnaroundRoll)
        {
            return;
        }

        var turnaround = _carrierRules.RollTurnaround(match.Rules);
        group.FighterGroundedForGame = turnaround.IsGroundedForGame;
        group.FighterRelaunchTurn = turnaround.IsGroundedForGame
            ? 0
            : match.TurnNumber + turnaround.TurnsBeforeRelaunch;
        match.AddLog(
            "Fighters",
            match.Phase.ToString(),
            turnaround.IsGroundedForGame
                ? $"{DescribeShip(match, group)} turnaround rolled {turnaround.Roll}: written off, it will not fly again this game."
                : $"{DescribeShip(match, group)} turnaround rolled {turnaround.Roll}: ready to launch again from turn {group.FighterRelaunchTurn}.");
    }

    /// <summary>The twelve-point course that best matches a heading across the table.</summary>
    private static int CourseTowards(decimal fromX, decimal fromY, decimal toX, decimal toY)
    {
        var bearing = Math.Atan2((double)(toX - fromX), -(double)(toY - fromY)) * 180 / Math.PI;
        var normalized = ((bearing % 360) + 360) % 360;
        return FullThrustLightCinematicRules.WrapCourse((int)Math.Round(normalized / 30));
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
        .. PlottableShipIds(match).Where(id =>
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
            // A ship whose reveal failed verification may re-lock during the reveal phase, so an
            // honest mistake - a mistyped salt, a device that lost its keys - does not deadlock the
            // turn.
            //
            // But only while nothing is public yet. A player controls whether their own reveal
            // fails, simply by revealing with the wrong salt, and this escape hatch used to be
            // granted on that basis alone. That turned a failed reveal into a privilege: fail on
            // purpose, read every opponent's revealed order and resolved movement out of the
            // snapshot, then re-lock knowing exactly where every enemy ship will end up. The whole
            // point of committing an order in advance was gone, and the only trace was one log
            // line. So the repair is allowed only while no order in the match has been revealed -
            // when there is nothing to have learned. Once anything is public, RevealOrder settles a
            // failure by dropping the order instead, and the ship holds its course.
            var isFailedRevealRepair = match.Phase == MatchPhase.Reveal
                && match.Commitments.TryGetValue(ship.Id, out var priorCommitment)
                && priorCommitment.VerificationFailed == true
                && !match.Commitments.Values.Any(c => c.IsRevealed);
            if (match.Phase is not (MatchPhase.OrderEntry or MatchPhase.OrdersLocked) && !isFailedRevealRepair)
            {
                throw new InvalidOperationException(
                    match.Phase == MatchPhase.Reveal
                        ? "Orders cannot be re-locked once any order in the match has been revealed."
                        : "Movement orders can only be locked during order entry.");
            }

            if (IsDestroyed(ship))
            {
                throw new InvalidOperationException($"{ship.Name} is destroyed and cannot receive movement orders.");
            }

            if (IsFighterGroupShip(ship))
            {
                throw new InvalidOperationException($"{ship.Name} is a fighter group: fly it straight to where it is going rather than plotting a course.");
            }

            RequireOrder(request.Order);
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
            var liveShipIds = PlottableShipIds(match);
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

            // Read before this reveal is written: whether anything in the match is already public
            // decides what a failure costs. Nothing public yet means nobody can have learned
            // anything, so a mismatch is treated as the honest mistake it almost always is.
            var anythingAlreadyRevealed = match.Commitments.Values.Any(c => c.IsRevealed);

            RequireOrder(request.Order);
            var normalized = _rules.Normalize(request.Order);
            var isValid = _commitments.Verify(commitment.CommitmentHash, normalized, request.Salt);

            if (!isValid && anythingAlreadyRevealed)
            {
                // An order that cannot be proved, once other orders are on the table, is an order
                // that was not given. Dropping the commitment entirely leaves the ship holding its
                // course and speed like any unordered ship, which settles the turn instead of
                // deadlocking it - and, crucially, does not hand the player a fresh plot made with
                // knowledge of where everyone else is going.
                match.Commitments.Remove(ship.Id);
                match.AddLog(
                    "Orders",
                    match.Phase.ToString(),
                    $"{DescribeShip(match, ship)} could not prove its locked order, and other orders were already revealed. Its order is discarded and it holds course and speed.");

                if (match.Commitments.Values.All(c => c.IsRevealed))
                {
                    match.Phase = MatchPhase.Movement;
                    match.AddLog("Phase", match.Phase.ToString(), "All movement orders revealed.");
                }

                match.Touch("OrderRevealFailed");
                return ToSnapshot(match);
            }

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
                    : $"{DescribeShip(match, ship)} reveal did not match its locked order. Re-lock and reveal again before anything else is revealed.");

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
                match.FiringParticipantId = null;
                match.ActivatedShipIds.Clear();
                match.TurnNumber++;
                match.Phase = MatchPhase.OrderEntry;
                match.Commitments.Clear();
                match.FiringResults.Clear();
                match.MovedFighterGroupIds.Clear();
                match.CarrierLaunchesThisTurn.Clear();
                match.CarrierRecoveriesThisTurn.Clear();
                match.RepairedShipIds.Clear();
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

            // Salvo missiles strike at the end of movement, before anyone opens fire.
            ResolveSalvoMissiles(match);

            match.Phase = MatchPhase.Firing;
            match.AddLog("Phase", match.Phase.ToString(), $"Turn {match.TurnNumber} firing phase opened.");
            match.ActivatedShipIds.Clear();
            RollFiringInitiative(match);
            AddShipStateSnapshot(match, "Start of firing phase");
            AddNoFireTelemetry(match);
            match.Touch("TurnAdvanced");
            return ToSnapshot(match);
        }
    }

    private MatchState FindMatch(Guid matchId)
    {
        if (!_matches.TryGetValue(matchId, out var match))
        {
            throw new NotFoundException("Match was not found.");
        }

        // Every read and every write comes through here, so this is the one place that knows a
        // match is still in use. Reading a snapshot counts: a table watching the map without
        // touching anything is not an abandoned game.
        match.LastActivity = DateTimeOffset.UtcNow;
        return match;
    }

    /// <summary>
    /// Drops matches nobody has touched inside the retention window, and their index entries with
    /// them, then refuses if the server is still full. Called when a match is opened rather than
    /// on a timer, so the service holds no background work and a process that is doing nothing
    /// stays doing nothing.
    /// </summary>
    /// <remarks>
    /// At the ceiling this refuses the new match rather than retiring the oldest live one. It used
    /// to do the opposite, on the theory that a table must always be able to start a game - but
    /// creating a match needs no credentials, so that theory handed anyone with a loop the power to
    /// destroy every game in progress, and a real table idle for two minutes over a range argument
    /// was the first to go. A live game is never destroyed to make room. A server genuinely holding
    /// five hundred matches inside a day is a server that needs a bigger ceiling, not a quieter one.
    /// </remarks>
    private void EvictIdleMatches()
    {
        var cutoff = DateTimeOffset.UtcNow - IdleMatchRetention;
        var stale = _matches.Values.Where(match => match.LastActivity < cutoff).Select(match => match.Id).ToArray();
        foreach (var matchId in stale)
        {
            Forget(matchId);
        }

        if (_matches.Count >= MaxConcurrentMatches)
        {
            throw new InvalidOperationException(
                $"This server is already hosting {MaxConcurrentMatches} matches, which is as many as it holds. Try again later.");
        }
    }

    private void Forget(Guid matchId)
    {
        if (!_matches.Remove(matchId, out var match))
        {
            return;
        }

        // A match that has been retired is gone on purpose, so the stored copy goes with it -
        // otherwise every eviction would be undone by the next restart.
        _store.Remove(matchId);

        _joinCodes.Remove(match.JoinCode);
        foreach (var fleet in match.Fleets)
        {
            _fleetToMatch.Remove(fleet.Id);
        }

        foreach (var ship in match.Ships)
        {
            _shipToMatch.Remove(ship.Id);
        }

        foreach (var marker in match.OrdnanceMarkers)
        {
            _markerToMatch.Remove(marker.Id);
        }
    }

    /// <summary>The match a ship belongs to, addressed by ship id alone.</summary>
    private MatchState FindMatchByShip(Guid shipId) => FindMatchByEntity(_shipToMatch, shipId, "Ship");

    /// <summary>The match a fleet belongs to, addressed by fleet id alone.</summary>
    private MatchState FindMatchByFleet(Guid fleetId) => FindMatchByEntity(_fleetToMatch, fleetId, "Fleet");

    /// <summary>The match an ordnance marker belongs to, addressed by marker id alone.</summary>
    private MatchState FindMatchByMarker(Guid markerId) => FindMatchByEntity(_markerToMatch, markerId, "Ordnance marker");

    private MatchState FindMatchByEntity(Dictionary<Guid, Guid> index, Guid id, string what) =>
        index.TryGetValue(id, out var matchId) && _matches.TryGetValue(matchId, out var match)
            ? match
            : throw new NotFoundException($"{what} was not found.");

    /// <summary>Records where every addressable part of a match lives, so it can be found by id.</summary>
    private void IndexMatch(MatchState match)
    {
        foreach (var fleet in match.Fleets)
        {
            _fleetToMatch[fleet.Id] = match.Id;
        }

        foreach (var ship in match.Ships)
        {
            _shipToMatch[ship.Id] = match.Id;
        }

        foreach (var marker in match.OrdnanceMarkers)
        {
            _markerToMatch[marker.Id] = match.Id;
        }
    }

    /// <summary>
    /// Refuses a request whose order came over the wire as null. The contract says the field is
    /// required, but the serializer will bind a JSON null to it regardless, and the rules would
    /// then fall over reading it - which reached the player as a server fault rather than as the
    /// bad request it was.
    /// </summary>
    private static void RequireOrder(MovementOrder? order)
    {
        if (order is null)
        {
            throw new InvalidOperationException("A movement order is required.");
        }
    }

    private static ParticipantState FindParticipant(MatchState match, string token) =>
        string.IsNullOrWhiteSpace(token)
            ? throw new UnauthorizedAccessException("Participant token is invalid.")
            : match.Participants.SingleOrDefault(p => p.IsClaimed && TokensMatch(p.Token, token))
                ?? throw new UnauthorizedAccessException("Participant token is invalid.");

    /// <summary>
    /// Compares a participant token in time that does not depend on how much of it is right. An
    /// ordinary string comparison returns as soon as two bytes differ, which leaks the length of
    /// the matching prefix to anyone who can time the request - enough, over many tries, to
    /// recover a token a character at a time.
    /// </summary>
    private static bool TokensMatch(string stored, string supplied) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(stored),
            System.Text.Encoding.UTF8.GetBytes(supplied));

    private static ShipState FindOwnedShip(MatchState match, Guid participantId, Guid shipId)
    {
        var ship = match.Ships.SingleOrDefault(s => s.Id == shipId) ?? throw new NotFoundException("Ship was not found.");
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
            ?? throw new NotFoundException("Ordnance marker was not found.");
        if (marker.OwnerParticipantId != participantId)
        {
            throw new UnauthorizedAccessException("You can only update your own ordnance markers.");
        }

        return marker;
    }

    /// <summary>
    /// Words a room code is built from. A code is read aloud across a table, so the list is all
    /// short, unambiguous, distinctly-sounding words - no near-homophones, and nothing that reads
    /// the same over a noisy room. Thirty-two words in three slots is 32,768 codes, which is what
    /// keeps a code from being guessed by someone walking the space; see <see cref="CreateJoinCode"/>.
    /// </summary>
    private static readonly string[] JoinCodeWords =
    [
        "BLUE", "COMET", "SEVEN", "IRON", "ORBIT", "NOVA", "VECTOR", "LANCE",
        "DRIFT", "EMBER", "AXIS", "BRAVO", "CINDER", "DELTA", "ECHO", "FLARE",
        "GAMMA", "HELIX", "INDIGO", "JUNO", "KILO", "LUMEN", "MERIDIAN", "NADIR",
        "OSPREY", "PULSAR", "QUASAR", "RAVEN", "SIGMA", "TALON", "UMBRA", "ZENITH",
    ];

    /// <summary>
    /// Mints an unused room code. The code is the only thing standing between a stranger and a
    /// seat at the table, so the words are drawn from a cryptographic source rather than
    /// <see cref="Random"/> - a predictable sequence would let one code disclose the next.
    /// Generation is bounded: after enough collisions the code takes a numeric suffix, so a busy
    /// server degrades into longer codes instead of spinning forever.
    /// </summary>
    private string CreateJoinCode()
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var code = string.Join("-", Enumerable.Range(0, 3)
                .Select(_ => JoinCodeWords[System.Security.Cryptography.RandomNumberGenerator.GetInt32(JoinCodeWords.Length)]));
            if (!_joinCodes.ContainsKey(code))
            {
                return code;
            }
        }

        // The word space is crowded. Fall back to a suffixed code, which is still readable aloud
        // and cannot collide for long.
        for (var attempt = 0; attempt < 1024; attempt++)
        {
            var code = string.Join("-", Enumerable.Range(0, 3)
                .Select(_ => JoinCodeWords[System.Security.Cryptography.RandomNumberGenerator.GetInt32(JoinCodeWords.Length)]))
                + "-" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(100, 1000);
            if (!_joinCodes.ContainsKey(code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("No room code could be allocated. Close some finished matches and try again.");
    }

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

    /// <summary>
    /// Ships that are flown by written order. Fighter groups are moved by hand instead, so they never
    /// hold up plotting and never drift on a heading.
    /// </summary>
    private static List<Guid> PlottableShipIds(MatchState match) =>
        [.. match.Ships.Where(s => !IsDestroyed(s) && !IsFighterGroupShip(s)).Select(s => s.Id)];

    private static List<Guid> LiveShipIds(MatchState match) =>
        match.Ships.Where(s => !IsDestroyed(s)).Select(s => s.Id).ToList();

    private static bool IsFighterGroup(string iconKey, string? className) =>
        iconKey == "fighter-group" || NormalizeIconText(className).Contains("fighter", StringComparison.Ordinal);

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

        // Fighters are carried by specialised carriers *or* by larger warships with bays fitted, so
        // what qualifies a host is a bay rather than its class name. A carrier-classed hull with no
        // bays recorded yet is still accepted, so a fleet can be assembled in any order.
        var isCarrierClassed = carrier.IconKey == "carrier"
            || NormalizeIconText(carrier.ClassName).Contains("carrier", StringComparison.Ordinal);
        if (carrier.FighterBays <= 0 && !isCarrierClassed)
        {
            throw new InvalidOperationException($"{carrier.Name} has no fighter bays, so it cannot host a fighter group.");
        }

        return carrier.Id;
    }

    /// <summary>Screen levels still generating, after whatever has been shot away.</summary>
    private static int EffectiveScreens(ShipState ship) => Math.Max(0, ship.ScreenRating - ship.ScreenDamage);

    /// <summary>Fighter bays still working.</summary>
    private static int EffectiveBays(ShipState ship) => Math.Max(0, ship.FighterBays - ship.FighterBayDamage);

    /// <summary>True when this ship record stands for a group of fighters rather than a hull.</summary>
    private static bool IsFighterGroupShip(ShipState ship) => IsFighterGroup(ship.IconKey, ship.ClassName);

    /// <summary>
    /// Fighters still flying in a group. A group's hull boxes stand for its aircraft, so losses come
    /// straight off the number of dice it rolls.
    /// </summary>
    private static int SurvivingFighters(ShipState ship) => Math.Max(0, ship.HullMax - ship.HullDamage);

    /// <summary>
    /// Whether a fighter group has already been in combat this turn, either shooting or being shot
    /// at. Endurance is spent once per active turn however much fighting happens in it.
    /// </summary>
    private static bool AlreadyInCombatThisTurn(MatchState match, Guid shipId) =>
        match.FiringResults.Any(f => f.TurnNumber == match.TurnNumber
            && (f.AttackerShipId == shipId || f.TargetShipId == shipId));

    /// <summary>Spends a turn of combat endurance for a fighter group that has just been engaged.</summary>
    private static void SpendFighterEndurance(MatchState match, ShipState ship)
    {
        if (!IsFighterGroupShip(ship) || ship.FighterEnduranceMax <= 0 || AlreadyInCombatThisTurn(match, ship.Id))
        {
            return;
        }

        ship.FighterEnduranceUsed = Math.Min(ship.FighterEnduranceMax, ship.FighterEnduranceUsed + 1);
        match.AddLog(
            "Fighters",
            match.Phase.ToString(),
            $"{DescribeShip(match, ship)} spent a turn of endurance in combat: {ship.FighterEnduranceUsed}/{ship.FighterEnduranceMax} used.");
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

}
