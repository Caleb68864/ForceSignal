using ForceSignal.Domain.Rules;

namespace ForceSignal.Contracts.Matches;

/// <summary>Creates a new tabletop match and first participant session.</summary>
/// <param name="DisplayName">Display name for the first participant.</param>
/// <param name="MatchName">Optional human-readable match name.</param>
/// <param name="TableWidth">Table width in play units.</param>
/// <param name="TableDepth">Table depth in play units.</param>
public sealed record CreateMatchRequest(string DisplayName, string? MatchName, int TableWidth = 72, int TableDepth = 48);

/// <summary>Joins an existing match by room code.</summary>
/// <param name="JoinCode">Memorable room code shown by the host.</param>
/// <param name="DisplayName">Display name for the joining participant.</param>
public sealed record JoinMatchRequest(string JoinCode, string DisplayName);

/// <summary>Sets the caller readiness state during fleet setup.</summary>
/// <param name="IsReady">True when the participant is ready to start order entry.</param>
public sealed record ReadyRequest(bool IsReady);

/// <summary>Creates a fleet owned by the participant token.</summary>
/// <param name="ParticipantToken">Secret participant token issued when creating or joining a match.</param>
/// <param name="Name">Fleet name shown in the UI and log.</param>
/// <param name="Faction">Optional user-owned faction or force label.</param>
/// <param name="FleetColor">Optional CSS hex color used for map accents.</param>
public sealed record CreateFleetRequest(string ParticipantToken, string Name, string? Faction, string? FleetColor = null);

/// <summary>Adds a ship record to a fleet.</summary>
/// <param name="ParticipantToken">Secret participant token for the fleet owner.</param>
/// <param name="Name">Ship display name.</param>
/// <param name="ClassName">Optional class or hull type label.</param>
/// <param name="ThrustRating">Maximum thrust available for movement plotting.</param>
/// <param name="InitialVelocity">Starting velocity.</param>
/// <param name="InitialCourse">Starting course on the twelve-point course clock.</param>
/// <param name="HullMax">Maximum hull damage boxes.</param>
/// <param name="ArmorMax">Maximum armor damage boxes.</param>
/// <param name="StartX">Starting X position on the table.</param>
/// <param name="StartY">Starting Y position on the table.</param>
/// <param name="ScreenRating">Defensive screen rating.</param>
/// <param name="Weapons">Weapon mounts carried by the ship.</param>
/// <param name="IconKey">Map icon key selected for this ship.</param>
/// <param name="FighterEnduranceMax">Maximum fighter endurance turns for fighter groups.</param>
/// <param name="FighterEnduranceUsed">Tracked fighter endurance turns already spent.</param>
/// <param name="FighterMaxRange">Maximum operating range from the fighter group's home carrier.</param>
/// <param name="FighterStatus">Docked, Airborne, or Recovering status for fighter groups.</param>
/// <param name="HomeCarrierShipId">Optional carrier ship that launched or owns the fighter group.</param>
/// <param name="PointsValue">Nominal points value (NPV) recorded from the player's own design sheet.</param>
/// <param name="FireControlMax">Fire control systems carried. Each directs fire at one target.</param>
/// <param name="PointDefenseSystems">Point defence systems carried, for shooting down fighters and missiles.</param>
public sealed record CreateShipRequest(
    string ParticipantToken,
    string Name,
    string? ClassName,
    int ThrustRating,
    int InitialVelocity,
    int InitialCourse,
    int HullMax,
    int ArmorMax,
    decimal StartX = 0,
    decimal StartY = 0,
    int ScreenRating = 0,
    IReadOnlyList<WeaponMountDto>? Weapons = null,
    string? IconKey = null,
    int FighterEnduranceMax = 0,
    int FighterEnduranceUsed = 0,
    int FighterMaxRange = 0,
    string? FighterStatus = null,
    Guid? HomeCarrierShipId = null,
    int PointsValue = 0,
    int FireControlMax = 1,
    int PointDefenseSystems = 0);

/// <summary>Updates editable ship profile, position, and equipment fields.</summary>
public sealed record UpdateShipProfileRequest(
    string ParticipantToken,
    string Name,
    string? ClassName,
    int ThrustRating,
    int CurrentVelocity,
    int CurrentCourse,
    int HullMax,
    int ArmorMax,
    decimal PositionX = 0,
    decimal PositionY = 0,
    int ScreenRating = 0,
    IReadOnlyList<WeaponMountDto>? Weapons = null,
    string? IconKey = null,
    int FighterEnduranceMax = 0,
    int FighterEnduranceUsed = 0,
    int FighterMaxRange = 0,
    string? FighterStatus = null,
    Guid? HomeCarrierShipId = null,
    int PointsValue = 0,
    int FireControlMax = 1,
    int PointDefenseSystems = 0);

