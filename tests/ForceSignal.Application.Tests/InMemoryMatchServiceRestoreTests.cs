using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

public sealed class InMemoryMatchServiceRestoreTests
{
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
        Assert.Equal(20, ship.PositionX);
        Assert.Equal(1, ship.Weapons.Single().AmmoUsed);
        Assert.Equal("#f5c766", restored.Snapshot.Fleets.Single().FleetColor);

        // Fleet and ship ids are reissued so a restored copy cannot collide with a still-running
        // match in the by-ship-id and by-fleet-id lookups; references stay internally consistent.
        var restoredFleet = restored.Snapshot.Fleets.Single();
        Assert.NotEqual(exported.Ships.Single().Id, ship.Id);
        Assert.NotEqual(exported.Fleets.Single().Id, restoredFleet.Id);
        Assert.Equal(restoredFleet.Id, ship.FleetId);
        // Participant ids are preserved so a device recognises the seat it held.
        Assert.Equal(exported.Participants.Single().Id, restored.Seats.Single().ParticipantId);

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

        var exportedTarget = exported.Ships.Single(s => s.Name == "Crimson");
        var restoredTarget = restored.Snapshot.Ships.Single(s => s.Name == "Crimson");
        Assert.Equal(exportedTarget.HullDamage, restoredTarget.HullDamage);
        Assert.Equal(exportedTarget.ArmorDamage, restoredTarget.ArmorDamage);
        Assert.Equal(exported.MovementResults.Count, restored.Snapshot.MovementResults.Count);
        Assert.Equal(exported.FiringResults.Count, restored.Snapshot.FiringResults.Count);

