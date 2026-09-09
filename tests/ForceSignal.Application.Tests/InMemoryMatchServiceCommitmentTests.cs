using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The commitment scheme is the only thing making a hidden-orders game honest across two devices,
/// and it rests on one assumption that was never enforced: that a ship is where it said it was when
/// the orders were written. <c>UpdateShipProfile</c> writes position, velocity and course, and it
/// had no phase gate at all - so a player could watch the reveal come up and then move.
/// </summary>
public sealed class InMemoryMatchServiceCommitmentTests
{
    private sealed record Table(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        ShipDto BlueShip,
        ShipDto RedShip);

    /// <summary>Two crews, one ship each, ready and into order entry.</summary>
    private static Table Ready()
    {
        var service = new InMemoryMatchService(_ => 6);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue Admiral", "Commitment Test", 72, 48, Rules: TestRules.Invented));
        var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red Admiral"));
        var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue Squadron", "Test"))
            .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red Squadron", "Test"))
            .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

        var blueShip = service.CreateShip(blueFleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Blue One", "Cruiser", 6, 8, 12, 14, 2, StartX: 18, StartY: 30))
            .Ships.Single(s => s.Name == "Blue One");
        var redShip = service.CreateShip(redFleet.Id, new CreateShipRequest(
            opponent.ParticipantToken, "Red One", "Destroyer", 4, 6, 6, 10, 1, StartX: 50, StartY: 18))
            .Ships.Single(s => s.Name == "Red One");

        service.SetReady(owner.MatchId, owner.ParticipantToken, true);
        var entry = service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
        Assert.Equal("OrderEntry", entry.Phase);

        return new Table(service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, blueShip, redShip);
    }

    private static UpdateShipProfileRequest MoveTo(string token, ShipDto ship, decimal x, decimal y) =>
        new(token, ship.Name, ship.ClassName, ship.ThrustRating, ship.CurrentVelocity, ship.CurrentCourse,
            ship.HullMax, ship.ArmorMax, PositionX: x, PositionY: y, ScreenRating: ship.ScreenRating);

    private static UpdateShipProfileRequest RenameTo(string token, ShipDto ship, string name) =>
        new(token, name, ship.ClassName, ship.ThrustRating, ship.CurrentVelocity, ship.CurrentCourse,
            ship.HullMax, ship.ArmorMax, PositionX: ship.PositionX, PositionY: ship.PositionY,
            ScreenRating: ship.ScreenRating);

    [Fact]
    public void APositionMayStillBeCorrectedWhileOrdersAreBeingWritten()
    {
        var t = Ready();

        var moved = t.Service.UpdateShipProfile(t.BlueShip.Id, MoveTo(t.OwnerToken, t.BlueShip, 20, 32));

        var ship = moved.Ships.Single(s => s.Id == t.BlueShip.Id);
        Assert.Equal(20, ship.PositionX);
        Assert.Equal(32, ship.PositionY);
    }

    [Fact]
    public void OnceOrdersAreLockedAShipCannotBeMoved()
    {
        var t = Ready();
        var order = new MovementOrder(0, 0, TurnDirection.None);
        var locked = t.Service.CommitOrder(t.MatchId, new CommitOrderRequest(t.OwnerToken, t.BlueShip.Id, order, "blue-salt"));
        Assert.Equal("OrderEntry", locked.Phase); // the opponent has not locked yet

        var error = Assert.Throws<InvalidOperationException>(
            () => t.Service.UpdateShipProfile(t.BlueShip.Id, MoveTo(t.OwnerToken, t.BlueShip, 40, 40)));

        Assert.Contains("locked", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AShipCannotBeMovedAfterWatchingTheReveal()
    {
        // The defect in its sharpest form: both plots are on the table, and the loser of the
        // exchange repositions before movement is worked out.
        var t = Ready();
        var order = new MovementOrder(0, 0, TurnDirection.None);
        t.Service.CommitOrder(t.MatchId, new CommitOrderRequest(t.OwnerToken, t.BlueShip.Id, order, "blue-salt"));
        t.Service.CommitOrder(t.MatchId, new CommitOrderRequest(t.OpponentToken, t.RedShip.Id, order, "red-salt"));
        t.Service.RevealOrder(t.MatchId, new RevealOrderRequest(t.OwnerToken, t.BlueShip.Id, order, "blue-salt"));
        var revealed = t.Service.RevealOrder(t.MatchId, new RevealOrderRequest(t.OpponentToken, t.RedShip.Id, order, "red-salt"));
        Assert.Equal("Movement", revealed.Phase);

        Assert.Throws<InvalidOperationException>(
            () => t.Service.UpdateShipProfile(t.BlueShip.Id, MoveTo(t.OwnerToken, t.BlueShip, 40, 40)));
    }

    [Fact]
    public void VelocityAndCourseAreFrozenWithThePosition()
    {
        var t = Ready();
        var order = new MovementOrder(0, 0, TurnDirection.None);
        t.Service.CommitOrder(t.MatchId, new CommitOrderRequest(t.OwnerToken, t.BlueShip.Id, order, "blue-salt"));

        var faster = new UpdateShipProfileRequest(
            t.OwnerToken, t.BlueShip.Name, t.BlueShip.ClassName, t.BlueShip.ThrustRating,
            t.BlueShip.CurrentVelocity + 3, t.BlueShip.CurrentCourse, t.BlueShip.HullMax, t.BlueShip.ArmorMax,
            PositionX: t.BlueShip.PositionX, PositionY: t.BlueShip.PositionY, ScreenRating: t.BlueShip.ScreenRating);

        Assert.Throws<InvalidOperationException>(() => t.Service.UpdateShipProfile(t.BlueShip.Id, faster));
    }

    [Fact]
    public void BookkeepingIsStillEditableWhileTheOrderIsLocked()
    {
        // Only the four fields that are the commitment are frozen. A typo noticed mid-turn should
        // not have to wait a turn, and freezing the whole form would be a different bug.
        var t = Ready();
        var order = new MovementOrder(0, 0, TurnDirection.None);
        t.Service.CommitOrder(t.MatchId, new CommitOrderRequest(t.OwnerToken, t.BlueShip.Id, order, "blue-salt"));

        var renamed = t.Service.UpdateShipProfile(t.BlueShip.Id, RenameTo(t.OwnerToken, t.BlueShip, "Blue One (flag)"));

        Assert.Equal("Blue One (flag)", renamed.Ships.Single(s => s.Id == t.BlueShip.Id).Name);
    }

    [Fact]
    public void APositionMayBeCorrectedAgainOnceTheTurnHasAdvanced()
    {
        var t = Ready();
        var order = new MovementOrder(0, 0, TurnDirection.None);
        t.Service.CommitOrder(t.MatchId, new CommitOrderRequest(t.OwnerToken, t.BlueShip.Id, order, "blue-salt"));
        t.Service.CommitOrder(t.MatchId, new CommitOrderRequest(t.OpponentToken, t.RedShip.Id, order, "red-salt"));
        t.Service.RevealOrder(t.MatchId, new RevealOrderRequest(t.OwnerToken, t.BlueShip.Id, order, "blue-salt"));
        t.Service.RevealOrder(t.MatchId, new RevealOrderRequest(t.OpponentToken, t.RedShip.Id, order, "red-salt"));
        t.Service.AdvanceTurn(t.MatchId, t.OwnerToken);

        var current = t.Service.GetSnapshot(t.MatchId).Ships.Single(s => s.Id == t.BlueShip.Id);
        var moved = t.Service.UpdateShipProfile(t.BlueShip.Id, MoveTo(t.OwnerToken, current, 25, 25));

        Assert.Equal(25, moved.Ships.Single(s => s.Id == t.BlueShip.Id).PositionX);
    }
}
