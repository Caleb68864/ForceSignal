# Match Restore Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Restore a full match — phase, turn, damage, positions, ordnance, log — from an exported snapshot file, with each player claiming their original seat to get their fleets back.

**Architecture:** One atomic server-side import (`RestoreMatch`) rebuilds `MatchState` directly under the existing lock, reusing every existing normalizer so a hand-edited file cannot inject illegal state. Participants come back as token-less *seats*; claiming a seat issues a fresh token, and fleets follow the seat. Order-phase exports fall back to `OrderEntry` because commitment salts are never exported; `Movement` and `Firing` exports rebuild commitments from the snapshot's public `revealedOrders` + `movementResults`.

**Tech Stack:** .NET 10 minimal API, xUnit, React 19 + TypeScript (single-file `main.tsx`), SignalR.

**Design:** `docs/plans/2026-07-31-match-restore-design.md`

**Working tree:** implement on `master` (clean at plan time; this repo has no worktree convention set up). Commit per task.

---

## Context an engineer new to this repo needs

- `src/ForceSignal.Application/Matches/InMemoryMatchService.cs` holds *everything* server-side:
  the `IMatchService` interface, the implementation, and private nested state classes
  (`MatchState`, `ParticipantState`, `FleetState`, `ShipState`, `WeaponMountState`,
  `OrdnanceMarkerState`, `OrderCommitmentState`, `FiringResultState`, `MatchLogEntryState`) plus
  all normalizers. Nested private classes are accessible from the containing class, so restore
  can build them directly.
- All mutations happen inside `lock (_gate)`. `match.Touch("Reason")` bumps `Version`.
- `match.AddLog(category, phase, message)` stamps `DateTimeOffset.UtcNow` and
  `MatchLog.Count + 1` as the sequence. Restoring original timestamps needs a separate path —
  Task 3 adds `AddRestoredLog`.
- `ToSnapshot` derives `RevealedOrders` from commitments where `RevealedOrder is not null`, and
  `MovementResults` from commitments where `Result is not null`. That is why rebuilding
  commitments is how trails come back.
- `FindParticipant(match, token)` authenticates by `p.Token == token`. Token-less seats make an
  empty-string token dangerous — Task 1 closes that.
- Endpoints live in `src/ForceSignal.Api/Program.cs`; each mutating one calls
  `NotifySnapshotChanged(hub, snapshot, "Reason")`.
- Tests: `dotnet test ForceSignal.slnx` runs all 36. Filter with
  `dotnet test tests/ForceSignal.Application.Tests --filter FullyQualifiedName~Restore`.
- **Stop any locally running API before building** (`dotnet run` locks the output DLLs and the
  build fails with MSB3027). Check with
  `Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*ForceSignal.Api*" }`.
- Web work: `cd src/ForceSignal.Web && npm run build` runs `tsc` then vite. `tsc` has
  `noUnusedLocals`, so an unused helper fails the build.

---

## Task 1: Make participant seats claimable

A restored participant has no token until someone claims it. Today `ParticipantState.Token` is
`required ... { get; init; }`, and an empty token would authenticate against `FindParticipant`.

**Files:**
- Modify: `src/ForceSignal.Application/Matches/InMemoryMatchService.cs` (`ParticipantState`, `FindParticipant`)
- Test: `tests/ForceSignal.Application.Tests/InMemoryMatchServiceRestoreTests.cs` (create)

**Step 1: Write the failing test**

```csharp
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

public sealed class InMemoryMatchServiceRestoreTests
{
    [Fact]
    public void EmptyToken_NeverAuthenticates()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Seat Guard"));

        Assert.False(service.IsMatchParticipant(owner.MatchId, string.Empty));
        Assert.False(service.IsMatchParticipant(owner.MatchId, "   "));
        Assert.Throws<UnauthorizedAccessException>(() =>
            service.SetReady(owner.MatchId, string.Empty, true));
    }
}
```

**Step 2: Run it to confirm the current behaviour**

Run: `dotnet test tests/ForceSignal.Application.Tests --filter FullyQualifiedName~EmptyToken_NeverAuthenticates`
Expected: PASS today (no seat exists yet) — this test is the guard that must still hold after
the change. Keep it; it fails later if `Token` becomes loosely compared.

**Step 3: Make the token claimable**

Replace `ParticipantState`'s token members:

```csharp
        public required string Token { get; set; }
        public bool IsClaimed => !string.IsNullOrWhiteSpace(Token);
```

Add a seat factory beside `Create`:

```csharp
        public static ParticipantState CreateSeat(Guid id, string displayName, string role, bool isReady) => new()
        {
            Id = id,
            Token = string.Empty,
            DisplayName = displayName,
            Role = role,
            IsReady = isReady
        };

        public string Claim() => Token = _commitmentSafeToken();
```

Harden lookup:

```csharp
    private static ParticipantState FindParticipant(MatchState match, string token) =>
        string.IsNullOrWhiteSpace(token)
            ? throw new UnauthorizedAccessException("Participant token is invalid.")
            : match.Participants.SingleOrDefault(p => p.IsClaimed && p.Token == token)
                ?? throw new UnauthorizedAccessException("Participant token is invalid.");
```

