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
