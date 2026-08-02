using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A ship with no order written holds the course and speed it already had - it still travels its
/// full velocity, and it does not hold up the turn. Plotting closes when the players say it does.
/// </summary>
public sealed class InMemoryMatchServiceDriftTests
{
    [Fact]
    public void DeclareOrdersComplete_FromEveryPlayer_OpensMovementWithNothingLocked()
    {
        var table = DriftTable.Build();

        table.Declare(table.OwnerToken);
        var snapshot = table.Declare(table.OpponentToken);

        // Nothing was locked, so there is nothing to reveal.
        Assert.Equal("Movement", snapshot.Phase);
        Assert.Contains(snapshot.MatchLog, entry => entry.Message.Contains("every ship holds course and speed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AdvanceTurn_MovesAnUnorderedShipItsFullVelocityOnItsCurrentCourse()
    {
        var table = DriftTable.Build();
        table.Declare(table.OwnerToken);
        table.Declare(table.OpponentToken);

        var firing = table.AdvanceTurn();

        // Course 6 runs down the table, so eight velocity carries the hull eight units in y.
        var drifter = firing.Ships.Single(ship => ship.Id == table.DrifterId);
        Assert.Equal(20m, drifter.PositionX);
        Assert.Equal(28m, drifter.PositionY);
        Assert.Equal(8, drifter.CurrentVelocity);
        Assert.Equal(6, drifter.CurrentCourse);
        Assert.Contains(firing.MatchLog, entry => entry.Category == "Movement"
            && entry.Message.Contains("had no order and held course", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AdvanceTurn_WithSomeShipsOrderedAndSomeNot_ResolvesBoth()
    {
        var table = DriftTable.Build();

        // The owner plots its ship to come about; the opponent writes nothing at all.
        table.Commit(table.OwnerToken, table.HelmsmanId, new MovementOrder(-2, 2, TurnDirection.Starboard));
        table.Declare(table.OwnerToken);
        table.Declare(table.OpponentToken);
        Assert.Equal("OrdersLocked", table.Snapshot().Phase);

        table.Reveal(table.OwnerToken, table.HelmsmanId, new MovementOrder(-2, 2, TurnDirection.Starboard));
        Assert.Equal("Movement", table.Snapshot().Phase);
        var firing = table.AdvanceTurn();

        var helmsman = firing.Ships.Single(ship => ship.Id == table.HelmsmanId);
        Assert.Equal(2, helmsman.CurrentCourse);
        Assert.Equal(6, helmsman.CurrentVelocity);
        var drifter = firing.Ships.Single(ship => ship.Id == table.DrifterId);
        Assert.Equal(28m, drifter.PositionY);
    }

    [Fact]
    public void AdvanceTurn_LeavesAStoppedShipWhereItIs()
    {
        var table = DriftTable.Build(drifterVelocity: 0);
        table.Declare(table.OwnerToken);
        table.Declare(table.OpponentToken);

        var firing = table.AdvanceTurn();

        var drifter = firing.Ships.Single(ship => ship.Id == table.DrifterId);
        Assert.Equal(20m, drifter.PositionX);
        Assert.Equal(20m, drifter.PositionY);
    }

    [Fact]
    public void DeclareOrdersComplete_WaitsForEveryPlayerWithShipsOnTheTable()
    {
        var table = DriftTable.Build();

        var afterOwner = table.Declare(table.OwnerToken);

        Assert.Equal("OrderEntry", afterOwner.Phase);
        Assert.True(afterOwner.Participants.Single(p => p.Role == "Owner").OrdersComplete);
        Assert.False(afterOwner.Participants.Single(p => p.Role != "Owner").OrdersComplete);
    }

    [Fact]
    public void AdvanceTurn_ReopensPlottingForEveryoneOnTheNextTurn()
    {
        var table = DriftTable.Build();
        table.Declare(table.OwnerToken);
        table.Declare(table.OpponentToken);
        table.AdvanceTurn();

        // Close out the firing phase to reach the next turn's order entry.
        var nextTurn = table.AdvanceTurn();

        Assert.Equal("OrderEntry", nextTurn.Phase);
        Assert.All(nextTurn.Participants, participant => Assert.False(participant.OrdersComplete));
    }

    [Fact]
    public void DeclareOrdersComplete_OutsideOrderEntry_IsRefused()
    {
        var table = DriftTable.Build();
        table.Declare(table.OwnerToken);
        table.Declare(table.OpponentToken);
        table.AdvanceTurn();

        var error = Assert.Throws<InvalidOperationException>(() => table.Declare(table.OwnerToken));

        Assert.Contains("order entry", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One ship a side: the owner's is plotted by hand, the opponent's is left alone.</summary>
    private sealed record DriftTable(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid HelmsmanId,
        Guid DrifterId)
    {
        public static DriftTable Build(int drifterVelocity = 8)
        {
            var service = new InMemoryMatchService(() => 4);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Drift Table"));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            var helmsman = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Helmsman", "Cruiser", 4,
                InitialVelocity: 8, InitialCourse: 12, HullMax: 12, ArmorMax: 0,
                StartX: 20, StartY: 40)).Ships.Single(s => s.Name == "Helmsman");
            var drifter = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Drifter", "Cruiser", 4,
                InitialVelocity: drifterVelocity, InitialCourse: 6, HullMax: 12, ArmorMax: 0,
                StartX: 20, StartY: 20)).Ships.Single(s => s.Name == "Drifter");

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);

            return new DriftTable(service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, helmsman.Id, drifter.Id);
        }

        public MatchSnapshotDto Snapshot() => Service.GetSnapshot(MatchId);

        public MatchSnapshotDto Declare(string token) =>
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(token));

        public MatchSnapshotDto Commit(string token, Guid shipId, MovementOrder order) =>
            Service.CommitOrder(MatchId, new CommitOrderRequest(token, shipId, order, "salt"));

        public MatchSnapshotDto Reveal(string token, Guid shipId, MovementOrder order) =>
            Service.RevealOrder(MatchId, new RevealOrderRequest(token, shipId, order, "salt"));

        public MatchSnapshotDto AdvanceTurn() => Service.AdvanceTurn(MatchId, OwnerToken);
    }
}
