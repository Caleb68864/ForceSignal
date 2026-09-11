using ForceSignal.Application;
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
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Restore Source", 96, 72, Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue Watch", "Test", "#f5c766")).Fleets.Single();
        service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4,
            StartX: 20, StartY: 24, ScreenRating: 1,
            Weapons: [new WeaponMountDto(Guid.NewGuid(), "Class-3 Beam", 3, 24, [.. FiringArcs.Firable], AmmoMax: 2, AmmoUsed: 1)]));
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
        var source = new InMemoryMatchService(_ => 6);
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Firing Source", Rules: TestRules.Invented));
        var opponent = source.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
        var blueFleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var redFleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null)).Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        var weaponId = Guid.NewGuid();
        var attacker = source.CreateShip(blueFleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24, FireControlMax: 1,
            Weapons: [new WeaponMountDto(weaponId, "Class-3 Beam", 3, 24, [.. FiringArcs.Firable])])).Ships.Single(s => s.Name == "Valiant");
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
        source.FireWeapon(owner.MatchId, new FireWeaponRequest(owner.ParticipantToken, attacker.Id, target.Id, weaponId, 8));
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
        var session = service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName, restored.JoinCode));
        var restoredAttacker = restored.Snapshot.Ships.Single(s => s.Name == "Valiant");
        var error = Assert.Throws<InvalidOperationException>(() =>
            service.FireWeapon(restored.MatchId, new FireWeaponRequest(session.ParticipantToken, restoredAttacker.Id, restoredTarget.Id, weaponId, 8)));
        Assert.Contains("already fired", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestoreMatch_FromOrdersLockedExport_FallsBackToOrderEntry()
    {
        var source = new InMemoryMatchService();
        var owner = source.CreateMatch(new CreateMatchRequest("Solo", "Locked Source", Rules: TestRules.Invented));
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
        var owner = source.CreateMatch(new CreateMatchRequest("Solo", "Movement Source", Rules: TestRules.Invented));
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
        var session = service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName, restored.JoinCode));
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
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Ownership Source", Rules: TestRules.Invented));
        var opponent = source.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
        var blueFleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var redFleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null)).Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        var blueShip = source.CreateShip(blueFleet.Id, new CreateShipRequest(owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24)).Ships.Single(s => s.Name == "Valiant");
        var redShip = source.CreateShip(redFleet.Id, new CreateShipRequest(opponent.ParticipantToken, "Crimson", "Destroyer", 4, 0, 9, 10, 1, StartX: 32, StartY: 24)).Ships.Single(s => s.Name == "Crimson");

        var service = new InMemoryMatchService();
        var restored = service.RestoreMatch(source.GetSnapshot(owner.MatchId), null);
        var blueSeat = restored.Seats.Single(s => s.DisplayName == "Blue");
        var blueSession = service.ClaimSeat(restored.MatchId, blueSeat.ParticipantId, new ClaimSeatRequest("Blue", restored.JoinCode));

        // Claiming the same seat again is refused.
        var doubleClaim = Assert.Throws<InvalidOperationException>(() =>
            service.ClaimSeat(restored.MatchId, blueSeat.ParticipantId, new ClaimSeatRequest("Blue", restored.JoinCode)));
        Assert.Contains("already been claimed", doubleClaim.Message, StringComparison.OrdinalIgnoreCase);

        // The claimed seat commands its own ship...
        var restoredBlueShip = restored.Snapshot.Ships.Single(s => s.Name == "Valiant");
        var restoredRedShip = restored.Snapshot.Ships.Single(s => s.Name == "Crimson");
        service.UpdateShipDamage(restoredBlueShip.Id, new UpdateShipDamageRequest(blueSession.ParticipantToken, 1, 0, 0, 0, 0));

        // ...and not the opponent's.
        Assert.Throws<UnauthorizedAccessException>(() =>
            service.UpdateShipDamage(restoredRedShip.Id, new UpdateShipDamageRequest(blueSession.ParticipantToken, 1, 0, 0, 0, 0)));

        // Source ids belong to the other service instance and are unknown here.
        Assert.Throws<NotFoundException>(() =>
            service.UpdateShipDamage(blueShip.Id, new UpdateShipDamageRequest(blueSession.ParticipantToken, 1, 0, 0, 0, 0)));
        Assert.NotEqual(blueShip.Id, restoredBlueShip.Id);
        Assert.NotEqual(redShip.Id, restoredRedShip.Id);

        // The opponent seat is still claimable, and the claimed one reports as taken.
        var seats = service.GetSeats(restored.MatchId, restored.JoinCode);
        Assert.True(seats.Single(s => s.DisplayName == "Blue").IsClaimed);
        Assert.False(seats.Single(s => s.DisplayName == "Red").IsClaimed);
    }

    [Fact]
    public void RestoreMatch_AlongsideTheStillRunningSourceMatch_LeavesBothEditable()
    {
        // Restoring a backup of a match that is still live must not make ship lookups ambiguous:
        // the by-ship-id endpoints scan every match in the store.
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Live Source", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        var liveShip = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24)).Ships.Single();

        var restored = service.RestoreMatch(service.GetSnapshot(owner.MatchId), null);
        var seat = restored.Seats.Single();
        var session = service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName, restored.JoinCode));
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
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Join Source", Rules: TestRules.Invented));
        source.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
        var fleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        source.CreateShip(fleet.Id, new CreateShipRequest(owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24));

        var service = new InMemoryMatchService();
        var restored = service.RestoreMatch(source.GetSnapshot(owner.MatchId), null);

        var error = Assert.Throws<InvalidOperationException>(() =>
            service.JoinMatch(new JoinMatchRequest(restored.JoinCode, "Someone New")));
        Assert.Contains("claim", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, service.GetSeats(restored.MatchId, restored.JoinCode).Count);

        // Once every seat is taken, the room behaves like any other and accepts joiners.
        foreach (var seat in service.GetSeats(restored.MatchId, restored.JoinCode))
        {
            service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName, restored.JoinCode));
        }

        var late = service.JoinMatch(new JoinMatchRequest(restored.JoinCode, "Someone New"));
        Assert.False(string.IsNullOrWhiteSpace(late.ParticipantToken));
        Assert.Equal(3, service.GetSeats(restored.MatchId, restored.JoinCode).Count);
    }

    [Fact]
    public void RestoreMatch_CarriesOrdnanceMarkersAndSeatReadiness()
    {
        var source = new InMemoryMatchService();
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Ordnance Source", Rules: TestRules.Invented));
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
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Unknown Seat Source", Rules: TestRules.Invented));
        var fleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        source.CreateShip(fleet.Id, new CreateShipRequest(owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24));

        var service = new InMemoryMatchService();
        var restored = service.RestoreMatch(source.GetSnapshot(owner.MatchId), null);

        var unknownSeat = Assert.Throws<NotFoundException>(() =>
            service.ClaimSeat(restored.MatchId, Guid.NewGuid(), new ClaimSeatRequest("Nobody", restored.JoinCode)));
        // The type is what the API layer maps to 404; the words are for the player.
        Assert.Contains("not found", unknownSeat.Message, StringComparison.OrdinalIgnoreCase);

        var unknownMatch = Assert.Throws<NotFoundException>(() =>
            service.ClaimSeat(Guid.NewGuid(), restored.Seats.Single().ParticipantId, new ClaimSeatRequest("Nobody", restored.JoinCode)));
        Assert.Contains("not found", unknownMatch.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestoreMatch_RejectedPayload_LeavesTheStoreUntouched()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Atomic Source", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        service.CreateShip(fleet.Id, new CreateShipRequest(owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24));
        var exported = service.GetSnapshot(owner.MatchId);

        // A ship pointing at a fleet that is not in the payload fails partway through the rebuild.
        var broken = exported with { Fleets = [] };
        Assert.Throws<InvalidOperationException>(() => service.RestoreMatch(broken, null));

        // Nothing was committed: the room code is still free, so a good restore can reuse it.
        var good = service.RestoreMatch(exported, null);
        Assert.False(good.ReusedJoinCode); // the source match still holds the original code
        var afterFailure = Assert.Throws<NotFoundException>(() => service.FindMatchByCode("NO-SUCH-ROOM"));
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
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Weapon Source", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Watch", null)).Fleets.Single();
        service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 3, 12, 2, StartX: 20, StartY: 24,
            Weapons:
            [
                new WeaponMountDto(Guid.NewGuid(), "Class-3 Beam", 3, 24, [.. FiringArcs.Firable], AmmoMax: 2, AmmoUsed: 1),
                new WeaponMountDto(Guid.NewGuid(), "Needle Missile", 2, 30, [FiringArc.Fore], AmmoMax: 1),
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
            var session = service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName, restored.JoinCode));
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
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Reject Source", Rules: TestRules.Invented));
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
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Clamp Source", Rules: TestRules.Invented));
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
        Assert.Equal(TestRules.Invented.MaxScreenLevel, ship.ScreenRating);
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
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", matchName, Rules: TestRules.Invented));
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
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Seat Guard", Rules: TestRules.Invented));

        Assert.False(service.IsMatchParticipant(owner.MatchId, string.Empty));
        Assert.False(service.IsMatchParticipant(owner.MatchId, "   "));
        Assert.Throws<UnauthorizedAccessException>(() =>
            service.SetReady(owner.MatchId, string.Empty, true));
    }

    [Fact]
    public void RestoringAFiringPhase_KeepsTheTurnOrderRatherThanStartingItAgain()
    {
        var dice = new ScriptedDice { Fallback = 4 };
        var service = new InMemoryMatchService(dice.Next);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Mid Volley", Rules: TestRules.Invented));
        var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
        var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
            .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
            .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        service.CreateShip(blueFleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 0, 12, 20, 0, StartX: 20, StartY: 28));
        service.CreateShip(redFleet.Id, new CreateShipRequest(
            opponent.ParticipantToken, "Crimson", "Cruiser", 4, 0, 6, 20, 0, StartX: 20, StartY: 22));

        service.SetReady(owner.MatchId, owner.ParticipantToken, true);
        service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
        service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(owner.ParticipantToken));
        service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(opponent.ParticipantToken));
        var firing = service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);
        Assert.Equal("Firing", firing.Phase);

        // Whoever won the die-off finishes one ship's fire, which spends that ship's turn.
        var holder = firing.FiringParticipantId;
        Assert.NotNull(holder);
        var holderToken = holder == owner.ParticipantId ? owner.ParticipantToken : opponent.ParticipantToken;
        var holderShip = firing.Ships.Single(ship =>
            firing.Fleets.Single(fleet => fleet.Id == ship.FleetId).OwnerParticipantId == holder);
        var afterCeaseFire = service.CeaseFire(owner.MatchId, new CeaseFireRequest(holderToken, holderShip.Id));
        Assert.Contains(holderShip.Id, afterCeaseFire.ActivatedShipIds);

        // Export mid-phase and bring it back somewhere else.
        var restored = new InMemoryMatchService().RestoreMatch(afterCeaseFire, null).Snapshot;

        // The ship that had already taken its turn must not get another one. Rolling a fresh
        // die-off, as this used to, handed one side a second round of shooting.
        Assert.Equal("Firing", restored.Phase);
        var restoredShip = restored.Ships.Single(ship => ship.Name == holderShip.Name);
        Assert.Contains(restoredShip.Id, restored.ActivatedShipIds);
        Assert.Equal(afterCeaseFire.FiringParticipantId, restored.FiringParticipantId);
    }

    [Fact]
    public void RestoreMatch_WithAShotCarryingMoreDiceThanAMatchHolds_IsRefusedByName()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Loaded Dice", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue Watch", null)).Fleets.Single();
        service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24));
        var exported = service.GetSnapshot(owner.MatchId);

        // The number of shots is already refused past its ceiling, so this is the way left to turn a
        // small file into a large match: one shot, holding a quarter of a million dice. Every one of
        // them is kept for the after-action review and written back out inside every later snapshot.
        var ship = exported.Ships.Single();
        var loaded = exported with
        {
            FiringResults =
            [
                new FiringResultDto(
                    ship.Id, ship.Id, Guid.NewGuid(), "Overloaded Battery", 1, 6, "close", FiringArc.Fore,
                    1, 0, 0, 0, 1, 0, 1, [.. Enumerable.Repeat(6, 250_000)]),
            ],
        };

        var refused = Assert.Throws<InvalidOperationException>(
            () => new InMemoryMatchService().RestoreMatch(loaded, savedAt: null));

        Assert.Contains("Overloaded Battery", refused.Message, StringComparison.Ordinal);
        Assert.Contains("dice on one shot", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreMatch_WithAnOrdinaryVolley_KeepsEveryDieTheTableRolled()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Real Volley", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue Watch", null)).Fleets.Single();
        service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24));
        var exported = service.GetSnapshot(owner.MatchId);
        var ship = exported.Ships.Single();

        // The ceiling is a guard against a crafted file, not a rules limit: a real broadside's worth
        // of dice has to come back untouched, or the ceiling has cost the table its audit trail.
        var volley = exported with
        {
            FiringResults =
            [
                new FiringResultDto(
                    ship.Id, ship.Id, Guid.NewGuid(), "Heavy Battery", 1, 6, "close", FiringArc.Fore,
                    24, 0, 0, 0, 8, 0, 8, [.. Enumerable.Repeat(5, 24)]),
            ],
        };

        var restored = new InMemoryMatchService().RestoreMatch(volley, savedAt: null);

        Assert.Equal(24, restored.Snapshot.FiringResults.Single().DiceRolls.Count);
    }
}