And in `IsMatchParticipant`, change the predicate to `p.IsClaimed && p.Token == participantToken`.

**Step 4: Run the full suite**

Run: `dotnet test ForceSignal.slnx`
Expected: all pass (37 now).

**Step 5: Commit**

```bash
git add src/ForceSignal.Application/Matches/InMemoryMatchService.cs tests/ForceSignal.Application.Tests/InMemoryMatchServiceRestoreTests.cs
git commit -m "feat: make participant seats claimable and reject blank tokens"
```

---

## Task 2: Restore contracts

**Files:**
- Modify: `src/ForceSignal.Contracts/Matches/MatchContracts.cs` (append)

**Step 1: Add the contracts**

```csharp
/// <summary>Claims an unclaimed seat in a restored match.</summary>
/// <param name="DisplayName">Seat display name, confirmed by the caller.</param>
public sealed record ClaimSeatRequest(string DisplayName);

/// <summary>An unclaimed or claimed seat in a restored match.</summary>
public sealed record MatchSeatDto(
    Guid ParticipantId,
    string DisplayName,
    string Role,
    bool IsClaimed,
    int FleetCount,
    int ShipCount);

/// <summary>Result of restoring a match from an exported snapshot.</summary>
public sealed record MatchRestoredResponse(
    Guid MatchId,
    string JoinCode,
    bool ReusedJoinCode,
    string RestoredPhase,
    bool LockedOrdersDropped,
    IReadOnlyList<MatchSeatDto> Seats,
    MatchSnapshotDto Snapshot);
```

**Step 2: Build**

Run: `dotnet build ForceSignal.slnx`
Expected: `Build succeeded. 0 Warning(s)` — XML docs are required on public members here
(`GenerateDocumentationFile` is on), so a missing `<summary>` shows up as a warning.

**Step 3: Commit**

```bash
git add src/ForceSignal.Contracts/Matches/MatchContracts.cs
git commit -m "feat: add restore and seat-claim contracts"
```

---

## Task 3: RestoreMatch — table, fleets, ships, log

**Files:**
- Modify: `src/ForceSignal.Application/Matches/InMemoryMatchService.cs`
- Test: `tests/ForceSignal.Application.Tests/InMemoryMatchServiceRestoreTests.cs`

**Step 1: Write the failing test**

```csharp
    [Fact]
    public void RestoreMatch_FromFleetSetupExport_RebuildsTableFleetsShipsAndLog()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Restore Source", 96, 72));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue Watch", "Test", "#f5c766")).Fleets.Single();
        service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4,
            StartX: 20, StartY: 24, ScreenRating: 1,
            Weapons: [new WeaponMountDto(Guid.NewGuid(), "Class-3 Beam", 3, 24, FiringArc.All, AmmoMax: 2, AmmoUsed: 1)]));
        var exported = service.GetSnapshot(owner.MatchId);

        var restored = new InMemoryMatchService().RestoreMatch(exported, savedAt: DateTimeOffset.UtcNow);

        Assert.NotEqual(exported.MatchId, restored.MatchId);
        Assert.Equal(exported.JoinCode, restored.JoinCode);
        Assert.True(restored.ReusedJoinCode);
        Assert.Equal("FleetSetup", restored.RestoredPhase);
        Assert.Equal(96, restored.Snapshot.TableWidth);
        Assert.Equal(72, restored.Snapshot.TableDepth);

        var ship = Assert.Single(restored.Snapshot.Ships);
        Assert.Equal("Valiant", ship.Name);
        Assert.Equal(exported.Ships.Single().Id, ship.Id);
        Assert.Equal(20, ship.PositionX);
        Assert.Equal(1, ship.Weapons.Single().AmmoUsed);
        Assert.Equal("#f5c766", restored.Snapshot.Fleets.Single().FleetColor);
        Assert.Equal(exported.Fleets.Single().Id, restored.Snapshot.Fleets.Single().Id);

        // Original log carries over, plus one restore entry.
        Assert.Equal(exported.MatchLog.Count + 1, restored.Snapshot.MatchLog.Count);
        Assert.Contains(restored.Snapshot.MatchLog, e => e.Category == "Session" && e.Message.Contains("restored", StringComparison.OrdinalIgnoreCase));

        // Seats come back unclaimed.
        var seat = Assert.Single(restored.Seats);
        Assert.False(seat.IsClaimed);
        Assert.Equal("Owner", seat.Role);
        Assert.Equal(1, seat.FleetCount);
        Assert.Equal(1, seat.ShipCount);
    }
```

**Step 2: Run to verify it fails**

Run: `dotnet test tests/ForceSignal.Application.Tests --filter FullyQualifiedName~RestoreMatch_FromFleetSetupExport`
Expected: FAIL — `RestoreMatch` does not exist (compile error CS1061).

**Step 3: Add the interface member**

In `IMatchService`, after `SetParticipantConnection`:

```csharp
    /// <summary>Rebuilds a match from an exported snapshot. Participants return as unclaimed seats.</summary>
    MatchRestoredResponse RestoreMatch(MatchSnapshotDto snapshot, DateTimeOffset? savedAt);
```

**Step 4: Implement**

Add to `MatchState` so restored log entries keep their original stamps:

