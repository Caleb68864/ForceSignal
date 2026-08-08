using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Matches;

/// <content>
/// Writing a match down so a restart does not end the game.
///
/// This deliberately persists the *internal* state rather than the snapshot the wire carries, and
/// the difference matters. A snapshot is a projection built for players: it reissues ids when it is
/// restored, it omits participant tokens, and it omits the commitment hashes behind locked orders.
/// Recovering through it would therefore hand every device new ship ids, make everyone claim their
/// seat again, and throw away any order already locked - which is right for "restore last night's
/// backup" and quite wrong for "the server restarted mid-turn".
///
/// Persisting the internal state instead makes a restart invisible: tokens still work, ids are
/// unchanged, and a locked order stays locked. That last one works because the salt was never here
/// to lose - the server only ever holds the hash, and the salt lives in the browser that made it.
/// </content>
public sealed partial class InMemoryMatchService
{
    /// <summary>
    /// The format version written into every record.
    /// </summary>
    /// <remarks>
    /// A stored match that does not carry the current version is skipped at startup rather than
    /// guessed at. Losing a match to a format change is bad; silently loading one as though a field
    /// that has changed meaning still means what it did is worse.
    /// </remarks>
    private const int PersistenceFormatVersion = 1;

    private static readonly JsonSerializerOptions PersistenceJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new FiringArcJsonConverter() },
    };

    /// <summary>Writes one match to the store, if there is one.</summary>
    private void Persist(MatchState match)
    {
        if (ReferenceEquals(_store, NoMatchStore.Instance))
        {
            return;
        }

        _store.Save(match.Id, JsonSerializer.Serialize(ToPersisted(match), PersistenceJson));
    }

    /// <summary>
    /// Rebuilds every match the store is holding. Called once, from the constructor.
    /// </summary>
    /// <remarks>
    /// A record that cannot be read is skipped rather than thrown, and the rest still load. One
    /// unreadable match should cost that match, not every other game on the machine.
    /// </remarks>
    private void LoadPersistedMatches()
    {
        foreach (var stored in _store.LoadAll())
        {
            PersistedMatch? persisted;
            try
            {
                persisted = JsonSerializer.Deserialize<PersistedMatch>(stored.State, PersistenceJson);
            }
            catch (JsonException)
            {
                continue;
            }

            if (persisted is null || persisted.FormatVersion != PersistenceFormatVersion)
            {
                continue;
            }

            var match = FromPersisted(persisted);
            _matches[match.Id] = match;
            _joinCodes[match.JoinCode] = match.Id;
            IndexMatch(match);
            match.Persist = Persist;
        }
    }

    private static PersistedMatch ToPersisted(MatchState match) => new(
        PersistenceFormatVersion,
        match.Id,
        match.JoinCode,
        match.Name,
        match.Phase.ToString(),
        match.Rules.Layer.ToString(),
        match.TurnNumber,
        match.TableWidth,
        match.TableDepth,
        match.PointsLimit,
        match.Version,
        match.LastActivity,
        match.FiringShipId,
        match.FiringParticipantId,
        [.. match.ActivatedShipIds],
        [.. match.MovedFighterGroupIds],
        [.. match.RepairedShipIds],
        match.CarrierLaunchesThisTurn,
        match.CarrierRecoveriesThisTurn,
        match.PendingThresholds,
        [.. match.Participants.Select(p => new PersistedParticipant(
            p.Id, p.Token, p.DisplayName, p.Role, p.IsReady, p.IsConnected, p.OrdersComplete))],
        [.. match.Fleets.Select(f => new PersistedFleet(f.Id, f.OwnerParticipantId, f.Name, f.Faction, f.FleetColor))],
        [.. match.Ships.Select(ToPersisted)],
        [.. match.OrdnanceMarkers.Select(m => new PersistedMarker(
            m.Id, m.OwnerParticipantId, m.Name, m.MarkerType, m.SourceShipId, m.TargetShipId,
            m.PositionX, m.PositionY, m.Course, m.Speed, m.EnduranceRemaining, m.AttackDice, m.MaxRange, m.Status))],
        [.. match.Commitments.Values.Select(c => new PersistedCommitment(
            c.ShipId, c.OwnerParticipantId, c.CommitmentHash, c.IsRevealed, c.VerificationFailed, c.RevealedOrder, c.Result))],
        [.. match.FiringResults],
        [.. match.MatchLog.Select(l => new PersistedLogEntry(
            l.Sequence, l.Timestamp, l.TurnNumber, l.Phase, l.Category, l.Message))]);

    private static PersistedShip ToPersisted(ShipState ship) => new(
        ship.Id, ship.FleetId, ship.Name, ship.ClassName, ship.ThrustRating,
        ship.CurrentVelocity, ship.CurrentCourse, ship.PositionX, ship.PositionY,
        ship.HullMax, ship.HullDamage, ship.ArmorMax, ship.ArmorDamage,
        ship.FireControlMax, ship.FireControlDamage, ship.PointDefenseSystems,
        ship.FighterBays, ship.FighterBayDamage, ship.DamageControlParties,
        ship.DriveDamage, ship.WeaponDamage, ship.ScreenRating, ship.ScreenDamage,
        ship.NeedledFireControl, ship.NeedledDrives, ship.NeedledScreens, ship.NeedledBays,
        [.. ship.Weapons.Select(w => new PersistedWeapon(
            w.Id, w.Name, w.AttackDice, w.MaxRange, [.. w.Arcs], w.AmmoMax, w.AmmoUsed,
            w.ReloadTurns, w.Kind, w.IsDestroyed, w.IsNeedleKilled))],
        ship.IconKey, ship.FighterEnduranceMax, ship.FighterEnduranceUsed, ship.FighterMaxRange,
        ship.FighterStatus, ship.FighterRelaunchTurn, ship.FighterGroundedForGame,
        ship.HomeCarrierShipId, ship.PointsValue);

    private static MatchState FromPersisted(PersistedMatch persisted)
    {
        var seats = persisted.Participants.Select(p => ParticipantState.Restore(
            p.Id, p.Token, p.DisplayName, p.Role, p.IsReady, p.OrdersComplete)).ToList();

        var match = new MatchState(persisted.Id, persisted.JoinCode, persisted.Name, seats[0])
        {
            Phase = Enum.TryParse<MatchPhase>(persisted.Phase, out var phase) ? phase : MatchPhase.FleetSetup,
            Rules = RulesProfile.Parse(persisted.RulesLayer),
            TurnNumber = persisted.TurnNumber,
            TableWidth = persisted.TableWidth,
            TableDepth = persisted.TableDepth,
            PointsLimit = persisted.PointsLimit,
            LastActivity = persisted.LastActivity,
            FiringShipId = persisted.FiringShipId,
            FiringParticipantId = persisted.FiringParticipantId,
        };

        foreach (var seat in seats.Skip(1))
        {
            match.Participants.Add(seat);
        }

        // A connection is a live thing and cannot survive the process that held it, so everyone
        // comes back disconnected and the hub marks them present again as they reconnect.
        match.RestoreVersion(persisted.Version);

        foreach (var id in persisted.ActivatedShipIds)
        {
            match.ActivatedShipIds.Add(id);
        }

        foreach (var id in persisted.MovedFighterGroupIds)
        {
            match.MovedFighterGroupIds.Add(id);
        }

        foreach (var id in persisted.RepairedShipIds)
        {
            match.RepairedShipIds.Add(id);
        }

        foreach (var (carrierId, count) in persisted.CarrierLaunchesThisTurn)
        {
            match.CarrierLaunchesThisTurn[carrierId] = count;
        }

        foreach (var (carrierId, count) in persisted.CarrierRecoveriesThisTurn)
        {
            match.CarrierRecoveriesThisTurn[carrierId] = count;
        }

        foreach (var (shipId, hull) in persisted.PendingThresholds)
        {
            match.PendingThresholds[shipId] = hull;
        }

        foreach (var fleet in persisted.Fleets)
        {
            match.Fleets.Add(new FleetState(fleet.Id, fleet.OwnerParticipantId, fleet.Name, fleet.Faction, fleet.FleetColor));
        }

        foreach (var ship in persisted.Ships)
        {
            match.Ships.Add(FromPersisted(ship));
        }

        foreach (var marker in persisted.OrdnanceMarkers)
        {
            match.OrdnanceMarkers.Add(new OrdnanceMarkerState(
                marker.Id, marker.OwnerParticipantId, marker.Name, marker.MarkerType,
                marker.SourceShipId, marker.TargetShipId, marker.PositionX, marker.PositionY,
                marker.Course, marker.Speed, marker.EnduranceRemaining, marker.AttackDice,
                marker.MaxRange, marker.Status));
        }

        foreach (var commitment in persisted.Commitments)
        {
            match.Commitments[commitment.ShipId] = new OrderCommitmentState(
                commitment.ShipId, commitment.OwnerParticipantId, commitment.CommitmentHash,
                commitment.IsRevealed, commitment.VerificationFailed, commitment.RevealedOrder, commitment.Result);
        }

        foreach (var firing in persisted.FiringResults)
        {
            match.FiringResults.Add(firing);
        }

        foreach (var entry in persisted.MatchLog)
        {
            // Replayed through the restoring path so the sequence counter lands past the highest
            // number already used, rather than starting again and repeating one.
            match.AddRestoredLog(new MatchLogEntryState(
                entry.Sequence, entry.Timestamp, entry.TurnNumber, entry.Phase, entry.Category, entry.Message));
        }

        return match;
    }

    private static ShipState FromPersisted(PersistedShip ship)
    {
        var restored = new ShipState(
            ship.Id, ship.FleetId, ship.Name, ship.ClassName, ship.ThrustRating,
            ship.CurrentVelocity, ship.CurrentCourse, ship.HullMax, ship.ArmorMax,
            ship.PositionX, ship.PositionY, ship.ScreenRating,
            [.. ship.Weapons.Select(w => new WeaponMountState(
                w.Id, w.Name, w.AttackDice, w.MaxRange, w.Arcs, w.AmmoMax, w.AmmoUsed, w.ReloadTurns, w.Kind)
            {
                IsDestroyed = w.IsDestroyed,
                IsNeedleKilled = w.IsNeedleKilled,
            })],
            ship.IconKey)
        {
            HullDamage = ship.HullDamage,
            ArmorDamage = ship.ArmorDamage,
            FireControlMax = ship.FireControlMax,
            FireControlDamage = ship.FireControlDamage,
            PointDefenseSystems = ship.PointDefenseSystems,
            FighterBays = ship.FighterBays,
            FighterBayDamage = ship.FighterBayDamage,
            DamageControlParties = ship.DamageControlParties,
            DriveDamage = ship.DriveDamage,
            WeaponDamage = ship.WeaponDamage,
            ScreenDamage = ship.ScreenDamage,
            NeedledFireControl = ship.NeedledFireControl,
            NeedledDrives = ship.NeedledDrives,
            NeedledScreens = ship.NeedledScreens,
            NeedledBays = ship.NeedledBays,
            FighterEnduranceMax = ship.FighterEnduranceMax,
            FighterEnduranceUsed = ship.FighterEnduranceUsed,
            FighterMaxRange = ship.FighterMaxRange,
            FighterStatus = ship.FighterStatus,
            FighterRelaunchTurn = ship.FighterRelaunchTurn,
            FighterGroundedForGame = ship.FighterGroundedForGame,
            HomeCarrierShipId = ship.HomeCarrierShipId,
            PointsValue = ship.PointsValue,
        };

        return restored;
    }

    private sealed record PersistedMatch(
        int FormatVersion,
        Guid Id,
        string JoinCode,
        string Name,
        string Phase,
        string RulesLayer,
        int TurnNumber,
        int TableWidth,
        int TableDepth,
        int PointsLimit,
        long Version,
        DateTimeOffset LastActivity,
        Guid? FiringShipId,
        Guid? FiringParticipantId,
        IReadOnlyList<Guid> ActivatedShipIds,
        IReadOnlyList<Guid> MovedFighterGroupIds,
        IReadOnlyList<Guid> RepairedShipIds,
        IReadOnlyDictionary<Guid, int> CarrierLaunchesThisTurn,
        IReadOnlyDictionary<Guid, int> CarrierRecoveriesThisTurn,
        IReadOnlyDictionary<Guid, int> PendingThresholds,
        IReadOnlyList<PersistedParticipant> Participants,
        IReadOnlyList<PersistedFleet> Fleets,
        IReadOnlyList<PersistedShip> Ships,
        IReadOnlyList<PersistedMarker> OrdnanceMarkers,
        IReadOnlyList<PersistedCommitment> Commitments,
        IReadOnlyList<FiringResultState> FiringResults,
        IReadOnlyList<PersistedLogEntry> MatchLog);

    private sealed record PersistedParticipant(
        Guid Id, string Token, string DisplayName, string Role, bool IsReady, bool IsConnected, bool OrdersComplete);

    private sealed record PersistedFleet(Guid Id, Guid OwnerParticipantId, string Name, string? Faction, string FleetColor);

    private sealed record PersistedShip(
        Guid Id, Guid FleetId, string Name, string? ClassName, int ThrustRating,
        int CurrentVelocity, int CurrentCourse, decimal PositionX, decimal PositionY,
        int HullMax, int HullDamage, int ArmorMax, int ArmorDamage,
        int FireControlMax, int FireControlDamage, int PointDefenseSystems,
        int FighterBays, int FighterBayDamage, int DamageControlParties,
        int DriveDamage, int WeaponDamage, int ScreenRating, int ScreenDamage,
        int NeedledFireControl, int NeedledDrives, int NeedledScreens, int NeedledBays,
        IReadOnlyList<PersistedWeapon> Weapons,
        string IconKey, int FighterEnduranceMax, int FighterEnduranceUsed, int FighterMaxRange,
        string FighterStatus, int FighterRelaunchTurn, bool FighterGroundedForGame,
        Guid? HomeCarrierShipId, int PointsValue);

    private sealed record PersistedWeapon(
        Guid Id, string Name, int AttackDice, int MaxRange, IReadOnlyList<FiringArc> Arcs,
        int AmmoMax, int AmmoUsed, int ReloadTurns, WeaponKind Kind, bool IsDestroyed, bool IsNeedleKilled);

    private sealed record PersistedMarker(
        Guid Id, Guid OwnerParticipantId, string Name, string MarkerType,
        Guid? SourceShipId, Guid? TargetShipId, decimal PositionX, decimal PositionY,
        int Course, int Speed, int EnduranceRemaining, int AttackDice, int MaxRange, string Status);

    private sealed record PersistedCommitment(
        Guid ShipId, Guid OwnerParticipantId, string CommitmentHash,
        bool IsRevealed, bool? VerificationFailed, MovementOrder? RevealedOrder, MovementResult? Result);

    private sealed record PersistedLogEntry(
        long Sequence, DateTimeOffset Timestamp, int TurnNumber, string Phase, string Category, string Message);
}
