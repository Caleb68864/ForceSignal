using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Matches;

/// <content>
/// The state a live match is held in.
///
/// These are deliberately private to the service and separate from the contracts in
/// <c>ForceSignal.Contracts</c>: the wire shape is a projection of this, not the same thing, so the
/// two are free to differ where a snapshot should not carry something the server tracks - the
/// commitment salts being the obvious case.
/// </content>
public sealed partial class InMemoryMatchService
{
    private enum MatchPhase
    {
        FleetSetup,
        OrderEntry,
        OrdersLocked,
        Reveal,
        Movement,
        Firing,
    }

    private sealed class MatchState(Guid id, string joinCode, string name, ParticipantState owner)
    {
        public Guid Id { get; } = id;
        public string JoinCode { get; } = joinCode;
        public string Name { get; } = name;
        public MatchPhase Phase { get; set; } = MatchPhase.FleetSetup;

        /// <summary>
        /// The numbers this match is played against. Blank until the players supply them, because
        /// this app ships none of its own.
        /// </summary>
        public RulesProfile Rules { get; set; } = RulesProfile.Empty;

        /// <summary>The ship part-way through its fire, if any. Its threshold checks are still owed.</summary>
        public Guid? FiringShipId { get; set; }

        /// <summary>Whose turn it is to pick a ship and fire it. Null outside the firing phase.</summary>
        public Guid? FiringParticipantId { get; set; }

        /// <summary>Ships that have already taken their turn to fire this phase.</summary>
        public HashSet<Guid> ActivatedShipIds { get; } = [];

        /// <summary>Fighter groups that have already flown this turn.</summary>
        public HashSet<Guid> MovedFighterGroupIds { get; } = [];

        /// <summary>Groups each carrier has put up this turn, by carrier id.</summary>
        public Dictionary<Guid, int> CarrierLaunchesThisTurn { get; } = [];

        /// <summary>
        /// Groups each carrier has brought aboard this turn, by carrier id. Counted apart from
        /// launches because the Fleet Book gives the two separate allowances; the older layer adds
        /// the two together against one budget.
        /// </summary>
        public Dictionary<Guid, int> CarrierRecoveriesThisTurn { get; } = [];

        /// <summary>Ships whose damage control has already worked this turn.</summary>
        public HashSet<Guid> RepairedShipIds { get; } = [];

        /// <summary>Hull damage each target had before the firing ship opened up, by target id.</summary>
        public Dictionary<Guid, int> PendingThresholds { get; } = [];
        public int TurnNumber { get; set; } = 1;
        public int TableWidth { get; set; } = 72;
        public int TableDepth { get; set; } = 48;
        public int PointsLimit { get; set; }

        /// <summary>When this match was last read or written. Drives idle eviction.</summary>
        public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;
        public List<ParticipantState> Participants { get; } = [owner];
        public List<FleetState> Fleets { get; } = [];
        public List<ShipState> Ships { get; } = [];
        public List<OrdnanceMarkerState> OrdnanceMarkers { get; } = [];
        public Dictionary<Guid, OrderCommitmentState> Commitments { get; } = [];
        public List<FiringResultState> FiringResults { get; } = [];
        public List<MatchLogEntryState> MatchLog { get; } = [];
        public long Version { get; private set; } = 1;

        /// <summary>
        /// Called after every change, to write the match down.
        /// </summary>
        /// <remarks>
        /// Hung here rather than called from each of the service's two dozen mutations, because
        /// every one of them already ends by touching the match. One hook cannot be forgotten; two
        /// dozen call sites can.
        /// </remarks>
        public Action<MatchState>? Persist { get; set; }

        public void Touch(string _)
        {
            Version++;
            Persist?.Invoke(this);
        }

        /// <summary>
        /// Puts the version back to what it was, when rebuilding a match from storage.
        /// </summary>
        /// <remarks>
        /// The clients' guard against an out-of-order snapshot compares versions, so a restarted
        /// server that began again at one would have every client ignore it until the count caught
        /// up - the board would simply stop moving.
        /// </remarks>
        public void RestoreVersion(long version) => Version = version;

        /// <summary>
        /// Next log sequence number. Kept separately from the list's length because the list is
        /// trimmed once it reaches its ceiling, and a sequence that restarted would make two
        /// different events in one game share a number.
        /// </summary>
        private long _nextLogSequence = 1;

        public void AddLog(string category, string phase, string message)
        {
            MatchLog.Add(new MatchLogEntryState(_nextLogSequence++, DateTimeOffset.UtcNow, TurnNumber, phase, category, message));
            TrimLog();
        }