```csharp
        public void AddRestoredLog(MatchLogEntryState entry) => MatchLog.Add(entry);
```

Add the implementation (place after `SetParticipantConnection`):

```csharp
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
            throw new InvalidOperationException($"Snapshot uses unknown rules profile '{snapshot.RulesProfileKey}'.");
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
                TurnNumber = Math.Max(1, snapshot.TurnNumber)
            };
            foreach (var seat in seats.Skip(1))
            {
                match.Participants.Add(seat);
            }

            var seatIds = seats.Select(s => s.Id).ToHashSet();
            foreach (var fleet in snapshot.Fleets ?? [])
            {
                var ownerId = seatIds.Contains(fleet.OwnerParticipantId) ? fleet.OwnerParticipantId : seats[0].Id;
                match.Fleets.Add(new FleetState(
                    fleet.Id == Guid.Empty ? Guid.NewGuid() : fleet.Id,
                    ownerId,
                    NormalizeText(fleet.Name, "Fleet"),
                    NormalizeOptionalText(fleet.Faction),
                    NormalizeFleetColor(fleet.FleetColor)));
            }

            var fleetIds = match.Fleets.Select(f => f.Id).ToHashSet();
            foreach (var ship in snapshot.Ships)
            {
                if (!fleetIds.Contains(ship.FleetId))
                {
                    throw new InvalidOperationException($"Ship '{ship.Name}' references a fleet that is not in the snapshot.");
                }

                var iconKey = NormalizeIconKey(ship.IconKey, ship.ClassName);
                var fighterEnduranceMax = NormalizeFighterEnduranceMax(ship.FighterEnduranceMax, iconKey, ship.ClassName);
                var restoredShip = new ShipState(
                    ship.Id == Guid.Empty ? Guid.NewGuid() : ship.Id,
                    ship.FleetId,
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
                    NormalizeWeapons(ship.Weapons),
                    iconKey)
                {
                    FighterEnduranceMax = fighterEnduranceMax,
                    FighterEnduranceUsed = NormalizeFighterEnduranceUsed(ship.FighterEnduranceUsed, fighterEnduranceMax),
                    FighterMaxRange = NormalizeFighterMaxRange(ship.FighterMaxRange, iconKey, ship.ClassName),
                    FighterStatus = NormalizeFighterStatus(ship.FighterStatus, iconKey, ship.ClassName),
                };
                restoredShip.HullDamage = ClampDamage(ship.HullDamage, restoredShip.HullMax);
                restoredShip.ArmorDamage = ClampDamage(ship.ArmorDamage, restoredShip.ArmorMax);
                restoredShip.FireControlDamage = ClampDamage(ship.FireControlDamage, 6);
                restoredShip.DriveDamage = ClampDamage(ship.DriveDamage, restoredShip.ThrustRating);
                restoredShip.WeaponDamage = ClampDamage(ship.WeaponDamage, 12);
                match.Ships.Add(restoredShip);
            }

            // Carrier links resolve only once every ship exists.
            foreach (var ship in snapshot.Ships.Where(s => s.HomeCarrierShipId is not null))
            {
                var restoredShip = match.Ships.SingleOrDefault(s => s.Id == ship.Id);
                if (restoredShip is not null)
                {
                    restoredShip.HomeCarrierShipId = ValidateCarrierId(match, ship.HomeCarrierShipId);
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
                    marker.SourceShipId,
                    marker.TargetShipId,
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

            var (phase, lockedOrdersDropped) = RestorePhase(snapshot.Phase);
            match.Phase = phase;

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

    private static (MatchPhase Phase, bool LockedOrdersDropped) RestorePhase(string? exported) => exported switch
    {
        nameof(MatchPhase.FleetSetup) => (MatchPhase.FleetSetup, false),
        nameof(MatchPhase.Movement) => (MatchPhase.Movement, false),
        nameof(MatchPhase.Firing) => (MatchPhase.Firing, false),
        // Commitment salts are never exported, so locked orders cannot come back.
        nameof(MatchPhase.OrdersLocked) or nameof(MatchPhase.Reveal) => (MatchPhase.OrderEntry, true),
        _ => (MatchPhase.OrderEntry, false),
    };

    private static IReadOnlyList<MatchSeatDto> BuildSeats(MatchState match) =>
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
```

`MatchState.TurnNumber` already has a setter. Confirm `Phase` does too (it does).

**Step 5: Run the test**

Run: `dotnet test tests/ForceSignal.Application.Tests --filter FullyQualifiedName~RestoreMatch_FromFleetSetupExport`
Expected: PASS.

**Step 6: Commit**

```bash
git add src/ForceSignal.Application/Matches/InMemoryMatchService.cs tests/ForceSignal.Application.Tests/InMemoryMatchServiceRestoreTests.cs
git commit -m "feat: restore match table, fleets, ships, ordnance and log from a snapshot"
```

---

## Task 4: Restore mid-battle phases and spent weapons

**Files:**
- Modify: `src/ForceSignal.Application/Matches/InMemoryMatchService.cs`
- Test: `tests/ForceSignal.Application.Tests/InMemoryMatchServiceRestoreTests.cs`

**Step 1: Write the failing tests**