/// <summary>Updates fighter launch/recovery and endurance tracking for a fighter group.</summary>
public sealed record UpdateFighterOperationsRequest(
    string ParticipantToken,
    string FighterStatus,
    int FighterEnduranceUsed,
    int FighterEnduranceMax,
    int FighterMaxRange,
    Guid? HomeCarrierShipId = null);

/// <summary>Adds a launched ordnance or salvo marker to the table map.</summary>
public sealed record CreateOrdnanceMarkerRequest(
    string ParticipantToken,
    string Name,
    string MarkerType,
    Guid? SourceShipId,
    Guid? TargetShipId,
    decimal PositionX,
    decimal PositionY,
    int Course = 1,
    int Speed = 0,
    int EnduranceRemaining = 1,
    int AttackDice = 0,
    int MaxRange = 0,
    string Status = "Active");

/// <summary>Updates a launched ordnance or salvo marker on the table map.</summary>
public sealed record UpdateOrdnanceMarkerRequest(
    string ParticipantToken,
    string Name,
    string MarkerType,
    Guid? TargetShipId,
    decimal PositionX,
    decimal PositionY,
    int Course = 1,
    int Speed = 0,
    int EnduranceRemaining = 1,
    int AttackDice = 0,
    int MaxRange = 0,
    string Status = "Active");

/// <summary>Removes a launched ordnance or salvo marker from the table map.</summary>
public sealed record RemoveOrdnanceMarkerRequest(string ParticipantToken);

/// <summary>Sets the agreed points ceiling each player's fleets must fit inside. Zero means unlimited.</summary>
/// <param name="ParticipantToken">Owner participant token.</param>
/// <param name="PointsLimit">Points per player, or zero for no limit.</param>
public sealed record UpdateMatchPointsLimitRequest(string ParticipantToken, int PointsLimit);

/// <summary>Updates the physical table dimensions used by map planning.</summary>
public sealed record UpdateMatchTableRequest(string ParticipantToken, int TableWidth, int TableDepth);

/// <summary>Duplicates an owned ship, optionally assigning a new name.</summary>
public sealed record DuplicateShipRequest(string ParticipantToken, string? Name);

/// <summary>Replaces tracked damage values for a ship.</summary>
public sealed record UpdateShipDamageRequest(
    string ParticipantToken,
    int HullDamage,
    int ArmorDamage,
    int FireControlDamage,
    int DriveDamage,
    int WeaponDamage);

/// <summary>
/// Declares that a participant has finished plotting for the turn. Ships left without an order
/// simply hold their course and speed, so a fleet does not need an order written for every hull.
/// </summary>
/// <param name="ParticipantToken">Session token of the participant who is done plotting.</param>
public sealed record DeclareOrdersCompleteRequest(string ParticipantToken);

/// <summary>Commits a hidden movement order by storing its salted hash.</summary>
public sealed record CommitOrderRequest(
    string ParticipantToken,
    Guid ShipId,
    MovementOrder Order,
    string Salt);

/// <summary>Reveals a committed movement order and verifies it against the original salted hash.</summary>
public sealed record RevealOrderRequest(
    string ParticipantToken,
    Guid ShipId,
    MovementOrder Order,
    string Salt);

/// <summary>Resolves one weapon mount firing at a target during the firing phase.</summary>
/// <param name="ParticipantToken">Session token of the firing participant.</param>
/// <param name="AttackerShipId">The firing ship.</param>
/// <param name="TargetShipId">The ship being fired at.</param>
/// <param name="WeaponId">The mount being fired.</param>
/// <param name="Range">Range measured on the table, in mu.</param>
/// <param name="Arc">
/// Optional. The arc the caller believes the target lies in. The server works the arc out from the
/// two ships' positions and the firing ship's course; when this is supplied it must agree, which
/// catches a client and a table that have drifted apart.
/// </param>
public sealed record FireWeaponRequest(
    string ParticipantToken,
    Guid AttackerShipId,
    Guid TargetShipId,
    Guid WeaponId,
    int Range,
    FiringArc? Arc = null);

