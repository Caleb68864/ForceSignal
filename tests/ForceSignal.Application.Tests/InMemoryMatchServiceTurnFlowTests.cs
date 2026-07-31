using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

public sealed class InMemoryMatchServiceTurnFlowTests
{
    [Fact]
    public void SoloLocalMatch_CanRunAFullTurnWithoutASecondParticipant()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Solo Admiral", "Single Device"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Home Watch", null)).Fleets.Single();
        var ship = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Lone Star",
            "Cruiser",
            4,
            6,
            3,
            12,
            2,
            StartX: 20,
            StartY: 24)).Ships.Single();

        var orderEntry = service.SetReady(owner.MatchId, owner.ParticipantToken, true);
        Assert.Equal("OrderEntry", orderEntry.Phase);

        var order = new MovementOrder(1, 1, TurnDirection.Starboard, [new TurnManeuver(TurnDirection.Starboard, 1)]);
        Assert.Equal("OrdersLocked", service.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, ship.Id, order, "solo-salt")).Phase);
        Assert.Equal("Movement", service.RevealOrder(owner.MatchId, new RevealOrderRequest(owner.ParticipantToken, ship.Id, order, "solo-salt")).Phase);

        var firing = service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);
        Assert.Equal("Firing", firing.Phase);
        var moved = firing.Ships.Single();
        Assert.Equal(7, moved.CurrentVelocity);
        Assert.Equal(4, moved.CurrentCourse);

        var nextTurn = service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);
        Assert.Equal("OrderEntry", nextTurn.Phase);
        Assert.Equal(2, nextTurn.TurnNumber);
    }

    [Fact]
    public void CommitOrder_DuringFleetSetup_IsRejected()
    {
        var table = TestMatch.Create();

        var error = Assert.Throws<InvalidOperationException>(() =>
            table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(
                table.OwnerToken,
                table.BlueLead.Id,
                new MovementOrder(0, 0, TurnDirection.None),
                "salt")));

        Assert.Contains("order entry", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RevealOrder_BeforeEveryLiveShipLocks_IsRejected()
    {
        var table = TestMatch.Create();
        table.MarkBothReady();
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.BlueLead.Id, Drift, "blue-salt"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.BlueLead.Id, Drift, "blue-salt")));

        Assert.Contains("revealed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedReveal_CanBeRepairedByRelockingWithoutDeadlockingTheTurn()
    {
        var table = TestMatch.Create();
        table.MarkBothReady();
        var accelerate = new MovementOrder(1, 0, TurnDirection.None);
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.BlueLead.Id, accelerate, "blue-salt"));
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.BlueEscort.Id, Drift, "escort-salt"));
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OpponentToken, table.RedLead.Id, Drift, "red-salt"));
        table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OpponentToken, table.RedLead.Id, Drift, "red-salt"));

        // Reveal an order that does not match the commitment: verification must fail, not throw.
        var mismatched = table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.BlueLead.Id, Drift, "blue-salt"));
        Assert.Equal("Reveal", mismatched.Phase);
        var failedStatus = mismatched.OrderStatuses.Single(status => status.ShipId == table.BlueLead.Id);
        Assert.True(failedStatus.VerificationFailed);
        Assert.False(failedStatus.IsRevealed);
        Assert.Contains(mismatched.MatchLog, entry => entry.Category == "Orders" && entry.Message.Contains("did not match", StringComparison.OrdinalIgnoreCase));

        // The turn must remain recoverable: re-lock the mismatched ship and reveal again.
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.BlueLead.Id, accelerate, "blue-salt-2"));
        table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.BlueLead.Id, accelerate, "blue-salt-2"));
        table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.BlueEscort.Id, Drift, "escort-salt"));

        var firing = table.Service.AdvanceTurn(table.MatchId, table.OwnerToken);
        Assert.Equal("Firing", firing.Phase);
    }

    [Fact]
    public void CommitOrder_UsesThrustLeftAfterDriveDamage()
    {
        var table = TestMatch.Create();
        table.MarkBothReady();
        // Blue Lead has thrust 4; three drive hits leave 1 thrust point.
        table.Service.UpdateShipDamage(table.BlueLead.Id, new UpdateShipDamageRequest(table.OwnerToken, 0, 0, 0, 3, 0));

        var overspend = Assert.Throws<InvalidOperationException>(() =>
            table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(
                table.OwnerToken,
                table.BlueLead.Id,
                new MovementOrder(2, 0, TurnDirection.None),
                "blue-salt")));
        Assert.Contains("thrust", overspend.Message, StringComparison.OrdinalIgnoreCase);

        var locked = table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(
            table.OwnerToken,
            table.BlueLead.Id,
            new MovementOrder(1, 0, TurnDirection.None),
            "blue-salt"));
        Assert.True(locked.OrderStatuses.Single(status => status.ShipId == table.BlueLead.Id).IsCommitted);
    }

    [Fact]
    public void AdvanceTurn_DiscardsOrdersForShipsDestroyedBeforeMovement()
    {
        var table = TestMatch.Create();
        table.MarkBothReady();
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.BlueLead.Id, Drift, "blue-salt"));
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.BlueEscort.Id, new MovementOrder(2, 0, TurnDirection.None), "escort-salt"));
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OpponentToken, table.RedLead.Id, Drift, "red-salt"));
        table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.BlueLead.Id, Drift, "blue-salt"));
        table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.BlueEscort.Id, new MovementOrder(2, 0, TurnDirection.None), "escort-salt"));
        table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OpponentToken, table.RedLead.Id, Drift, "red-salt"));
        table.Service.UpdateShipDamage(table.BlueEscort.Id, new UpdateShipDamageRequest(table.OwnerToken, table.BlueEscort.HullMax, 0, 0, 0, 0));

        var firing = table.Service.AdvanceTurn(table.MatchId, table.OwnerToken);

        var wreck = firing.Ships.Single(ship => ship.Id == table.BlueEscort.Id);
        Assert.Equal(table.BlueEscort.CurrentVelocity, wreck.CurrentVelocity);
        Assert.Contains(firing.MatchLog, entry => entry.Message.Contains("destroyed before movement", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DestroyedShips_DoNotBlockLockAndRevealGates()
    {
        var table = TestMatch.Create();
        table.MarkBothReady();
        var wreckSnapshot = table.Service.UpdateShipDamage(table.BlueEscort.Id, new UpdateShipDamageRequest(
            table.OwnerToken,
            table.BlueEscort.HullMax,
            0,
            0,
            0,
            0));
        Assert.True(wreckSnapshot.Ships.Single(ship => ship.Id == table.BlueEscort.Id).IsDestroyed);

        var wreckOrder = Assert.Throws<InvalidOperationException>(() =>
            table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.BlueEscort.Id, Drift, "wreck-salt")));
        Assert.Contains("destroyed", wreckOrder.Message, StringComparison.OrdinalIgnoreCase);

        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.BlueLead.Id, Drift, "blue-salt"));
        var locked = table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OpponentToken, table.RedLead.Id, Drift, "red-salt"));
        Assert.Equal("OrdersLocked", locked.Phase);

        table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.BlueLead.Id, Drift, "blue-salt"));
        var revealed = table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OpponentToken, table.RedLead.Id, Drift, "red-salt"));
        Assert.Equal("Movement", revealed.Phase);

        Assert.Equal("Firing", table.Service.AdvanceTurn(table.MatchId, table.OwnerToken).Phase);
    }

    [Fact]
    public void FireWeapon_RejectsDestroyedAttackerAndDestroyedTarget()
    {
        var table = TestMatch.Create();
        table.MarkBothReady();
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.BlueLead.Id, Drift, "blue-salt"));
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OwnerToken, table.BlueEscort.Id, Drift, "escort-salt"));
        table.Service.CommitOrder(table.MatchId, new CommitOrderRequest(table.OpponentToken, table.RedLead.Id, Drift, "red-salt"));
        table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.BlueLead.Id, Drift, "blue-salt"));
        table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OwnerToken, table.BlueEscort.Id, Drift, "escort-salt"));
        table.Service.RevealOrder(table.MatchId, new RevealOrderRequest(table.OpponentToken, table.RedLead.Id, Drift, "red-salt"));
        table.Service.AdvanceTurn(table.MatchId, table.OwnerToken);

        table.Service.UpdateShipDamage(table.RedLead.Id, new UpdateShipDamageRequest(table.OpponentToken, table.RedLead.HullMax, 0, 0, 0, 0));
        var deadTarget = Assert.Throws<InvalidOperationException>(() =>
            table.Service.FireWeapon(table.MatchId, new FireWeaponRequest(table.OwnerToken, table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 6, FiringArc.Fore)));
        Assert.Contains("already destroyed", deadTarget.Message, StringComparison.OrdinalIgnoreCase);

        table.Service.UpdateShipDamage(table.RedLead.Id, new UpdateShipDamageRequest(table.OpponentToken, 0, 0, 0, 0, 0));
        table.Service.UpdateShipDamage(table.BlueLead.Id, new UpdateShipDamageRequest(table.OwnerToken, table.BlueLead.HullMax, 0, 0, 0, 0));
        var deadAttacker = Assert.Throws<InvalidOperationException>(() =>
            table.Service.FireWeapon(table.MatchId, new FireWeaponRequest(table.OwnerToken, table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 6, FiringArc.Fore)));
        Assert.Contains("cannot fire", deadAttacker.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateShip_WithForeignHomeCarrierId_ImportsAsUnassignedInsteadOfFailing()
    {
        var table = TestMatch.Create();

        var snapshot = table.Service.CreateShip(table.BlueFleetId, new CreateShipRequest(
            table.OwnerToken,
            "Alpha Wing",
            "Fighter Group",
            6,
            12,
            1,
            6,
            0,
            IconKey: "fighter-group",
            FighterEnduranceMax: 6,
            FighterMaxRange: 24,
            HomeCarrierShipId: Guid.NewGuid()));

        var fighter = snapshot.Ships.Single(ship => ship.Name == "Alpha Wing");
        Assert.Null(fighter.HomeCarrierShipId);
        Assert.Equal("Airborne", table.Service.UpdateFighterOperations(fighter.Id, new UpdateFighterOperationsRequest(
            table.OwnerToken,
            "Airborne",
            0,
            6,
            24)).Ships.Single(ship => ship.Id == fighter.Id).FighterStatus);
    }

    [Fact]
    public void CreateShip_ClampsThrustRatingToTheSupportedRange()
    {
        var table = TestMatch.Create();

        var snapshot = table.Service.CreateShip(table.BlueFleetId, new CreateShipRequest(
            table.OwnerToken,
            "Overthrust",
            "Cruiser",
            9999,
            0,
            1,
            12,
            0));

        Assert.Equal(20, snapshot.Ships.Single(ship => ship.Name == "Overthrust").ThrustRating);
    }

    [Fact]
    public void IsMatchParticipant_AcceptsIssuedTokensAndRejectsOthers()
    {
        var table = TestMatch.Create();

        Assert.True(table.Service.IsMatchParticipant(table.MatchId, table.OwnerToken));
        Assert.True(table.Service.IsMatchParticipant(table.MatchId, table.OpponentToken));
        Assert.False(table.Service.IsMatchParticipant(table.MatchId, "not-a-token"));
        Assert.False(table.Service.IsMatchParticipant(table.MatchId, string.Empty));
        Assert.False(table.Service.IsMatchParticipant(Guid.NewGuid(), table.OwnerToken));
    }

    private static MovementOrder Drift => new(0, 0, TurnDirection.None);

    private sealed record TestMatch(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid BlueFleetId,
        ShipDto BlueLead,
        ShipDto BlueEscort,
        ShipDto RedLead,
        Guid BlueWeaponId)
    {
        public static TestMatch Create()
        {
            var service = new InMemoryMatchService();
            var owner = service.CreateMatch(new CreateMatchRequest("Blue Admiral", "Turn Flow Test"));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red Admiral"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", "Test"))
                .Fleets.Single(fleet => fleet.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", "Test"))
                .Fleets.Single(fleet => fleet.OwnerParticipantId == opponent.ParticipantId);
            var blueWeaponId = Guid.NewGuid();

            var blueLead = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken,
                "Blue Lead",
                "Cruiser",
                4,
                0,
                1,
                12,
                0,
                StartX: 18,
                StartY: 24,
                Weapons: [new WeaponMountDto(blueWeaponId, "Class-3 Beam", 3, 24, FiringArc.Fore)])).Ships.Single(ship => ship.Name == "Blue Lead");
            var blueEscort = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken,
                "Blue Escort",
                "Escort",
                6,
                0,
                1,
                6,
                0,
                StartX: 20,
                StartY: 24)).Ships.Single(ship => ship.Name == "Blue Escort");
            var redLead = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken,
                "Red Lead",
                "Destroyer",
                4,
                0,
                7,
                10,
                0,
                StartX: 22,
                StartY: 24)).Ships.Single(ship => ship.Name == "Red Lead");

            return new TestMatch(
                service,
                owner.MatchId,
                owner.ParticipantToken,
                opponent.ParticipantToken,
                blueFleet.Id,
                blueLead,
                blueEscort,
                redLead,
                blueWeaponId);
        }

        public void MarkBothReady()
        {
            Service.SetReady(MatchId, OwnerToken, true);
            var snapshot = Service.SetReady(MatchId, OpponentToken, true);
            Assert.Equal("OrderEntry", snapshot.Phase);
        }
    }
}
