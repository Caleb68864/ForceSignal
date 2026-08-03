using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Flight operations cost a carrier its manoeuvre: it must hold course and speed on any turn it
/// launches or recovers. Bays cap how many groups it can hold and how many it can work in a turn,
/// and a bay lost to a threshold check takes whatever was sitting in it.
/// </summary>
public sealed class InMemoryMatchServiceCarrierOperationsTests
{
    [Fact]
    public void UpdateFighterOperations_LaunchesFromAnUnorderedCarrier()
    {
        // No order at all means the carrier is holding course and speed, which is what a launch needs.
        var table = CarrierTable.Build();

        var result = table.Launch();

        var group = result.Ships.Single(ship => ship.Id == table.GroupId);
        Assert.Equal("Airborne", group.FighterStatus);
        // The group deploys on the carrier.
        Assert.Equal(20m, group.PositionX);
        Assert.Equal(40m, group.PositionY);
        Assert.Contains(result.MatchLog, entry => entry.Category == "Fighters"
            && entry.Message.Contains("launched from Home Plate", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFighterOperations_LaunchesFromACarrierPlottedToHoldCourse()
    {
        var table = CarrierTable.Build();
        table.OrderCarrier(new MovementOrder(0, 0, TurnDirection.None));

        var result = table.Launch();

        Assert.Equal("Airborne", result.Ships.Single(ship => ship.Id == table.GroupId).FighterStatus);
    }

    [Fact]
    public void UpdateFighterOperations_RefusesALaunchFromAManoeuvringCarrier()
    {
        var table = CarrierTable.Build();
        table.OrderCarrier(new MovementOrder(2, 1, TurnDirection.Starboard));

        var error = Assert.Throws<InvalidOperationException>(() => table.Launch());

        Assert.Contains("must hold course and speed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateFighterOperations_RefusesALaunchWhileTheCarriersOrderIsStillSealed()
    {
        // Whether the carrier is manoeuvring is hidden until it reveals, so the answer is "not yet".
        var table = CarrierTable.Build();
        table.CommitCarrierOrder(new MovementOrder(0, 0, TurnDirection.None));

        var error = Assert.Throws<InvalidOperationException>(() => table.Launch());

        Assert.Contains("still sealed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateFighterOperations_LetsATrueCarrierWorkTwoGroupsATurnAndNoMore()
    {
        var table = CarrierTable.Build(bays: 3, secondGroup: true, thirdGroup: true);

        table.Launch();
        table.Launch(secondGroup: true);
        var error = Assert.Throws<InvalidOperationException>(() => table.Launch(thirdGroup: true));

        Assert.Contains("already handled 2 groups this turn", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateFighterOperations_LetsAWarshipWithABayWorkOnlyOneGroupATurn()
    {
        var table = CarrierTable.Build(bays: 2, secondGroup: true, carrierIsWarship: true);

        table.Launch();
        var error = Assert.Throws<InvalidOperationException>(() => table.Launch(secondGroup: true));

        Assert.Contains("already handled 1 group this turn", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateFighterOperations_RecoversAGroupThatCanMakeTheRendezvous()
    {
        var table = CarrierTable.Build();
        table.Launch();
        table.NextTurn();
        table.Fly(x: 26, y: 34);
        table.NextTurn();

        var result = table.Recover();

        var group = result.Ships.Single(ship => ship.Id == table.GroupId);
        Assert.Equal("Docked", group.FighterStatus);
        Assert.Equal(20m, group.PositionX);
        Assert.Contains(result.MatchLog, entry => entry.Message.Contains("landed aboard Home Plate", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFighterOperations_RefusesARecoveryTheGroupCannotReach()
    {
        var table = CarrierTable.Build();
        table.Launch();
        table.NextTurn();
        // Two turns of flying puts the group well beyond a single turn's rendezvous.
        table.Fly(x: 26, y: 30);
        table.NextTurn();
        table.Fly(x: 30, y: 22);
        table.NextTurn();

        var error = Assert.Throws<InvalidOperationException>(() => table.Recover());

        Assert.Contains("too far to make the rendezvous", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateFighterOperations_RefusesARecoveryIntoAFullDeck()
    {
        // One bay, and the second group is already sitting in it.
        var table = CarrierTable.Build(bays: 1, secondGroup: true);
        table.Launch();
        table.NextTurn();

        var error = Assert.Throws<InvalidOperationException>(() => table.Recover());

        Assert.Contains("bay and they are full", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateFighterOperations_RefusesFlightOperationsWithNoWorkingBays()
    {
        var table = CarrierTable.Build(bays: 0);

        var error = Assert.Throws<InvalidOperationException>(() => table.Launch());

        Assert.Contains("no working fighter bays", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThresholdCheck_CanDestroyABayAndTheGroupSittingInIt()
    {
        // Every threshold die comes up 1, so the carrier loses everything it rolls for - including
        // its single bay, with the docked group still aboard.
        var table = CarrierTable.Build(bays: 1, damageDie: 1);

        var result = table.ShootTheCarrierIntoAThreshold();

        var carrier = result.Ships.Single(ship => ship.Id == table.CarrierId);
        Assert.Equal(0, carrier.FighterBays);
        Assert.True(result.Ships.Single(ship => ship.Id == table.GroupId).IsDestroyed);
        Assert.Contains(result.MatchLog, entry => entry.Category == "Threshold"
            && entry.Message.Contains("fighter bay destroyed with Hawk Flight aboard", StringComparison.Ordinal));
    }

    /// <summary>A carrier with its groups aboard, and an enemy cruiser in a position to shoot it.</summary>
    private sealed record CarrierTable(
        InMemoryMatchService Service,
        ScriptedDice Dice,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid CarrierId,
        Guid GroupId,
        Guid SecondGroupId,
        Guid ThirdGroupId,
        Guid EnemyId,
        Guid EnemyMount)
    {
        public static CarrierTable Build(
            int bays = 2,
            bool secondGroup = false,
            bool thirdGroup = false,
            bool carrierIsWarship = false,
            int damageDie = 4)
        {
            var dice = new ScriptedDice { Fallback = damageDie };
            var service = new InMemoryMatchService(dice.Next);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Carrier Table"));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            var carrier = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Home Plate", carrierIsWarship ? "Battleship" : "Fleet Carrier", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 20, ArmorMax: 0,
                StartX: 20, StartY: 40,
                IconKey: carrierIsWarship ? "capital" : "carrier",
                FireControlMax: 1,
                FighterBays: bays)).Ships.Single(s => s.Name == "Home Plate");

            var groupIds = new List<Guid>();
            foreach (var name in new[] { "Hawk Flight", "Kite Flight", "Gull Flight" }.Take(1 + (secondGroup ? 1 : 0) + (thirdGroup ? 1 : 0)))
            {
                groupIds.Add(service.CreateShip(blueFleet.Id, new CreateShipRequest(
                    owner.ParticipantToken, name, "Fighter Group", 6,
                    InitialVelocity: 0, InitialCourse: 12, HullMax: 6, ArmorMax: 0,
                    StartX: 20, StartY: 40,
                    IconKey: "fighter-group",
                    FighterEnduranceMax: 6,
                    HomeCarrierShipId: carrier.Id)).Ships.Single(s => s.Name == name).Id);
            }

            var enemyMount = Guid.NewGuid();
            var enemy = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Raider", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: 20, ArmorMax: 0,
                StartX: 20, StartY: 28, FireControlMax: 1,
                Weapons: [new WeaponMountDto(enemyMount, "Class-3 Beam", 3, 36, [FiringArc.Fore])]))
                .Ships.Single(s => s.Name == "Raider");

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);

            return new CarrierTable(
                service, dice, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken,
                carrier.Id, groupIds[0],
                groupIds.Count > 1 ? groupIds[1] : Guid.Empty,
                groupIds.Count > 2 ? groupIds[2] : Guid.Empty,
                enemy.Id, enemyMount);
        }

        private Guid GroupFor(bool second, bool third) => third ? ThirdGroupId : second ? SecondGroupId : GroupId;

        public MatchSnapshotDto Launch(bool secondGroup = false, bool thirdGroup = false) =>
            SetStatus(GroupFor(secondGroup, thirdGroup), "Airborne");

        public MatchSnapshotDto Recover(bool secondGroup = false) =>
            SetStatus(GroupFor(secondGroup, false), "Docked");

        private MatchSnapshotDto SetStatus(Guid groupId, string status) =>
            Service.UpdateFighterOperations(groupId, new UpdateFighterOperationsRequest(
                OwnerToken, status, 0, 6, 24, CarrierId));

        public MatchSnapshotDto Fly(decimal x, decimal y) =>
            Service.MoveFighterGroup(MatchId, new MoveFighterGroupRequest(OwnerToken, GroupId, x, y));

        /// <summary>
        /// Plots and reveals the whole table's orders, so the carrier's manoeuvre is known. Orders
        /// cannot be revealed piecemeal - which is why a launch declared before the reveal is told to
        /// wait rather than guessed at.
        /// </summary>
        public void OrderCarrier(MovementOrder order)
        {
            var hold = new MovementOrder(0, 0, TurnDirection.None);
            Service.CommitOrder(MatchId, new CommitOrderRequest(OwnerToken, CarrierId, order, "carrier"));
            Service.CommitOrder(MatchId, new CommitOrderRequest(OpponentToken, EnemyId, hold, "raider"));
            Service.RevealOrder(MatchId, new RevealOrderRequest(OwnerToken, CarrierId, order, "carrier"));
            Service.RevealOrder(MatchId, new RevealOrderRequest(OpponentToken, EnemyId, hold, "raider"));
        }

        /// <summary>Plots an order and leaves it sealed.</summary>
        public void CommitCarrierOrder(MovementOrder order) =>
            Service.CommitOrder(MatchId, new CommitOrderRequest(OwnerToken, CarrierId, order, "carrier"));

        /// <summary>Runs the turn out to the next order-entry phase.</summary>
        public void NextTurn()
        {
            var phase = Service.GetSnapshot(MatchId).Phase;
            if (phase is "OrderEntry" or "OrdersLocked" or "Reveal")
            {
                Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OwnerToken));
                Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OpponentToken));
            }

            if (Service.GetSnapshot(MatchId).Phase == "Movement")
            {
                Service.AdvanceTurn(MatchId, OwnerToken);
            }

            Service.AdvanceTurn(MatchId, OwnerToken);
        }

        /// <summary>Puts enough beam damage into the carrier to complete a hull row.</summary>
        public MatchSnapshotDto ShootTheCarrierIntoAThreshold()
        {
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OwnerToken));
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OpponentToken));
            var firing = Service.AdvanceTurn(MatchId, OwnerToken);
            if (firing.FiringParticipantId != Service.GetSnapshot(MatchId).Participants.Single(p => p.Role != "Owner").Id)
            {
                // Hand the turn to the enemy so it can shoot.
                Service.CeaseFire(MatchId, new CeaseFireRequest(OwnerToken, CarrierId));
            }

            // A 20-box hull runs in rows of five, so six 6s put it past the first row.
            Dice.Script(6, 6, 6);
            Service.FireWeapon(MatchId, new FireWeaponRequest(OpponentToken, EnemyId, CarrierId, EnemyMount, 12));
            return Service.CeaseFire(MatchId, new CeaseFireRequest(OpponentToken, EnemyId));
        }
    }
}
