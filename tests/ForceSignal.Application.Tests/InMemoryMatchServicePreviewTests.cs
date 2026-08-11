using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Covers the plotting preview. The preview exists so the client never has to work out where a
/// draft order lands: the answer has to come from the same resolver the turn will actually be
/// flown by, or the ship arrives somewhere the player was not shown.
/// </summary>
public sealed class InMemoryMatchServicePreviewTests
{
    [Fact]
    public void PreviewOrder_LandsWhereTheTurnActuallyPutsTheShip()
    {
        var table = PreviewTable.Create();
        table.MarkBothReady();
        var order = new MovementOrder(2, 0, TurnDirection.None, [new TurnManeuver(TurnDirection.Starboard, 2)]);

        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(table.OwnerToken, table.Ship.Id, order));

        Assert.True(preview.IsValid);
        Assert.Empty(preview.Errors);

        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.Ship.Id, order, "salt"));
        table.CloseOrders();
        table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.Ship.Id, order, "salt"));
        var moved = table.Service.AdvanceTurn(table.MatchId, table.OwnerToken).Ships.Single(s => s.Id == table.Ship.Id);

        var endpoint = preview.Path[^1];
        Assert.Equal(moved.PositionX, endpoint.X);
        Assert.Equal(moved.PositionY, endpoint.Y);
        Assert.Equal(moved.CurrentVelocity, preview.EndingVelocity);
        Assert.Equal(moved.CurrentCourse, preview.EndingCourse);
    }

    [Fact]
    public void PreviewOrder_PutsTheShipWhereTheGeometrySays()
    {
        // Pinned to worked-out numbers rather than only to agreement with resolution. The
        // round-trip test above compares the preview with the move, so a change that moves both -
        // the rounding, the trigonometry, the clamp - keeps them equal and passes. Mutation testing
        // found exactly that: rounding positions to whole units instead of thousandths broke no
        // test in the suite.
        //
        // Blue Lead sits at 20,24 on course 3 at velocity 6. Course 3 is a quarter turn clockwise
        // from straight up, so a leg of 6 runs due right: 26,24 and no change in depth.
        var table = PreviewTable.Create();

        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OwnerToken,
            table.Ship.Id,
            new MovementOrder(0, 0, TurnDirection.None)));

        Assert.Equal(26m, preview.Path[^1].X);
        Assert.Equal(24m, preview.Path[^1].Y);
    }

    [Fact]
    public void PreviewOrder_KeepsPositionsToAThousandthOfAUnit()
    {
        // The rounding exists so a course whose sine is not exactly zero does not accumulate into
        // 20.000000000000001 on a table measured in whole units. Rounding harder would silently
        // snap every ship to a grid it does not play on.
        var table = PreviewTable.Create();

        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OwnerToken,
            table.Ship.Id,
            new MovementOrder(0, 0, TurnDirection.None, [new TurnManeuver(TurnDirection.Starboard, 1)])));

        // A one-point turn ends on course 4, which is 120 degrees: a leg with a fractional
        // component in both axes. Whole-unit rounding would make both of these integers.
        var end = preview.Path[^1];
        Assert.NotEqual(decimal.Round(end.X, 0), end.X);
        Assert.Equal(decimal.Round(end.X, 3), end.X);
    }

    [Fact]
    public void PreviewOrder_WalksTheSameSegmentsTheMoveIsResolvedInto()
    {
        var table = PreviewTable.Create();
        table.MarkBothReady();
        // Thrust 4 caps a turn at 2 points, which is still enough to split across the move.
        var order = new MovementOrder(0, 0, TurnDirection.None, [new TurnManeuver(TurnDirection.Starboard, 2)]);

        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(table.OwnerToken, table.Ship.Id, order));

        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.Ship.Id, order, "salt"));
        table.CloseOrders();
        var revealed = table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.Ship.Id, order, "salt"));
        var result = revealed.MovementResults.Single(r => r.ShipId == table.Ship.Id);

        Assert.Equal(result.Segments!.Count, preview.Segments.Count);
        Assert.Equal(result.Segments, preview.Segments);
        // A two-point turn pivots one at the start and one at the mid-point, so the path is the
        // starting position plus one point per leg flown.
        Assert.Equal(preview.Segments.Count + 1, preview.Path.Count);
    }

    [Fact]
    public void PreviewOrder_StartsThePathWhereTheShipIsNow()
    {
        var table = PreviewTable.Create();

        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OwnerToken,
            table.Ship.Id,
            new MovementOrder(1, 0, TurnDirection.None)));

        Assert.Equal(table.Ship.PositionX, preview.Path[0].X);
        Assert.Equal(table.Ship.PositionY, preview.Path[0].Y);
        Assert.Equal(table.Ship.CurrentVelocity, preview.StartingVelocity);
        Assert.Equal(table.Ship.CurrentCourse, preview.StartingCourse);
    }

    [Fact]
    public void PreviewOrder_ChangesNothingAboutTheMatch()
    {
        var table = PreviewTable.Create();
        var before = table.Service.GetSnapshot(table.MatchId);

        table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OwnerToken,
            table.Ship.Id,
            new MovementOrder(3, 0, TurnDirection.None, [new TurnManeuver(TurnDirection.Port, 1)])));

        var after = table.Service.GetSnapshot(table.MatchId);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.MatchLog.Count, after.MatchLog.Count);
        Assert.Equal(before.OrderStatuses.Count, after.OrderStatuses.Count);
        var ship = after.Ships.Single(s => s.Id == table.Ship.Id);
        Assert.Equal(table.Ship.PositionX, ship.PositionX);
        Assert.Equal(table.Ship.CurrentVelocity, ship.CurrentVelocity);
    }

    [Fact]
    public void PreviewOrder_ExplainsAnIllegalOrderInsteadOfThrowing()
    {
        var table = PreviewTable.Create();

        // Thrust 4: spending 4 on velocity leaves nothing for a turn.
        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OwnerToken,
            table.Ship.Id,
            new MovementOrder(4, 0, TurnDirection.None, [new TurnManeuver(TurnDirection.Port, 2)])));

        Assert.False(preview.IsValid);
        Assert.NotEmpty(preview.Errors);
        // The plot is still walked, so the map can show where the illegal draft would have gone.
        Assert.NotEmpty(preview.Path);
    }

    [Fact]
    public void PreviewOrder_ReportsTheThrustBudgetTheClientWouldOtherwiseCompute()
    {
        var table = PreviewTable.Create();

        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OwnerToken,
            table.Ship.Id,
            new MovementOrder(1, 0, TurnDirection.None, [new TurnManeuver(TurnDirection.Starboard, 1)])));

        Assert.Equal(4, preview.UsableThrust);
        Assert.Equal(2, preview.ThrustSpent);
        // Half thrust rounded up is 2, and 3 of the 4 points are still unspent, so 2 is the cap.
        Assert.Equal(2, preview.MaxTurnSteps);
    }

    [Fact]
    public void PreviewOrder_CountsDriveDamageAgainstTheThrustBudget()
    {
        var table = PreviewTable.Create();
        table.Service.UpdateShipDamage(table.Ship.Id, new UpdateShipDamageRequest(
            table.OwnerToken,
            HullDamage: 0,
            ArmorDamage: 0,
            DriveDamage: 2,
            WeaponDamage: 0,
            ScreenDamage: 0,
            FireControlDamage: 0));

        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OwnerToken,
            table.Ship.Id,
            new MovementOrder(0, 0, TurnDirection.None)));

        Assert.Equal(2, preview.UsableThrust);
        Assert.Equal(1, preview.MaxTurnSteps);
    }

    [Fact]
    public void PreviewOrder_FlagsAPlotThatRunsOffTheTable()
    {
        var table = PreviewTable.Create();

        // Course 12 is straight up the table from y=4 at speed 9: the ship hits the edge.
        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OwnerToken,
            table.EdgeRunner.Id,
            new MovementOrder(0, 0, TurnDirection.None)));

        Assert.True(preview.RunsOffTable);
        Assert.Equal(0, preview.Path[^1].Y);
    }

    [Fact]
    public void PreviewOrder_DoesNotFlagAPlotThatStaysOnTheTable()
    {
        var table = PreviewTable.Create();

        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OwnerToken,
            table.Ship.Id,
            new MovementOrder(0, 0, TurnDirection.None)));

        Assert.False(preview.RunsOffTable);
    }

    [Fact]
    public void PreviewOrder_RefusesAShipTheCallerDoesNotOwn()
    {
        var table = PreviewTable.Create();

        Assert.Throws<UnauthorizedAccessException>(() => table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OpponentToken,
            table.Ship.Id,
            new MovementOrder(1, 0, TurnDirection.None))));
    }

    [Fact]
    public void PreviewOrder_RefusesAnUnknownParticipant()
    {
        var table = PreviewTable.Create();

        Assert.Throws<UnauthorizedAccessException>(() => table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            "not-a-token",
            table.Ship.Id,
            new MovementOrder(1, 0, TurnDirection.None))));
    }

    [Fact]
    public void PreviewOrder_SaysAFighterGroupIsFlownRatherThanPlotted()
    {
        var table = PreviewTable.Create();

        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OwnerToken,
            table.FighterGroup.Id,
            new MovementOrder(1, 0, TurnDirection.None)));

        Assert.False(preview.IsValid);
        Assert.Contains(preview.Errors, e => e.Contains("fighter group", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreviewOrder_WorksBeforeOrderEntryOpens()
    {
        var table = PreviewTable.Create();
        Assert.Equal("FleetSetup", table.Service.GetSnapshot(table.MatchId).Phase);

        var preview = table.Service.PreviewOrder(table.MatchId, new PreviewOrderRequest(
            table.OwnerToken,
            table.Ship.Id,
            new MovementOrder(1, 0, TurnDirection.None)));

        Assert.True(preview.IsValid);
    }

    private sealed record PreviewTable(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        ShipDto Ship,
        ShipDto EdgeRunner,
        ShipDto FighterGroup)
    {
        public static PreviewTable Create()
        {
            var service = new InMemoryMatchService();
            var owner = service.CreateMatch(new CreateMatchRequest("Blue Admiral", "Preview Test", Rules: TestRules.Invented));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red Admiral"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", "Test"))
                .Fleets.Single(fleet => fleet.OwnerParticipantId == owner.ParticipantId);
            service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", "Test"));

            var ship = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken,
                "Blue Lead",
                "Cruiser",
                ThrustRating: 4,
                InitialVelocity: 6,
                InitialCourse: 3,
                HullMax: 12,
                ArmorMax: 0,
                StartX: 20,
                StartY: 24)).Ships.Single(s => s.Name == "Blue Lead");

            var edgeRunner = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken,
                "Edge Runner",
                "Escort",
                ThrustRating: 4,
                InitialVelocity: 9,
                InitialCourse: 12,
                HullMax: 6,
                ArmorMax: 0,
                StartX: 20,
                StartY: 4)).Ships.Single(s => s.Name == "Edge Runner");

            var fighterGroup = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken,
                "Alpha Flight",
                "Fighter Group",
                ThrustRating: 0,
                InitialVelocity: 0,
                InitialCourse: 12,
                HullMax: 6,
                ArmorMax: 0,
                StartX: 20,
                StartY: 26,
                IconKey: "fighter-group")).Ships.Single(s => s.Name == "Alpha Flight");

            return new PreviewTable(service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, ship, edgeRunner, fighterGroup);
        }

        /// <summary>Opens order entry.</summary>
        public void MarkBothReady()
        {
            Service.SetReady(MatchId, OwnerToken, true);
            Assert.Equal("OrderEntry", Service.SetReady(MatchId, OpponentToken, true).Phase);
        }

        /// <summary>
        /// Closes plotting with only the named ship under orders. Every other hull holds its course,
        /// which is what the drift rule is for and keeps these tests to one moving ship.
        /// </summary>
        public void CloseOrders()
        {
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OwnerToken));
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OpponentToken));
        }
    }
}