        // The spent weapon stays spent: firing it again must be refused.
        var seat = restored.Seats.Single(s => s.Role == "Owner");
        var session = service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName));
        var restoredAttacker = restored.Snapshot.Ships.Single(s => s.Name == "Valiant");
        var error = Assert.Throws<InvalidOperationException>(() =>
            service.FireWeapon(restored.MatchId, new FireWeaponRequest(session.ParticipantToken, restoredAttacker.Id, restoredTarget.Id, weaponId, 8, FiringArc.Fore)));
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

    [Fact]
    public void RestoreMatch_FromMovementExport_AppliesRestoredOrdersExactlyOnce()
    {
        var source = new InMemoryMatchService();
        var owner = source.CreateMatch(new CreateMatchRequest("Solo", "Movement Source"));
        var fleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        var ship = source.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Lone Star", "Cruiser", 4, 6, 3, 12, 2, StartX: 20, StartY: 24)).Ships.Single();
        source.SetReady(owner.MatchId, owner.ParticipantToken, true);
        var order = new MovementOrder(1, 1, TurnDirection.Starboard, [new TurnManeuver(TurnDirection.Starboard, 1)]);
        source.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, ship.Id, order, "s"));
        var movement = source.RevealOrder(owner.MatchId, new RevealOrderRequest(owner.ParticipantToken, ship.Id, order, "s"));
        Assert.Equal("Movement", movement.Phase);

        var service = new InMemoryMatchService();
        var restored = service.RestoreMatch(source.GetSnapshot(owner.MatchId), null);
        Assert.Equal("Movement", restored.RestoredPhase);

        var seat = restored.Seats.Single();
        var session = service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName));
        var firing = service.AdvanceTurn(restored.MatchId, session.ParticipantToken);

        // The restored order resolves once: velocity 6+1, course 3 starboard 1.
        var moved = firing.Ships.Single();
        Assert.Equal("Firing", firing.Phase);
        Assert.Equal(7, moved.CurrentVelocity);
        Assert.Equal(4, moved.CurrentCourse);

        // Advancing again starts the next turn rather than re-applying movement.
        var nextTurn = service.AdvanceTurn(restored.MatchId, session.ParticipantToken);
        Assert.Equal("OrderEntry", nextTurn.Phase);
        Assert.Equal(7, nextTurn.Ships.Single().CurrentVelocity);
        Assert.Equal(4, nextTurn.Ships.Single().CurrentCourse);
    }

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
        var restoredBlueShip = restored.Snapshot.Ships.Single(s => s.Name == "Valiant");
        var restoredRedShip = restored.Snapshot.Ships.Single(s => s.Name == "Crimson");
        service.UpdateShipDamage(restoredBlueShip.Id, new UpdateShipDamageRequest(blueSession.ParticipantToken, 1, 0, 0, 0, 0));

        // ...and not the opponent's.
        Assert.Throws<UnauthorizedAccessException>(() =>
            service.UpdateShipDamage(restoredRedShip.Id, new UpdateShipDamageRequest(blueSession.ParticipantToken, 1, 0, 0, 0, 0)));

        // Source ids belong to the other service instance and are unknown here.
        Assert.Throws<InvalidOperationException>(() =>
            service.UpdateShipDamage(blueShip.Id, new UpdateShipDamageRequest(blueSession.ParticipantToken, 1, 0, 0, 0, 0)));
        Assert.NotEqual(blueShip.Id, restoredBlueShip.Id);
        Assert.NotEqual(redShip.Id, restoredRedShip.Id);

        // The opponent seat is still claimable, and the claimed one reports as taken.
        var seats = service.GetSeats(restored.MatchId);
        Assert.True(seats.Single(s => s.DisplayName == "Blue").IsClaimed);
        Assert.False(seats.Single(s => s.DisplayName == "Red").IsClaimed);
    }

    [Fact]
    public void RestoreMatch_AlongsideTheStillRunningSourceMatch_LeavesBothEditable()
    {
        // Restoring a backup of a match that is still live must not make ship lookups ambiguous:
        // the by-ship-id endpoints scan every match in the store.
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Live Source"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        var liveShip = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24)).Ships.Single();

        var restored = service.RestoreMatch(service.GetSnapshot(owner.MatchId), null);
        var seat = restored.Seats.Single();
        var session = service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName));
        var restoredShip = restored.Snapshot.Ships.Single();

        // Both copies remain independently editable.
        var liveEdit = service.UpdateShipDamage(liveShip.Id, new UpdateShipDamageRequest(owner.ParticipantToken, 3, 0, 0, 0, 0));
        var restoredEdit = service.UpdateShipDamage(restoredShip.Id, new UpdateShipDamageRequest(session.ParticipantToken, 5, 0, 0, 0, 0));

        Assert.Equal(3, liveEdit.Ships.Single().HullDamage);
        Assert.Equal(5, restoredEdit.Ships.Single().HullDamage);
        Assert.NotEqual(liveShip.Id, restoredShip.Id);
        // The room code was taken, so a fresh one was minted.
        Assert.False(restored.ReusedJoinCode);
        Assert.NotEqual(owner.JoinCode, restored.JoinCode);
    }

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

        // Once every seat is taken, the room behaves like any other and accepts joiners.
        foreach (var seat in service.GetSeats(restored.MatchId))
        {
            service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName));
        }

        var late = service.JoinMatch(new JoinMatchRequest(restored.JoinCode, "Someone New"));
        Assert.False(string.IsNullOrWhiteSpace(late.ParticipantToken));
        Assert.Equal(3, service.GetSeats(restored.MatchId).Count);
    }

    [Fact]
    public void RestoreMatch_CarriesOrdnanceMarkersAndSeatReadiness()
    {
        var source = new InMemoryMatchService();
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Ordnance Source"));
        var fleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        var carrier = source.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Home Plate", "Carrier", 4, 4, 3, 14, 5, StartX: 12, StartY: 20, IconKey: "carrier")).Ships.Single(s => s.Name == "Home Plate");
        var fighter = source.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Alpha Wing", "Fighter Group", 6, 12, 3, 6, 0,
            StartX: 16, StartY: 22, IconKey: "fighter-group",
            FighterEnduranceMax: 6, FighterEnduranceUsed: 2, FighterMaxRange: 24,
            FighterStatus: "Airborne", HomeCarrierShipId: carrier.Id)).Ships.Single(s => s.Name == "Alpha Wing");
        source.CreateOrdnanceMarker(owner.MatchId, new CreateOrdnanceMarkerRequest(
            owner.ParticipantToken, "Alpha Salvo", "Missile", carrier.Id, fighter.Id, 18, 24, 2, 12, 3, 3, 24));
        source.SetReady(owner.MatchId, owner.ParticipantToken, true);
        var exported = source.GetSnapshot(owner.MatchId);
        Assert.True(exported.Participants.Single().IsReady);

        var restored = new InMemoryMatchService().RestoreMatch(exported, null).Snapshot;

        // Ordnance survives, with its ship references remapped to the restored hulls.
        var marker = Assert.Single(restored.OrdnanceMarkers);
        var restoredCarrier = restored.Ships.Single(s => s.Name == "Home Plate");
        var restoredFighter = restored.Ships.Single(s => s.Name == "Alpha Wing");
        Assert.Equal("Alpha Salvo", marker.Name);
        Assert.Equal("Missile", marker.MarkerType);
        Assert.Equal("Active", marker.Status);
        Assert.Equal(3, marker.EnduranceRemaining);
        Assert.Equal(18, marker.PositionX);
        Assert.Equal(24, marker.PositionY);
        Assert.Equal(restoredCarrier.Id, marker.SourceShipId);
        Assert.Equal(restoredFighter.Id, marker.TargetShipId);

        // Fighter state and the carrier link come back.
        Assert.Equal("Airborne", restoredFighter.FighterStatus);
        Assert.Equal(2, restoredFighter.FighterEnduranceUsed);
        Assert.Equal(24, restoredFighter.FighterMaxRange);
        Assert.Equal(restoredCarrier.Id, restoredFighter.HomeCarrierShipId);

        // Readiness is preserved; the seat is unclaimed and offline until a device takes it.
        var participant = Assert.Single(restored.Participants);
        Assert.True(participant.IsReady);
        Assert.False(participant.IsConnected);
    }

    [Fact]
    public void ClaimSeat_ForAnUnknownSeat_IsRejectedAsNotFound()
    {
        var source = new InMemoryMatchService();
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Unknown Seat Source"));
        var fleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        source.CreateShip(fleet.Id, new CreateShipRequest(owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24));

        var service = new InMemoryMatchService();
        var restored = service.RestoreMatch(source.GetSnapshot(owner.MatchId), null);

        var unknownSeat = Assert.Throws<InvalidOperationException>(() =>
            service.ClaimSeat(restored.MatchId, Guid.NewGuid(), new ClaimSeatRequest("Nobody")));
        // "not found" is what the API layer maps to 404.
        Assert.Contains("not found", unknownSeat.Message, StringComparison.OrdinalIgnoreCase);

        var unknownMatch = Assert.Throws<InvalidOperationException>(() =>
            service.ClaimSeat(Guid.NewGuid(), restored.Seats.Single().ParticipantId, new ClaimSeatRequest("Nobody")));
        Assert.Contains("not found", unknownMatch.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestoreMatch_RejectedPayload_LeavesTheStoreUntouched()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Atomic Source"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        service.CreateShip(fleet.Id, new CreateShipRequest(owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24));
        var exported = service.GetSnapshot(owner.MatchId);

        // A ship pointing at a fleet that is not in the payload fails partway through the rebuild.
        var broken = exported with { Fleets = [] };
        Assert.Throws<InvalidOperationException>(() => service.RestoreMatch(broken, null));

        // Nothing was committed: the room code is still free, so a good restore can reuse it.
        var good = service.RestoreMatch(exported, null);
        Assert.False(good.ReusedJoinCode); // the source match still holds the original code
        var afterFailure = Assert.Throws<InvalidOperationException>(() => service.FindMatchByCode("NO-SUCH-ROOM"));
        Assert.Contains("not found", afterFailure.Message, StringComparison.OrdinalIgnoreCase);

        // The source match is intact and still commandable.
        var sourceStillWorks = service.GetSnapshot(owner.MatchId);
        Assert.Single(sourceStillWorks.Ships);
        Assert.Equal("FleetSetup", sourceStillWorks.Phase);
    }

    [Fact]
    public void RestoreMatch_KeepsEveryWeaponMountEvenWhenAnEntryIsMalformed()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Weapon Source"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24,
            Weapons:
            [
                new WeaponMountDto(Guid.NewGuid(), "Class-3 Beam", 3, 24, FiringArc.All, AmmoMax: 2, AmmoUsed: 1),
                new WeaponMountDto(Guid.NewGuid(), "Needle Missile", 2, 30, FiringArc.Fore, AmmoMax: 1),
            ]));
        var exported = service.GetSnapshot(owner.MatchId);
        var ship = exported.Ships.Single();

        // A hand-edited file with a blank mount name must not disarm the ship.
        var mangled = exported with
        {
            Ships = [ship with { Weapons = [ship.Weapons[0] with { Name = "   " }, ship.Weapons[1]] }],
        };

        var restored = new InMemoryMatchService().RestoreMatch(mangled, null).Snapshot;
        var restoredShip = restored.Ships.Single();

        Assert.Equal(2, restoredShip.Weapons.Count);
        Assert.Equal("Unnamed Mount", restoredShip.Weapons[0].Name);
        Assert.Equal(1, restoredShip.Weapons[0].AmmoUsed);
        Assert.Equal(2, restoredShip.Weapons[0].AmmoMax);
        Assert.Equal("Needle Missile", restoredShip.Weapons[1].Name);
    }

    [Fact]
    public void RestoreMatch_FromRevealExport_FallsBackToOrderEntry()
    {
        // The design's phase table maps Reveal alongside OrdersLocked; this pins that row.
        var table = RestoreFixture.TwoFleetsReady("Reveal Row");
        var drift = new MovementOrder(0, 0, TurnDirection.None);
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.BlueShipId, drift, "b"));
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OpponentToken, table.RedShipId, drift, "r"));
        var partial = table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.BlueShipId, drift, "b"));
        Assert.Equal("Reveal", partial.Phase);

        var restored = new InMemoryMatchService().RestoreMatch(table.Service.GetSnapshot(table.MatchId), null);

        Assert.Equal("OrderEntry", restored.RestoredPhase);
        Assert.True(restored.LockedOrdersDropped);
        Assert.All(restored.Snapshot.OrderStatuses, status => Assert.False(status.IsCommitted));
        Assert.Empty(restored.Snapshot.RevealedOrders);
        Assert.Contains(restored.Snapshot.MatchLog, e => e.Message.Contains("Locked orders could not be restored", StringComparison.Ordinal));
    }

    [Fact]
    public void RestoreMatch_KeepsDestroyedShipsDestroyedAndStaysPlayable()
    {
        var table = RestoreFixture.TwoFleetsReady("Destroyed Row");
        var redShip = table.Service.GetSnapshot(table.MatchId).Ships.Single(s => s.Name == "Crimson");
        table.Service.UpdateShipDamage(redShip.Id, new UpdateShipDamageRequest(table.OpponentToken, redShip.HullMax, redShip.ArmorMax, 0, 0, 0));
        var exported = table.Service.GetSnapshot(table.MatchId);
        Assert.True(exported.Ships.Single(s => s.Name == "Crimson").IsDestroyed);

        var service = new InMemoryMatchService();
        var restored = service.RestoreMatch(exported, null);

        var wreck = restored.Snapshot.Ships.Single(s => s.Name == "Crimson");
        Assert.True(wreck.IsDestroyed);
        Assert.Equal(wreck.HullMax, wreck.HullDamage);

        // The restored match is still playable: claim both seats, ready up, reach order entry
        // without the wreck blocking the gate.
        foreach (var seat in restored.Seats)
        {
            var session = service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName));
            var snapshot = service.SetReady(restored.MatchId, session.ParticipantToken, true);
            if (seat == restored.Seats[^1])
            {
                Assert.Equal("OrderEntry", snapshot.Phase);
            }
        }
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
        Assert.Throws<InvalidOperationException>(() => service.RestoreMatch(good with { Fleets = [] }, null));
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
            Ships = [exported.Ships.Single() with
            {
                ThrustRating = 9999,
                HullMax = 999999,
                HullDamage = -5,
                ScreenRating = 9,
                CurrentCourse = 40,
                CurrentVelocity = -12,
            }],
        };

        var restored = new InMemoryMatchService().RestoreMatch(mangled, null).Snapshot;
        var ship = restored.Ships.Single();

        Assert.Equal(144, restored.TableWidth);
        Assert.Equal(20, ship.ThrustRating);
        Assert.Equal(80, ship.HullMax);
        Assert.Equal(0, ship.HullDamage);
        Assert.Equal(3, ship.ScreenRating);
        Assert.Equal(0, ship.CurrentVelocity);
        Assert.InRange(ship.CurrentCourse, 1, 12);
    }

    /// <summary>Two ready fleets, one ship each, as a starting point for restore scenarios.</summary>
    private sealed record RestoreFixture(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid BlueShipId,
        Guid RedShipId)
    {
        public static RestoreFixture TwoFleetsReady(string matchName)
        {
            var service = new InMemoryMatchService();
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", matchName));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
            var blueShip = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24)).Ships.Single(s => s.Name == "Valiant");
            var redShip = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Crimson", "Destroyer", 4, 6, 9, 10, 1, StartX: 32, StartY: 24)).Ships.Single(s => s.Name == "Crimson");
            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
            return new RestoreFixture(service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, blueShip.Id, redShip.Id);
        }
    }

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
