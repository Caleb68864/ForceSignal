using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;

namespace ForceSignal.Application.Tests;

public sealed class InMemoryMatchServicePointsTests
{
    [Fact]
    public void PointsLimit_BlocksAnOverStrengthFleetFromReadyingUp()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Points Match", Rules: TestRules.Invented));
        var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
        var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
            .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
            .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        service.CreateShip(blueFleet.Id, Ship(owner.ParticipantToken, "Valiant", 120));
        service.CreateShip(blueFleet.Id, Ship(owner.ParticipantToken, "Vigil", 80));
        service.CreateShip(redFleet.Id, Ship(opponent.ParticipantToken, "Crimson", 540));

        var limited = service.UpdatePointsLimit(owner.MatchId, new UpdateMatchPointsLimitRequest(owner.ParticipantToken, 200));
        Assert.Equal(200, limited.PointsLimit);
        Assert.Contains(limited.MatchLog, e => e.Message.Contains("Points limit set to 200", StringComparison.Ordinal));

        // Blue is exactly on the limit and may ready.
        service.SetReady(owner.MatchId, owner.ParticipantToken, true);

        // Red is 340 over and cannot.
        var blocked = Assert.Throws<InvalidOperationException>(() =>
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true));
        Assert.Contains("340 points over", blocked.Message, StringComparison.OrdinalIgnoreCase);

        // The owner agreeing to a mismatch is the escape hatch.
        service.UpdatePointsLimit(owner.MatchId, new UpdateMatchPointsLimitRequest(owner.ParticipantToken, 0));
        var started = service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
        Assert.Equal("OrderEntry", started.Phase);
        Assert.Equal(0, started.PointsLimit);
    }

    [Fact]
    public void PointsLimit_IsOwnerOnlyAndClampedIntoRange()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Owner Only", Rules: TestRules.Invented));
        var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));

        Assert.Throws<UnauthorizedAccessException>(() =>
            service.UpdatePointsLimit(owner.MatchId, new UpdateMatchPointsLimitRequest(opponent.ParticipantToken, 500)));

        var negative = service.UpdatePointsLimit(owner.MatchId, new UpdateMatchPointsLimitRequest(owner.ParticipantToken, -50));
        Assert.Equal(0, negative.PointsLimit);
        var huge = service.UpdatePointsLimit(owner.MatchId, new UpdateMatchPointsLimitRequest(owner.ParticipantToken, 999999));
        Assert.Equal(99999, huge.PointsLimit);
    }

    [Fact]
    public void ShipPointsValue_RoundTripsThroughCreateEditDuplicateAndRestore()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "NPV Round Trip", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        var created = service.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant", 84)).Ships.Single();
        Assert.Equal(84, created.PointsValue);

        var edited = service.UpdateShipProfile(created.Id, new UpdateShipProfileRequest(
            owner.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4,
            PositionX: 20, PositionY: 24, PointsValue: 96)).Ships.Single();
        Assert.Equal(96, edited.PointsValue);

        var duplicated = service.DuplicateShip(created.Id, new DuplicateShipRequest(owner.ParticipantToken, "Valiant II"));
        Assert.Equal(96, duplicated.Ships.Single(s => s.Name == "Valiant II").PointsValue);

        service.UpdatePointsLimit(owner.MatchId, new UpdateMatchPointsLimitRequest(owner.ParticipantToken, 300));
        var exported = service.GetSnapshot(owner.MatchId);
        var restored = new InMemoryMatchService().RestoreMatch(exported, null).Snapshot;

        Assert.Equal(300, restored.PointsLimit);
        Assert.Equal([96, 96], restored.Ships.Select(s => s.PointsValue).Order().ToArray());
    }

    private static CreateShipRequest Ship(string token, string name, int points) => new(
        token, name, "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24, PointsValue: points);
}
