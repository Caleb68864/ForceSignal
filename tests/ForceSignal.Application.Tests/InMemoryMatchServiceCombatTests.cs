using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

public sealed class InMemoryMatchServiceCombatTests
{
    [Fact]
    public void MatchFlow_WithTwoPlayersMultiTurnOrdersFiringAndDamage_WritesCompleteBattleRecord()
    {
        // Every die a 6 so the damage assertions are exact rather than lucky.
        var service = new InMemoryMatchService(() => 6);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue Admiral", "Self Play Test", 72, 48));
        var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red Admiral"));
        var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue Squadron", "Test")).Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red Squadron", "Test")).Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        var blueWeapon = Guid.NewGuid();
        var redWeapon = Guid.NewGuid();

        var blueShip = service.CreateShip(blueFleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Blue One",
            "Cruiser",
            6,
            8,
            12,
            14,
            2,
            StartX: 18,
            StartY: 30,
            ScreenRating: 1,
            Weapons: [new WeaponMountDto(blueWeapon, "Class-3 Beam", 3, 24, [.. FiringArcs.Firable])])).Ships.Single(s => s.Name == "Blue One");
        var redShip = service.CreateShip(redFleet.Id, new CreateShipRequest(
            opponent.ParticipantToken,
            "Red One",
            "Destroyer",
            4,
            6,
            6,
            10,
            1,
            StartX: 50,
            StartY: 18,
            Weapons: [new WeaponMountDto(redWeapon, "Class-2 Beam", 2, 24, [.. FiringArcs.Firable])])).Ships.Single(s => s.Name == "Red One");

        service.SetReady(owner.MatchId, owner.ParticipantToken, true);
        var orderEntry = service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
        Assert.Equal("OrderEntry", orderEntry.Phase);

        var blueOrder = new MovementOrder(
            1,
            3,
            TurnDirection.None,
            [
                new TurnManeuver(TurnDirection.Starboard, 1),
                new TurnManeuver(TurnDirection.Port, 1),
                new TurnManeuver(TurnDirection.Starboard, 1)
            ]);
        var redOrder = new MovementOrder(-1, 1, TurnDirection.Port);

        service.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, blueShip.Id, blueOrder, "blue-salt"));
        var committed = service.CommitOrder(owner.MatchId, new CommitOrderRequest(opponent.ParticipantToken, redShip.Id, redOrder, "red-salt"));
        Assert.Equal(2, committed.OrderStatuses.Count(status => status.IsCommitted));

        service.RevealOrder(owner.MatchId, new RevealOrderRequest(owner.ParticipantToken, blueShip.Id, blueOrder, "blue-salt"));
        var movement = service.RevealOrder(owner.MatchId, new RevealOrderRequest(opponent.ParticipantToken, redShip.Id, redOrder, "red-salt"));
        Assert.Equal("Movement", movement.Phase);
        Assert.Contains(movement.RevealedOrders, order => order.ShipId == blueShip.Id && order.TurnManeuvers?.Count == 3);

        var firing = service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);
        Assert.Equal("Firing", firing.Phase);
        var blueMoved = firing.Ships.Single(ship => ship.Id == blueShip.Id);
        Assert.Equal(1, blueMoved.CurrentCourse);
        Assert.Equal(9, blueMoved.CurrentVelocity);
        Assert.Contains(firing.MovementResults, result => result.ShipId == blueShip.Id && result.Segments?.Select(segment => segment.Course).SequenceEqual([12, 1, 12, 1]) == true);
        Assert.Contains(firing.MatchLog, entry => entry.Category == "Movement" && entry.Message.Contains("helm S1, P1, S1", StringComparison.OrdinalIgnoreCase));

        var afterFire = service.FireWeapon(owner.MatchId, new FireWeaponRequest(owner.ParticipantToken, blueShip.Id, redShip.Id, blueWeapon, 10));
        var damagedRed = afterFire.Ships.Single(ship => ship.Id == redShip.Id);
        Assert.True(damagedRed.ArmorDamage + damagedRed.HullDamage > 0);
        Assert.Contains(afterFire.FiringResults, result => result.AttackerShipId == blueShip.Id && result.TargetShipId == redShip.Id && result.Range == 10);
        Assert.Contains(afterFire.MatchLog, entry => entry.Category == "Fire" && entry.Message.Contains("range 10", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FireWeapon_DuringFiringPhase_AppliesDamageAndWritesBattleLog()
    {
        // Every die a 6: a Class-3 beam rolls 3 dice, each scoring 2 through level-1 screens.
        var service = new InMemoryMatchService(() => 6);
        var owner = service.CreateMatch(new CreateMatchRequest("Owner", "Combat Test"));
        var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Opponent"));
        var ownerFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", "Test")).Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var opponentFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", "Test")).Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        var weaponId = Guid.NewGuid();

        var attackerSnapshot = service.CreateShip(ownerFleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Attacker",
            "Cruiser",
            4,
            InitialVelocity: 0,
            InitialCourse: 12,
            12,
            0,
            StartX: 20,
            StartY: 30,
            Weapons: [new WeaponMountDto(weaponId, "Class-3 Beam", 3, 24, [FiringArc.Fore])]));
        var attacker = attackerSnapshot.Ships.Single(s => s.Name == "Attacker");
        var target = service.CreateShip(opponentFleet.Id, new CreateShipRequest(
            opponent.ParticipantToken,
            "Target",
            "Frigate",
            4,
            InitialVelocity: 0,
            InitialCourse: 6,
            8,
            1,
            StartX: 20,
            StartY: 18,
            ScreenRating: 1)).Ships.Single(s => s.Name == "Target");

        service.SetReady(owner.MatchId, owner.ParticipantToken, true);
        service.SetReady(owner.MatchId, opponent.ParticipantToken, true);

        service.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, attacker.Id, new MovementOrder(0, 0, TurnDirection.None), "owner-salt"));
        service.CommitOrder(owner.MatchId, new CommitOrderRequest(opponent.ParticipantToken, target.Id, new MovementOrder(0, 0, TurnDirection.None), "opponent-salt"));
        service.RevealOrder(owner.MatchId, new RevealOrderRequest(owner.ParticipantToken, attacker.Id, new MovementOrder(0, 0, TurnDirection.None), "owner-salt"));
        service.RevealOrder(owner.MatchId, new RevealOrderRequest(opponent.ParticipantToken, target.Id, new MovementOrder(0, 0, TurnDirection.None), "opponent-salt"));
        var firingSnapshot = service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

        Assert.Equal("Firing", firingSnapshot.Phase);
        Assert.Contains(firingSnapshot.MatchLog, entry => entry.Category == "Snapshot" && entry.Message.Contains("pos", StringComparison.OrdinalIgnoreCase));

        var result = service.FireWeapon(owner.MatchId, new FireWeaponRequest(
            owner.ParticipantToken,
            attacker.Id,
            target.Id,
            weaponId,
            Range: 6,
            Arc: FiringArc.Fore));

        var damagedTarget = result.Ships.Single(s => s.Id == target.Id);
        Assert.Equal(1, damagedTarget.ArmorDamage);
        Assert.Equal(5, damagedTarget.HullDamage);
        Assert.Contains(result.MatchLog, entry => entry.Message.Contains("range 6", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.MatchLog, entry => entry.Message.Contains("rolled 6,6,6", StringComparison.Ordinal));
        Assert.Contains(result.MatchLog, entry => entry.Message.Contains("vs screens 1", StringComparison.Ordinal));
        Assert.Contains(result.MatchLog, entry => entry.Category == "Snapshot");
        Assert.Contains(result.MatchLog, entry => entry.Category == "Fire" && entry.Timestamp != default);
        // Three 6s through level-1 screens: 2 damage each, armor absorbs one then hull takes five.
        Assert.Contains(result.FiringResults, firing => firing.Damage == 6 && firing.ArmorDamageApplied == 1 && firing.HullDamageApplied == 5);
        Assert.Contains(result.FiringResults, firing => firing.RangeBand == "close" && firing.DiceRolls.Count == 3);
    }

    [Fact]
    public void FireWeapon_RejectsSameMountTwiceInOneTurn()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Owner", "Duplicate Fire Test"));
        var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Opponent"));
        var ownerFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", "Test")).Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var opponentFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", "Test")).Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        var weaponId = Guid.NewGuid();

        var attacker = service.CreateShip(ownerFleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Attacker",
            "Cruiser",
            4,
            InitialVelocity: 0,
            InitialCourse: 12,
            12,
            0,
            StartX: 20,
            StartY: 30,
            Weapons: [new WeaponMountDto(weaponId, "Class-2 Beam", 2, 24, [FiringArc.Fore])])).Ships.Single(s => s.Name == "Attacker");
        var target = service.CreateShip(opponentFleet.Id, new CreateShipRequest(
            opponent.ParticipantToken,
            "Target",
            "Frigate",
            4,
            InitialVelocity: 0,
            InitialCourse: 6,
            8,
            1,
            StartX: 20,
            StartY: 18)).Ships.Single(s => s.Name == "Target");

        service.SetReady(owner.MatchId, owner.ParticipantToken, true);
        service.SetReady(owner.MatchId, opponent.ParticipantToken, true);

        service.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, attacker.Id, new MovementOrder(0, 0, TurnDirection.None), "owner-salt"));
        service.CommitOrder(owner.MatchId, new CommitOrderRequest(opponent.ParticipantToken, target.Id, new MovementOrder(0, 0, TurnDirection.None), "opponent-salt"));
        service.RevealOrder(owner.MatchId, new RevealOrderRequest(owner.ParticipantToken, attacker.Id, new MovementOrder(0, 0, TurnDirection.None), "owner-salt"));
        service.RevealOrder(owner.MatchId, new RevealOrderRequest(opponent.ParticipantToken, target.Id, new MovementOrder(0, 0, TurnDirection.None), "opponent-salt"));
        service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

        service.FireWeapon(owner.MatchId, new FireWeaponRequest(owner.ParticipantToken, attacker.Id, target.Id, weaponId, 8, FiringArc.Fore));

        var error = Assert.Throws<InvalidOperationException>(() =>
            service.FireWeapon(owner.MatchId, new FireWeaponRequest(owner.ParticipantToken, attacker.Id, target.Id, weaponId, 8, FiringArc.Fore)));
        Assert.Contains("already fired", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FireWeapon_WithLimitedAmmo_TracksAmmoAndRejectsEmptyMountAcrossTurns()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Owner", "Ammo Test"));
        var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Opponent"));
        var ownerFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", "Test")).Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var opponentFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", "Test")).Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        var weaponId = Guid.NewGuid();

        var attacker = service.CreateShip(ownerFleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Missile Boat",
            "Destroyer",
            4,
            InitialVelocity: 0,
            InitialCourse: 12,
            10,
            0,
            StartX: 20,
            StartY: 30,
            Weapons: [new WeaponMountDto(weaponId, "Needle Missile", 2, 24, [FiringArc.Fore], AmmoMax: 1)])).Ships.Single(s => s.Name == "Missile Boat");
        var target = service.CreateShip(opponentFleet.Id, new CreateShipRequest(
            opponent.ParticipantToken,
            "Target",
            "Frigate",
            4,
            InitialVelocity: 0,
            InitialCourse: 6,
            8,
            1,
            StartX: 20,
            StartY: 18)).Ships.Single(s => s.Name == "Target");

        service.SetReady(owner.MatchId, owner.ParticipantToken, true);
        service.SetReady(owner.MatchId, opponent.ParticipantToken, true);

        service.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, attacker.Id, new MovementOrder(0, 0, TurnDirection.None), "owner-salt-1"));
        service.CommitOrder(owner.MatchId, new CommitOrderRequest(opponent.ParticipantToken, target.Id, new MovementOrder(0, 0, TurnDirection.None), "opponent-salt-1"));
        service.RevealOrder(owner.MatchId, new RevealOrderRequest(owner.ParticipantToken, attacker.Id, new MovementOrder(0, 0, TurnDirection.None), "owner-salt-1"));
        service.RevealOrder(owner.MatchId, new RevealOrderRequest(opponent.ParticipantToken, target.Id, new MovementOrder(0, 0, TurnDirection.None), "opponent-salt-1"));
        service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

        var afterFire = service.FireWeapon(owner.MatchId, new FireWeaponRequest(owner.ParticipantToken, attacker.Id, target.Id, weaponId, 8, FiringArc.Fore));
        var spentWeapon = afterFire.Ships.Single(ship => ship.Id == attacker.Id).Weapons.Single(weapon => weapon.Id == weaponId);
        Assert.Equal(1, spentWeapon.AmmoUsed);
        Assert.Contains(afterFire.MatchLog, entry => entry.Category == "Fire" && entry.Message.Contains("Ammo 1/1", StringComparison.Ordinal));

        service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);
        service.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, attacker.Id, new MovementOrder(0, 0, TurnDirection.None), "owner-salt-2"));
        service.CommitOrder(owner.MatchId, new CommitOrderRequest(opponent.ParticipantToken, target.Id, new MovementOrder(0, 0, TurnDirection.None), "opponent-salt-2"));
        service.RevealOrder(owner.MatchId, new RevealOrderRequest(owner.ParticipantToken, attacker.Id, new MovementOrder(0, 0, TurnDirection.None), "owner-salt-2"));
        service.RevealOrder(owner.MatchId, new RevealOrderRequest(opponent.ParticipantToken, target.Id, new MovementOrder(0, 0, TurnDirection.None), "opponent-salt-2"));
        service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

        var error = Assert.Throws<InvalidOperationException>(() =>
            service.FireWeapon(owner.MatchId, new FireWeaponRequest(owner.ParticipantToken, attacker.Id, target.Id, weaponId, 8, FiringArc.Fore)));
        Assert.Contains("no ammunition", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
