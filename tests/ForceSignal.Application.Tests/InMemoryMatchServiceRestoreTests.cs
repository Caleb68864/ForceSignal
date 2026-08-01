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