/// <summary>
/// Declares that a ship has finished firing for the turn, which is when its threshold checks are
/// rolled. Switching to another ship or ending the firing phase does the same thing implicitly.
/// </summary>
/// <param name="ParticipantToken">Session token of the firing participant.</param>
/// <param name="ShipId">The ship whose fire is complete.</param>
public sealed record CeaseFireRequest(string ParticipantToken, Guid ShipId);

/// <summary>Session details returned when a participant creates a match.</summary>
public sealed record MatchCreatedResponse(Guid MatchId, string JoinCode, Guid ParticipantId, string ParticipantToken);

/// <summary>Session details returned when a participant joins a match.</summary>
public sealed record MatchJoinedResponse(Guid MatchId, string JoinCode, Guid ParticipantId, string ParticipantToken);

/// <summary>Authoritative match state returned by API calls and SignalR notifications.</summary>
public sealed record MatchSnapshotDto(
    Guid MatchId,
    string JoinCode,
    string Name,
    string Phase,
    int TurnNumber,
    string RulesProfileKey,
    int TableWidth,
    int TableDepth,
    IReadOnlyList<ParticipantDto> Participants,
    IReadOnlyList<FleetDto> Fleets,
    IReadOnlyList<ShipDto> Ships,
    IReadOnlyList<OrderStatusDto> OrderStatuses,
    IReadOnlyList<RevealedOrderDto> RevealedOrders,
    IReadOnlyList<MovementResultDto> MovementResults,
    IReadOnlyList<FiringResultDto> FiringResults,
    IReadOnlyList<OrdnanceMarkerDto> OrdnanceMarkers,
    IReadOnlyList<MatchLogEntryDto> MatchLog,
    Guid? FiringShipId,
    Guid? FiringParticipantId,
    IReadOnlyList<Guid> ActivatedShipIds,
    long Version,
    int PointsLimit);

/// <summary>Participant display and readiness state.</summary>
public sealed record ParticipantDto(Guid Id, string DisplayName, string Role, bool IsReady, bool IsConnected, bool OrdersComplete);

/// <summary>Fleet display state and owner association.</summary>
public sealed record FleetDto(Guid Id, Guid OwnerParticipantId, string Name, string? Faction, string FleetColor);

/// <summary>Ship profile, table position, equipment, and damage state.</summary>
public sealed record ShipDto(
    Guid Id,
    Guid FleetId,
    string Name,
    string? ClassName,
    int ThrustRating,
    int CurrentVelocity,
    int CurrentCourse,
    decimal PositionX,
    decimal PositionY,
    int HullMax,
    int HullDamage,
    int ArmorMax,
    int ArmorDamage,
    int FireControlMax,
    int FireControlDamage,
    int PointDefenseSystems,
    int DriveDamage,
    int WeaponDamage,
    int ScreenRating,
    IReadOnlyList<WeaponMountDto> Weapons,
    bool IsDestroyed,
    IReadOnlyList<int> HullRows,
    int HullRowsCompleted,
    string IconKey,
    int FighterEnduranceMax,
    int FighterEnduranceUsed,
    int FighterMaxRange,
    string FighterStatus,
    Guid? HomeCarrierShipId,
    int PointsValue);

/// <summary>Commit/reveal status for a ship order.</summary>
public sealed record OrderStatusDto(Guid ShipId, Guid OwnerParticipantId, bool IsCommitted, bool IsRevealed, bool VerificationFailed);

/// <summary>Verified movement order visible after reveal.</summary>
public sealed record RevealedOrderDto(
    Guid ShipId,
    int VelocityDelta,
    int TurnSteps,
    TurnDirection TurnDirection,
    IReadOnlyList<TurnManeuver>? TurnManeuvers);

/// <summary>Resolved movement result for one ship.</summary>
public sealed record MovementResultDto(
    Guid ShipId,
    int StartingVelocity,
    int StartingCourse,
    int EndingVelocity,
    int EndingCourse,
    IReadOnlyList<MovementSegment>? Segments);