```csharp
    [Fact]
    public void RestoreMatch_FromFiringExport_KeepsPhaseSpentWeaponsAndTrails()
    {
        var source = new InMemoryMatchService();
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Firing Source"));
        var opponent = source.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
        var blueFleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var redFleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null)).Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        var weaponId = Guid.NewGuid();
        var attacker = source.CreateShip(blueFleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24,
            Weapons: [new WeaponMountDto(weaponId, "Class-3 Beam", 3, 24, FiringArc.All)])).Ships.Single(s => s.Name == "Valiant");
        var target = source.CreateShip(redFleet.Id, new CreateShipRequest(
            opponent.ParticipantToken, "Crimson", "Destroyer", 4, 6, 9, 10, 1, StartX: 32, StartY: 24)).Ships.Single(s => s.Name == "Crimson");
        source.SetReady(owner.MatchId, owner.ParticipantToken, true);
        source.SetReady(owner.MatchId, opponent.ParticipantToken, true);
        var order = new MovementOrder(1, 1, TurnDirection.Port, [new TurnManeuver(TurnDirection.Port, 1)]);
        var drift = new MovementOrder(0, 0, TurnDirection.None);
        source.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, attacker.Id, order, "b"));
        source.CommitOrder(owner.MatchId, new CommitOrderRequest(opponent.ParticipantToken, target.Id, drift, "r"));
        source.RevealOrder(owner.MatchId, new RevealOrderRequest(owner.ParticipantToken, attacker.Id, order, "b"));
        source.RevealOrder(owner.MatchId, new RevealOrderRequest(opponent.ParticipantToken, target.Id, drift, "r"));
        source.AdvanceTurn(owner.MatchId, owner.ParticipantToken);
        source.FireWeapon(owner.MatchId, new FireWeaponRequest(owner.ParticipantToken, attacker.Id, target.Id, weaponId, 8, FiringArc.Fore));
        var exported = source.GetSnapshot(owner.MatchId);

        var service = new InMemoryMatchService();
        var restored = service.RestoreMatch(exported, DateTimeOffset.UtcNow);

        Assert.Equal("Firing", restored.RestoredPhase);
        Assert.False(restored.LockedOrdersDropped);
        Assert.Equal(exported.TurnNumber, restored.Snapshot.TurnNumber);
        // Damage survived.
        var restoredTarget = restored.Snapshot.Ships.Single(s => s.Name == "Crimson");
        Assert.Equal(exported.Ships.Single(s => s.Name == "Crimson").HullDamage, restoredTarget.HullDamage);
        Assert.Equal(exported.Ships.Single(s => s.Name == "Crimson").ArmorDamage, restoredTarget.ArmorDamage);
        // Movement trails survived.
        Assert.Equal(exported.MovementResults.Count, restored.Snapshot.MovementResults.Count);
        // The spent weapon stays spent: firing it again must be refused.
        var seat = restored.Seats.Single(s => s.Role == "Owner");
        var session = service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName));
        var error = Assert.Throws<InvalidOperationException>(() =>
            service.FireWeapon(restored.MatchId, new FireWeaponRequest(session.ParticipantToken, attacker.Id, target.Id, weaponId, 8, FiringArc.Fore)));
        Assert.Contains("already fired", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestoreMatch_FromOrdersLockedExport_FallsBackToOrderEntry()
    {
        var source = new InMemoryMatchService();
        var owner = source.CreateMatch(new CreateMatchRequest("Solo", "Locked Source"));
        var fleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        var ship = source.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Lone Star", "Cruiser", 4, 6, 3, 12, 2, StartX: 20, StartY: 24)).Ships.Single();
        source.SetReady(owner.MatchId, owner.ParticipantToken, true);
        var order = new MovementOrder(1, 0, TurnDirection.None);
        var locked = source.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, ship.Id, order, "s"));
        Assert.Equal("OrdersLocked", locked.Phase);

        var restored = new InMemoryMatchService().RestoreMatch(source.GetSnapshot(owner.MatchId), null);

        Assert.Equal("OrderEntry", restored.RestoredPhase);
        Assert.True(restored.LockedOrdersDropped);
        Assert.All(restored.Snapshot.OrderStatuses, status => Assert.False(status.IsCommitted));
        Assert.Contains(restored.Snapshot.MatchLog, e => e.Message.Contains("Locked orders could not be restored", StringComparison.Ordinal));
    }
```

**Step 2: Run to verify they fail**

Run: `dotnet test tests/ForceSignal.Application.Tests --filter FullyQualifiedName~RestoreMatch_From`
Expected: FAIL — `ClaimSeat` missing (compile error), and firing results are not restored.

**Step 3: Restore firing results and commitments**

Inside `RestoreMatch`, after the log loop and before `RestorePhase`, add:

```csharp
            foreach (var firing in snapshot.FiringResults ?? [])
            {
                match.FiringResults.Add(new FiringResultState(
                    firing.AttackerShipId,
                    firing.TargetShipId,
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
                    firing.HullDamageApplied));
            }
```

Then, after `match.Phase = phase;`, rebuild commitments for the two phases where orders are
already public. `revealedOrders` and `movementResults` are post-reveal data, so this loses
nothing secret and lets `AdvanceTurn` behave correctly — from `Movement` it applies the
restored orders exactly once, and from `Firing` it clears them.

