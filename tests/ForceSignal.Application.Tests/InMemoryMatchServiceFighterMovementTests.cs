using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A fighter group takes no written orders. It is flown straight to where it is going, up to its
/// allowance, in any direction - so it never holds up plotting and never drifts on a heading.
/// </summary>
public sealed class InMemoryMatchServiceFighterMovementTests
{
    [Fact]
    public void MoveFighterGroup_FliesTheGroupAndPointsItTheWayItWent()
    {
        var table = FighterMoveTable.Build();

        var result = table.Fly(x: 28, y: 32);

        var group = result.Ships.Single(ship => ship.Id == table.GroupId);
        Assert.Equal(28m, group.PositionX);
        Assert.Equal(32m, group.PositionY);
        // It flew up and to starboard from 20,40, which is course 2 on the clock.
        Assert.Equal(2, group.CurrentCourse);
        Assert.Contains(result.MatchLog, entry => entry.Category == "Fighters"
            && entry.Message.Contains("flew 11.3", StringComparison.Ordinal));
    }

    [Fact]
    public void MoveFighterGroup_MayFlyInAnyDirectionIncludingBackwards()
    {
        // A warship cannot reverse; a group has no course to reverse against.
        var table = FighterMoveTable.Build();

        // Straight back down the table, which the 48-deep table allows as far as its edge.
        var result = table.Fly(x: 20, y: 48);

        var group = result.Ships.Single(ship => ship.Id == table.GroupId);
        Assert.Equal(48m, group.PositionY);
        Assert.Equal(6, group.CurrentCourse);
    }

    [Fact]
    public void MoveFighterGroup_RefusesToFlyPastTheAllowance()
    {
        var table = FighterMoveTable.Build();

        var error = Assert.Throws<InvalidOperationException>(() => table.Fly(x: 60, y: 36));

        Assert.Contains($"past the {TestRules.Invented.FighterMoveAllowance}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveFighterGroup_OnlyOncePerTurn()
    {
        var table = FighterMoveTable.Build();
        table.Fly(x: 24, y: 36);

        var error = Assert.Throws<InvalidOperationException>(() => table.Fly(x: 26, y: 34));

        Assert.Contains("already flown this turn", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveFighterGroup_RefusesAWarship()
    {
        var table = FighterMoveTable.Build();

        var error = Assert.Throws<InvalidOperationException>(() => table.FlyTheCruiser());

        Assert.Contains("not a fighter group", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CommitOrder_RefusesToPlotAFighterGroup()
    {
        var table = FighterMoveTable.Build();

        var error = Assert.Throws<InvalidOperationException>(table.PlotTheGroup);

        Assert.Contains("fly it straight to where it is going", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclareOrdersComplete_DoesNotWaitForFighterGroups()
    {
        // The cruiser is the only ship that needs an order, so plotting closes without the group.
        var table = FighterMoveTable.Build();

        table.Declare(table.OwnerToken);
        var snapshot = table.Declare(table.OpponentToken);

        Assert.Equal("Movement", snapshot.Phase);
    }

    [Fact]
    public void AdvanceTurn_DoesNotDriftAFighterGroup()
    {
        // A group holds position unless it is flown, where an unordered warship holds its course.
        var table = FighterMoveTable.Build();
        table.Declare(table.OwnerToken);
        table.Declare(table.OpponentToken);

        var firing = table.AdvanceTurn();

        var group = firing.Ships.Single(ship => ship.Id == table.GroupId);
        Assert.Equal(20m, group.PositionX);
        Assert.Equal(40m, group.PositionY);
        Assert.DoesNotContain(firing.MatchLog, entry => entry.Category == "Movement"
            && entry.Message.Contains("Hawk Flight", StringComparison.Ordinal));
    }

    [Fact]
    public void MoveFighterGroup_MayFlyAgainOnTheNextTurn()
    {
        var table = FighterMoveTable.Build();
        table.Fly(x: 24, y: 36);
        table.Declare(table.OwnerToken);
        table.Declare(table.OpponentToken);
        table.AdvanceTurn();
        table.AdvanceTurn();

        var result = table.Fly(x: 28, y: 32);

        Assert.Equal(28m, result.Ships.Single(ship => ship.Id == table.GroupId).PositionX);
    }

    [Fact]
    public void MoveFighterGroup_RefusesDuringTheFiringPhase()
    {
        var table = FighterMoveTable.Build();
        table.Declare(table.OwnerToken);
        table.Declare(table.OpponentToken);
        table.AdvanceTurn();

        var error = Assert.Throws<InvalidOperationException>(() => table.Fly(x: 24, y: 36));

        Assert.Contains("between plotting and the firing phase", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A cruiser and a fighter group on one side, a lone enemy cruiser on the other.</summary>
    private sealed record FighterMoveTable(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid CruiserId,
        Guid GroupId)
    {
        public static FighterMoveTable Build()
        {
            var service = new InMemoryMatchService(() => 4);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Fighter Move Table", Rules: TestRules.Invented));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            var cruiser = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Escort", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 12, ArmorMax: 0,
                StartX: 24, StartY: 44)).Ships.Single(s => s.Name == "Escort");
            var group = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Hawk Flight", "Fighter Group", 6,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 6, ArmorMax: 0,
                StartX: 20, StartY: 40,
                IconKey: "fighter-group",
                FighterEnduranceMax: 6)).Ships.Single(s => s.Name == "Hawk Flight");
            service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Mark", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: 12, ArmorMax: 0,
                StartX: 20, StartY: 20));

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);

            return new FighterMoveTable(service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, cruiser.Id, group.Id);
        }

        public MatchSnapshotDto Fly(decimal x, decimal y) =>
            Service.MoveFighterGroup(MatchId, new MoveFighterGroupRequest(OwnerToken, GroupId, x, y));

        public MatchSnapshotDto FlyTheCruiser() =>
            Service.MoveFighterGroup(MatchId, new MoveFighterGroupRequest(OwnerToken, CruiserId, 26, 44));

        public MatchSnapshotDto PlotTheGroup() =>
            Service.CommitOrder(MatchId, new CommitOrderRequest(
                OwnerToken, GroupId, new MovementOrder(0, 0, TurnDirection.None), "salt"));

        public MatchSnapshotDto Declare(string token) =>
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(token));

        public MatchSnapshotDto AdvanceTurn() => Service.AdvanceTurn(MatchId, OwnerToken);
    }
}