        public void AddRestoredLog(MatchLogEntryState entry)
        {
            MatchLog.Add(entry);
            _nextLogSequence = Math.Max(_nextLogSequence, entry.Sequence + 1);
            TrimLog();
        }

        /// <summary>
        /// Drops the oldest entries once the log passes its ceiling. The whole log rides inside
        /// every snapshot, so letting it grow forever would make the last turn of a long game
        /// noticeably slower than the first.
        /// </summary>
        private void TrimLog()
        {
            if (MatchLog.Count > MaxLogEntriesPerMatch)
            {
                MatchLog.RemoveRange(0, MatchLog.Count - MaxLogEntriesPerMatch);
            }
        }
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

        /// <summary>
        /// Rebuilds a seat from storage, token and all.
        /// </summary>
        /// <remarks>
        /// Keeping the token is the whole point: it is what makes a restart invisible to the device
        /// holding it, rather than making everyone claim their seat again. Connection state is not
        /// restored - a connection cannot outlive the process that held it, so everyone comes back
        /// disconnected and the hub marks them present as they reconnect.
        /// </remarks>
        public static ParticipantState Restore(
            Guid id, string token, string displayName, string role, bool isReady, bool ordersComplete) => new()
        {
            Id = id,
            Token = token,
            DisplayName = displayName,
            Role = role,
            IsReady = isReady,
            OrdersComplete = ordersComplete,
        };

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
        public int PointDefenseSystems { get; set; }
        public int FighterBays { get; set; }
        public int DamageControlParties { get; set; }
        public int DriveDamage { get; set; }
        public int WeaponDamage { get; set; }
        /// <summary>Screen levels the ship was built with. Losses are counted separately.</summary>
        public int ScreenRating { get; set; } = screenRating;
        public int ScreenDamage { get; set; }
        public int FighterBayDamage { get; set; }

        /// <summary>
        /// Losses a needle beam inflicted, which damage control can never undo. Counted per system so
        /// a ship that lost one firecon to a threshold and another to a needle can repair exactly one.
        /// </summary>
        public int NeedledFireControl { get; set; }
        public int NeedledDrives { get; set; }
        public int NeedledScreens { get; set; }
        public int NeedledBays { get; set; }
        public List<WeaponMountState> Weapons { get; } = [.. weapons];
        public string IconKey { get; set; } = iconKey;
        public int FighterEnduranceMax { get; set; }
        public int FighterEnduranceUsed { get; set; }
        public int FighterMaxRange { get; set; }
        public string FighterStatus { get; set; } = "Docked";

        /// <summary>
        /// Earliest turn this group may be launched again, after a turnaround roll held it on the
        /// deck. Zero when nothing is holding it.
        /// </summary>
        public int FighterRelaunchTurn { get; set; }

        /// <summary>True when a turnaround roll wrote this group off for the rest of the game.</summary>
        public bool FighterGroundedForGame { get; set; }
        public Guid? HomeCarrierShipId { get; set; }
        public int PointsValue { get; set; }
    }
    private sealed class WeaponMountState(Guid id, string name, int attackDice, int maxRange, IReadOnlyList<FiringArc> arcs, int ammoMax, int ammoUsed, int reloadTurns, WeaponKind kind = WeaponKind.Beam)
    {
        public Guid Id { get; } = id;
        public string Name { get; } = name;
        public int AttackDice { get; } = attackDice;
        public int MaxRange { get; } = maxRange;
        public IReadOnlyList<FiringArc> Arcs { get; } = arcs;
        public bool IsDestroyed { get; set; }
        public bool IsNeedleKilled { get; set; }
        public WeaponKind Kind { get; } = kind;
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
        IReadOnlyList<int> DiceRolls,
        WeaponKind WeaponKind,
        int? ToHitNumber,
        bool? IsHit,
        decimal MapRange,
        bool RangeDisagreed);
    private sealed record MatchLogEntryState(long Sequence, DateTimeOffset Timestamp, int TurnNumber, string Phase, string Category, string Message);
    private sealed record OrderCommitmentState(
        Guid ShipId,
        Guid OwnerParticipantId,
        string CommitmentHash,
        bool IsRevealed,
        bool? VerificationFailed,
        MovementOrder? RevealedOrder,
        MovementResult? Result);
    private sealed record DamageSnapshot(int Hull, int Armor, int FireControl, int Drive, int Weapons);
}