```csharp
            if (phase is MatchPhase.Movement or MatchPhase.Firing)
            {
                foreach (var revealed in snapshot.RevealedOrders ?? [])
                {
                    var ship = match.Ships.SingleOrDefault(s => s.Id == revealed.ShipId);
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
```

**Step 4: Add ClaimSeat**

Interface member:

```csharp
    /// <summary>Claims an unclaimed seat in a restored match and issues a participant token.</summary>
    MatchJoinedResponse ClaimSeat(Guid matchId, Guid participantId, ClaimSeatRequest request);
```

Implementation:

```csharp
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
```

**Step 5: Run the tests**

Run: `dotnet test tests/ForceSignal.Application.Tests --filter FullyQualifiedName~Restore`
Expected: PASS.

**Step 6: Commit**

```bash
git add src/ForceSignal.Application/Matches/InMemoryMatchService.cs tests/ForceSignal.Application.Tests/InMemoryMatchServiceRestoreTests.cs
git commit -m "feat: restore mid-battle phases, spent weapons and seat claiming"
```

---

## Task 5: Seat ownership and double-claim guards

**Files:**
- Test only: `tests/ForceSignal.Application.Tests/InMemoryMatchServiceRestoreTests.cs`

**Step 1: Write the tests**

```csharp
    [Fact]
    public void ClaimedSeat_CommandsOnlyItsOwnFleets_AndCannotBeClaimedTwice()
    {
        var source = new InMemoryMatchService();
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Ownership Source"));
        var opponent = source.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
        var blueFleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var redFleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null)).Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        var blueShip = source.CreateShip(blueFleet.Id, new CreateShipRequest(owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24)).Ships.Single(s => s.Name == "Valiant");
        var redShip = source.CreateShip(redFleet.Id, new CreateShipRequest(opponent.ParticipantToken, "Crimson", "Destroyer", 4, 0, 9, 10, 1, StartX: 32, StartY: 24)).Ships.Single(s => s.Name == "Crimson");

        var service = new InMemoryMatchService();
        var restored = service.RestoreMatch(source.GetSnapshot(owner.MatchId), null);
        var blueSeat = restored.Seats.Single(s => s.DisplayName == "Blue");
        var blueSession = service.ClaimSeat(restored.MatchId, blueSeat.ParticipantId, new ClaimSeatRequest("Blue"));

        // Claiming the same seat again is refused.
        var doubleClaim = Assert.Throws<InvalidOperationException>(() =>
            service.ClaimSeat(restored.MatchId, blueSeat.ParticipantId, new ClaimSeatRequest("Blue")));
        Assert.Contains("already been claimed", doubleClaim.Message, StringComparison.OrdinalIgnoreCase);

        // The claimed seat commands its own ship...
        service.UpdateShipDamage(blueShip.Id, new UpdateShipDamageRequest(blueSession.ParticipantToken, 1, 0, 0, 0, 0));
        // ...and not the opponent's.
        Assert.Throws<UnauthorizedAccessException>(() =>
            service.UpdateShipDamage(redShip.Id, new UpdateShipDamageRequest(blueSession.ParticipantToken, 1, 0, 0, 0, 0)));

        // An unclaimed seat grants nothing.
        Assert.Single(service.RestoreMatch(source.GetSnapshot(owner.MatchId), null).Seats.Where(s => s.DisplayName == "Red" && !s.IsClaimed));
    }

    [Fact]
    public void RestoreMatch_RejectsUnusableSnapshots()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Reject Source"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        service.CreateShip(fleet.Id, new CreateShipRequest(owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24));
        var good = service.GetSnapshot(owner.MatchId);

        Assert.Throws<InvalidOperationException>(() => service.RestoreMatch(good with { Ships = [] }, null));
        Assert.Throws<InvalidOperationException>(() => service.RestoreMatch(good with { Participants = [] }, null));
        Assert.Throws<InvalidOperationException>(() => service.RestoreMatch(good with { RulesProfileKey = "not-a-profile" }, null));
    }

    [Fact]
    public void RestoreMatch_ClampsHandEditedValues()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Clamp Source"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        service.CreateShip(fleet.Id, new CreateShipRequest(owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24));
        var exported = service.GetSnapshot(owner.MatchId);
        var mangled = exported with
        {
            TableWidth = 100000,
            Ships = [exported.Ships.Single() with { ThrustRating = 9999, HullMax = 999999, HullDamage = -5, ScreenRating = 9, CurrentCourse = 40 }],
        };

        var restored = new InMemoryMatchService().RestoreMatch(mangled, null).Snapshot;
        var ship = restored.Ships.Single();

        Assert.Equal(144, restored.TableWidth);
        Assert.Equal(20, ship.ThrustRating);
        Assert.Equal(80, ship.HullMax);
        Assert.Equal(0, ship.HullDamage);
        Assert.Equal(3, ship.ScreenRating);
        Assert.InRange(ship.CurrentCourse, 1, 12);
    }
```

**Step 2: Run**

Run: `dotnet test tests/ForceSignal.Application.Tests --filter FullyQualifiedName~Restore`
Expected: PASS. If the clamp test fails, the corresponding normalizer call is missing from
Task 3 — fix there, not with a special case here.