/// <summary>Weapon mount profile attached to a ship.</summary>
/// <param name="Id">Stable mount id.</param>
/// <param name="Name">Mount name as printed on the ship record.</param>
/// <param name="AttackDice">Dice rolled at the closest range band.</param>
/// <param name="MaxRange">Longest range the mount reaches, in mu.</param>
/// <param name="Arcs">
/// Arcs the mount bears through. A battery may bear through one, two, or three adjacent arcs, and
/// an all-round mount bears through every arc except aft, which is blacked out on every weapon.
/// </param>
/// <param name="AmmoMax">Rounds carried, or zero for an unlimited beam mount.</param>
/// <param name="AmmoUsed">Rounds already spent.</param>
/// <param name="ReloadTurns">Turns needed to reload, for the table's own bookkeeping.</param>
/// <param name="IsDestroyed">True once a threshold check has knocked this mount out.</param>
/// <param name="Kind">Beam battery or pulse torpedo launcher. Defaults to a beam.</param>
public sealed record WeaponMountDto(
    Guid Id,
    string Name,
    int AttackDice,
    int MaxRange,
    IReadOnlyList<FiringArc>? Arcs = null,
    int AmmoMax = 0,
    int AmmoUsed = 0,
    int ReloadTurns = 0,
    bool IsDestroyed = false,
    WeaponKind Kind = WeaponKind.Beam)
{
    /// <summary>
    /// Compatibility field for data written before ForceSignal used six arcs. When
    /// <see cref="Arcs"/> is empty this four-arc name is expanded: "Port" covers both port arcs,
    /// "Starboard" both starboard arcs, "Aft" the two quarters either side of the blind spot, and
    /// "All" everything a weapon may fire through. Never written back out.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Arc { get; init; }
}

/// <summary>Resolved firing details for one weapon attack.</summary>
public sealed record FiringResultDto(
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
    IReadOnlyList<int> DiceRolls,
    WeaponKind WeaponKind = WeaponKind.Beam,
    int? ToHitNumber = null,
    bool? IsHit = null,
    decimal MapRange = 0,
    bool RangeDisagreed = false);

/// <summary>Launched ordnance or salvo marker tracked on the table map.</summary>
public sealed record OrdnanceMarkerDto(
    Guid Id,
    Guid OwnerParticipantId,
    string Name,
    string MarkerType,
    Guid? SourceShipId,
    Guid? TargetShipId,
    decimal PositionX,
    decimal PositionY,
    int Course,
    int Speed,
    int EnduranceRemaining,
    int AttackDice,
    int MaxRange,
    string Status);

/// <summary>Claims an unclaimed seat in a restored match.</summary>
/// <param name="DisplayName">Seat display name, confirmed by the caller.</param>
public sealed record ClaimSeatRequest(string DisplayName);

/// <summary>An unclaimed or claimed seat in a restored match.</summary>
/// <param name="ParticipantId">Participant id preserved from the restored snapshot.</param>
/// <param name="DisplayName">Admiral name shown when picking a seat.</param>
/// <param name="Role">Owner or Player, preserved from the snapshot.</param>
/// <param name="IsClaimed">True once a device has taken this seat.</param>
/// <param name="FleetCount">Fleets that follow this seat.</param>
/// <param name="ShipCount">Ships across this seat's fleets.</param>
public sealed record MatchSeatDto(
    Guid ParticipantId,
    string DisplayName,
    string Role,
    bool IsClaimed,
    int FleetCount,
    int ShipCount);

/// <summary>Result of restoring a match from an exported snapshot.</summary>
/// <param name="MatchId">Newly issued match id for the restored match.</param>
/// <param name="JoinCode">Room code for the restored match.</param>
/// <param name="ReusedJoinCode">True when the exported room code was still free and was reused.</param>
/// <param name="RestoredPhase">Phase the restore landed in, which may differ from the export.</param>
/// <param name="LockedOrdersDropped">True when locked orders could not be restored and order entry reopened.</param>
/// <param name="Seats">Seats available to claim.</param>
/// <param name="Snapshot">Authoritative snapshot of the restored match.</param>
public sealed record MatchRestoredResponse(
    Guid MatchId,
    string JoinCode,
    bool ReusedJoinCode,
    string RestoredPhase,
    bool LockedOrdersDropped,
    IReadOnlyList<MatchSeatDto> Seats,
    MatchSnapshotDto Snapshot);

/// <summary>Match identity resolved from a room code.</summary>
/// <param name="MatchId">Match the room code refers to.</param>
/// <param name="JoinCode">Normalized room code.</param>
/// <param name="HasUnclaimedSeats">True when the match was restored and still has seats to claim.</param>
public sealed record MatchIdentityDto(Guid MatchId, string JoinCode, bool HasUnclaimedSeats);

/// <summary>One chronological battle log entry.</summary>
public sealed record MatchLogEntryDto(
    long Sequence,
    DateTimeOffset Timestamp,
    int TurnNumber,
    string Phase,
    string Category,
    string Message);