**Step 3: Commit**

```bash
git add tests/ForceSignal.Application.Tests/InMemoryMatchServiceRestoreTests.cs
git commit -m "test: cover seat ownership, double claim, rejection and clamping on restore"
```

---

## Task 6: API endpoints

**Files:**
- Modify: `src/ForceSignal.Api/Program.cs`
- Test: `tests/ForceSignal.Api.Tests/MatchRestoreEndpointTests.cs` (create)

**Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ForceSignal.Api.Tests;

public sealed class MatchRestoreEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task RestoreAndClaim_RebuildsTheMatchAndIssuesAWorkingToken()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Restore Endpoint", 72, 48)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        var fleetSnapshot = await (await client.PostAsJsonAsync($"/api/matches/{created!.MatchId}/fleets",
            new CreateFleetRequest(created.ParticipantToken, "Blue Watch", null, "#47f1ff")))
            .Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var fleet = fleetSnapshot!.Fleets.Single();
        var withShip = await (await client.PostAsJsonAsync($"/api/fleets/{fleet.Id}/ships",
            new CreateShipRequest(created.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24)))
            .Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);

        // Restore accepts the exported wrapper shape, with no participant token.
        var backup = new { savedAt = DateTimeOffset.UtcNow, snapshot = withShip };
        using var restoreResponse = await client.PostAsync("/api/matches/restore",
            new StringContent(JsonSerializer.Serialize(backup, JsonOptions), Encoding.UTF8, "application/json"));
        restoreResponse.EnsureSuccessStatusCode();
        var restored = await restoreResponse.Content.ReadFromJsonAsync<MatchRestoredResponse>(JsonOptions);

        Assert.NotNull(restored);
        Assert.NotEqual(created.MatchId, restored.MatchId);
        var seat = Assert.Single(restored.Seats);
        Assert.False(seat.IsClaimed);

        using var claimResponse = await client.PostAsJsonAsync(
            $"/api/matches/{restored.MatchId}/seats/{seat.ParticipantId}/claim",
            new ClaimSeatRequest(seat.DisplayName));
        claimResponse.EnsureSuccessStatusCode();
        var session = await claimResponse.Content.ReadFromJsonAsync<MatchJoinedResponse>();

        // The issued token really commands the restored fleet.
        using var damage = await client.PostAsJsonAsync($"/api/ships/{withShip!.Ships.Single().Id}/damage",
            new UpdateShipDamageRequest(session!.ParticipantToken, 2, 0, 0, 0, 0));
        damage.EnsureSuccessStatusCode();

        // A second claim on the same seat is refused.
        using var secondClaim = await client.PostAsJsonAsync(
            $"/api/matches/{restored.MatchId}/seats/{seat.ParticipantId}/claim",
            new ClaimSeatRequest(seat.DisplayName));
        Assert.Equal(HttpStatusCode.BadRequest, secondClaim.StatusCode);
    }

    [Fact]
    public async Task Restore_WithGarbagePayload_ReturnsProblemDetails()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/matches/restore",
            new StringContent("{\"snapshot\":{\"ships\":[]}}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test tests/ForceSignal.Api.Tests --filter FullyQualifiedName~Restore`
Expected: FAIL — 404 on `/api/matches/restore`.

**Step 3: Add the endpoints**

In `Program.cs`, after the `JoinMatch` endpoint. Bind to `JsonElement` so both the wrapper and
a bare snapshot are accepted:

```csharp
app.MapPost("/api/matches/restore", (JsonElement body, IMatchService matches) =>
{
    var hasWrapper = body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty("snapshot", out var wrapped)
        && wrapped.ValueKind == JsonValueKind.Object;
    var snapshotElement = hasWrapper ? body.GetProperty("snapshot") : body;
    DateTimeOffset? savedAt = body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty("savedAt", out var saved)
        && saved.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(saved.GetString(), out var parsed)
            ? parsed
            : null;

    MatchSnapshotDto? snapshot;
    try
    {
        snapshot = snapshotElement.Deserialize<MatchSnapshotDto>(RestoreJson);
    }
    catch (JsonException ex)
    {
        throw new InvalidOperationException($"Snapshot could not be read: {ex.Message}");
    }

    if (snapshot is null)
    {
        throw new InvalidOperationException("Snapshot payload was empty.");
    }

    return Results.Ok(matches.RestoreMatch(snapshot, savedAt));
})
    .WithName("RestoreMatch")
    .WithTags("Matches")
    .WithSummary("Rebuilds a match from an exported snapshot backup.")
    .Produces<MatchRestoredResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest);

app.MapGet("/api/matches/{matchId:guid}/seats", (Guid matchId, IMatchService matches) =>
    Results.Ok(matches.GetSeats(matchId)))
    .WithName("GetMatchSeats")
    .WithTags("Matches")
    .WithSummary("Lists claimable seats in a restored match.")
    .Produces<IReadOnlyList<MatchSeatDto>>()
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/seats/{participantId:guid}/claim", async (
    Guid matchId,
    Guid participantId,
    ClaimSeatRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var session = matches.ClaimSeat(matchId, participantId, request);
    await NotifySnapshotChanged(hub, matches.GetSnapshot(matchId), "SeatClaimed");
    return Results.Ok(session);
})
    .WithName("ClaimMatchSeat")
    .WithTags("Matches")
    .WithSummary("Claims a seat in a restored match and issues a participant token.")
    .Produces<MatchJoinedResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);
```

Add near the top of `Program.cs`, after `var app = builder.Build();`:

```csharp
var RestoreJson = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() },
};
```

Add `using System.Text.Json;` to the usings. Add `GetSeats` to `IMatchService` and the service:

```csharp
    /// <summary>Lists seats for a match, showing which are still claimable.</summary>
    IReadOnlyList<MatchSeatDto> GetSeats(Guid matchId);
```

```csharp
    public IReadOnlyList<MatchSeatDto> GetSeats(Guid matchId)
    {
        lock (_gate)
        {
            return BuildSeats(FindMatch(matchId));
        }
    }
```

**Step 4: Run**

Run: `dotnet test tests/ForceSignal.Api.Tests --filter FullyQualifiedName~Restore`
Expected: PASS. Then `dotnet test ForceSignal.slnx` — all pass.

**Step 5: Commit**

```bash
git add src/ForceSignal.Api/Program.cs src/ForceSignal.Application/Matches/InMemoryMatchService.cs tests/ForceSignal.Api.Tests/MatchRestoreEndpointTests.cs
git commit -m "feat: expose match restore, seat list and seat claim endpoints"
```

---

## Task 7: Join by code surfaces unclaimed seats

**Files:**
- Modify: `src/ForceSignal.Application/Matches/InMemoryMatchService.cs` (`JoinMatch`)
- Test: `tests/ForceSignal.Application.Tests/InMemoryMatchServiceRestoreTests.cs`

**Step 1: Write the failing test**

```csharp
    [Fact]
    public void JoinMatch_OnRestoredMatchWithUnclaimedSeats_DoesNotMintANewParticipant()
    {
        var source = new InMemoryMatchService();
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Join Source"));
        source.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
        var fleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        source.CreateShip(fleet.Id, new CreateShipRequest(owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24));

        var service = new InMemoryMatchService();
        var restored = service.RestoreMatch(source.GetSnapshot(owner.MatchId), null);

        var error = Assert.Throws<InvalidOperationException>(() =>
            service.JoinMatch(new JoinMatchRequest(restored.JoinCode, "Someone New")));
        Assert.Contains("claim", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, service.GetSeats(restored.MatchId).Count);
    }
```

**Step 2: Run to verify it fails**

Run: `dotnet test tests/ForceSignal.Application.Tests --filter FullyQualifiedName~JoinMatch_OnRestoredMatch`
Expected: FAIL — join currently adds a third participant.

**Step 3: Implement**

In `JoinMatch`, after resolving `match`:

```csharp
            if (match.Participants.Any(p => !p.IsClaimed))
            {
                throw new InvalidOperationException("This match was restored from a backup. Claim your seat instead of joining.");
            }
```

**Step 4: Run the full suite**

Run: `dotnet test ForceSignal.slnx`
Expected: all pass.

**Step 5: Commit**

```bash
git add src/ForceSignal.Application/Matches/InMemoryMatchService.cs tests/ForceSignal.Application.Tests/InMemoryMatchServiceRestoreTests.cs
git commit -m "feat: point joiners at seat claiming on a restored match"
```

---

## Task 8: Web client — Restore Match and seat picker

**Files:**
- Modify: `src/ForceSignal.Web/src/main.tsx`
- Modify: `src/ForceSignal.Web/src/style.css`

**Step 1: Add types and state**

Types, beside `Session`:

```tsx
type MatchSeat = {
  participantId: string;
  displayName: string;
  role: string;
  isClaimed: boolean;
  fleetCount: number;
  shipCount: number;
};

type MatchRestored = {
  matchId: string;
  joinCode: string;
  reusedJoinCode: boolean;
  restoredPhase: string;
  lockedOrdersDropped: boolean;
  seats: MatchSeat[];
};
```

In `App`, beside the other join-screen state:

```tsx
  const [pendingRestore, setPendingRestore] = useState<MatchRestored | null>(null);
  const restoreInputRef = useRef<HTMLInputElement | null>(null);
```

**Step 2: Add the actions**

```tsx
  async function restoreFromBackupFile(file: File) {
    const restored = await post<MatchRestored>('/api/matches/restore', JSON.parse(await file.text()));
    setPendingRestore(restored);
    setMessage(restored.lockedOrdersDropped
      ? `Restored into room ${restored.joinCode}. Locked orders could not be recovered — re-lock to continue.`
      : `Restored into room ${restored.joinCode}. Claim your seat to take command.`);
  }

  async function claimSeat(restored: MatchRestored, seat: MatchSeat) {
    const session = await post<Session>(`/api/matches/${restored.matchId}/seats/${seat.participantId}/claim`, {
      displayName: seat.displayName,
    });
    setPendingRestore(null);
    setSession(session);
    setMessage(`Took command as ${seat.displayName}.`);
  }

  async function loadSeatsForCode(code: string) {
    // A restored room refuses ordinary joins, so fall back to its seat list.
    const joined = await post<Session>('/api/matches/join', { displayName, joinCode: code });
    setSession(joined);
  }
```

For the join button, catch the restored-match rejection and show seats instead:

```tsx
  async function joinMatch() {
    clearLocalMatchState();
    try {
      await loadSeatsForCode(joinCode);
      setMessage(`Joined room ${joinCode}.`);
    } catch (error) {
      if (!(error instanceof ApiRequestError) || error.status !== 400 || !error.message.toLowerCase().includes('claim')) {
        throw error;
      }

      const matchId = await get<{ matchId: string }>(`/api/matches/by-code/${encodeURIComponent(joinCode)}`);
      setPendingRestore({
        matchId: matchId.matchId,
        joinCode,
        reusedJoinCode: true,
        restoredPhase: '',
        lockedOrdersDropped: false,
        seats: await get<MatchSeat[]>(`/api/matches/${matchId.matchId}/seats`),
      });
      setMessage('This room was restored from a backup. Claim your seat.');
    }
  }
```

> **Note for the implementer:** this needs one more tiny read endpoint,
> `GET /api/matches/by-code/{joinCode}` returning `{ matchId }`, because the client only knows
> the room code. Add it in Task 6 style beside the seat list, with a test asserting 404 for an
> unknown code. Keep it token-free: it exposes only an id that the room code already implies.

**Step 3: Render the seat picker**

Replace the join-screen section's contents so that when `pendingRestore` is set, the seat list
shows instead of the create/join form:

```tsx
        pendingRestore ? (
          <section className="panel seat-picker" aria-label="Claim a seat">
            <div>
              <span className="label">Restored room</span>
              <h2>{pendingRestore.joinCode}</h2>
              <p className="privacy">Pick the admiral you were playing. Fleets follow the seat.</p>
            </div>
            {pendingRestore.seats.map((seat) => (
              <button
                key={seat.participantId}
                type="button"
                disabled={seat.isClaimed}
                onClick={() => claimSeat(pendingRestore, seat).catch(showError(setMessage))}
              >
                {seat.displayName} · {seat.role} · {seat.fleetCount} fleet{seat.fleetCount === 1 ? '' : 's'}, {seat.shipCount} ship{seat.shipCount === 1 ? '' : 's'}
                {seat.isClaimed ? ' · taken' : ''}
              </button>
            ))}
            <button className="ghost" type="button" onClick={() => setPendingRestore(null)}>Cancel</button>
          </section>
        ) : (
          /* existing create/join auth-grid section */
        )
```

Add the Restore Match control to the existing `auth-grid`, beside *Export Last Device Backup*:

```tsx
          <button className="ghost auth-wide" type="button" onClick={() => restoreInputRef.current?.click()}>Restore Match</button>
          <input
            ref={restoreInputRef}
            className="file-input"
            type="file"
            accept=".json,application/json"
            onChange={(event) => {
              const file = event.target.files?.[0];
              event.target.value = '';
              if (file) {
                restoreFromBackupFile(file).catch(showError(setMessage));
              }
            }}
          />
```

**Step 4: Style**

```css
.seat-picker {
  display: grid;
  gap: 10px;
  padding: 18px;
}
```

**Step 5: Build**

Run: `cd src/ForceSignal.Web && npm run build`
Expected: `tsc` clean, vite writes `dist/`.

**Step 6: Verify by hand against a running stack**

1. `dotnet run --project src/ForceSignal.Api --launch-profile http`
2. `cd src/ForceSignal.Web && npm run dev`
3. Create a match, add ships, damage one, advance to Firing, fire a weapon, `Save Snapshot`.
4. Restart the API (this is the failure being fixed).
5. Reload, click **Restore Match**, pick the file, claim the owner seat.
6. Confirm: turn number, phase, damage, positions and log came back, and the weapon already
   fired shows `spent`.

**Step 7: Commit**

```bash
git add src/ForceSignal.Web/src/main.tsx src/ForceSignal.Web/src/style.css
git commit -m "feat: restore a match from a backup file and claim a seat"
```

---

## Task 9: Docs

**Files:**
- Modify: `README.md`, `docs/roadmap.md`, `docs/current-functionality-should.md`

**Step 1: README** — under *Current Slice*, add:

```markdown
- Restore a match from an exported snapshot backup and claim your original seat.
```

**Step 2: Roadmap** — mark the snapshot-recovery line as covering restore, and note under the
PostgreSQL item that file-based restore now covers API-restart recovery, which reduces the
urgency of durable storage without replacing it.

**Step 3: Functionality doc** — under *Export, Import, And Recovery*, add:

```markdown
- A snapshot backup should restore a full match, with each player claiming their original seat.
- Restoring a snapshot taken during order entry should reopen order entry and say that locked
  orders could not be recovered.
```

**Step 4: Commit**

```bash
git add README.md docs/roadmap.md docs/current-functionality-should.md
git commit -m "docs: describe match restore and seat claiming"
```

---

## Done when

- `dotnet test ForceSignal.slnx` passes with the new restore tests.
- `cd src/ForceSignal.Web && npm run build` is clean.
- `docker compose up -d --build && ./scripts/docker-smoke.ps1` passes.
- The manual check in Task 8 Step 6 recovers a mid-battle match after an API restart.
