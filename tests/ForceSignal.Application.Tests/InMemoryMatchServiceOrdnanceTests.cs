using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Salvo missiles, fighter strikes, and the close-in fire that answers both. A salvo is thrown at a
/// point of aim and strikes at the end of movement; a fighter group rolls one die per surviving
/// fighter and loses aircraft to point defence on the way in.
/// </summary>
public sealed class InMemoryMatchServiceOrdnanceTests
{
    [Fact]
    public void AdvanceTurn_ResolvesASalvoAgainstTheNearestEnemyToThePointOfAim()
    {
        var table = OrdnanceTable.Build(targetPointDefense: 0);
        table.LaunchSalvo(aimX: 20, aimY: 20);
        // Arrival die 4, then damage dice of 6, 3, 2 and 5.
        table.Dice.Script(4, 6, 3, 2, 5);

        var firing = table.RunToFiring();

        var target = firing.Ships.Single(ship => ship.Id == table.TargetId);
        // Sixteen points: armour takes half rounded up, and the rest goes straight to the hull.
        Assert.Equal(8, target.ArmorDamage);
        Assert.Equal(8, target.HullDamage);
        var strike = firing.MatchLog.Last(entry => entry.Category == "Ordnance");
        Assert.Contains("4 of 4 missiles arrived", strike.Message, StringComparison.Ordinal);
        Assert.Contains("ignoring screens", strike.Message, StringComparison.Ordinal);
        Assert.Equal("Resolved", firing.OrdnanceMarkers.Single().Status);
    }

    [Fact]
    public void AdvanceTurn_LetsPointDefenceThinASalvoBeforeItStrikes()
    {
        var table = OrdnanceTable.Build(targetPointDefense: 2);
        table.LaunchSalvo(aimX: 20, aimY: 20);
        // Arrival high enough to fill the salvo, then two point defence dice that stop one between
        // them, and the three survivors roll the lowest face there is.
        table.Dice.Script(8, 7, 1, 1, 1, 1);

        var firing = table.RunToFiring();

        var strike = firing.MatchLog.Last(entry => entry.Category == "Ordnance");
        Assert.Contains("stopped 1", strike.Message, StringComparison.Ordinal);
        Assert.Contains("3 got through", strike.Message, StringComparison.Ordinal);
        var target = firing.Ships.Single(ship => ship.Id == table.TargetId);
        Assert.Equal(3, target.ArmorDamage + target.HullDamage);
    }

    [Fact]
    public void AdvanceTurn_WastesASalvoWithNothingInReachOfThePointOfAim()
    {
        var table = OrdnanceTable.Build(targetPointDefense: 0);
        // Aimed at empty space, well clear of either fleet but inside the launcher's reach.
        table.LaunchSalvo(aimX: 38, aimY: 44);

        var firing = table.RunToFiring();

        Assert.Equal("Expired", firing.OrdnanceMarkers.Single().Status);
        Assert.Contains(firing.MatchLog, entry => entry.Category == "Ordnance"
            && entry.Message.Contains($"found nothing within {TestRules.Invented.SalvoAttackRadius}", StringComparison.Ordinal));
        var target = firing.Ships.Single(ship => ship.Id == table.TargetId);
        Assert.Equal(0, target.HullDamage + target.ArmorDamage);
    }

    [Fact]
    public void AdvanceTurn_SalvoDamageThatFillsAHullRowRollsAThresholdCheck()
    {
        // Missile damage lands like any other, so it can take systems with it. The whole salvo
        // arrives and every missile rolls the top face: 32 points, half of it onto eight armour
        // boxes - which is all the armour there is - and the remaining 24 into the hull.
        var table = OrdnanceTable.Build(targetPointDefense: 0);
        table.LaunchSalvo(aimX: 20, aimY: 20);
        table.Dice.Script(8, 8, 8, 8, 8);

        var firing = table.RunToFiring();

        var target = firing.Ships.Single(ship => ship.Id == table.TargetId);
        Assert.Equal(8, target.ArmorDamage);
        Assert.Equal(24, target.HullDamage);
        // A 40-box hull in three rows runs 14/13/13, so 24 points completes the first.
        Assert.Equal(1, target.HullRowsCompleted);
        Assert.Contains(firing.MatchLog, entry => entry.Category == "Threshold");
    }

    [Fact]
    public void CreateOrdnanceMarker_RefusesAPointOfAimPastTheLaunchersReach()
    {
        var table = OrdnanceTable.Build(targetPointDefense: 0);

        // The launcher sits at 20,40 and a standard salvo reaches 24.
        var error = Assert.Throws<InvalidOperationException>(() => table.LaunchSalvo(aimX: 20, aimY: 5));

        Assert.Contains("past the 24 this salvo can reach", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_RollsOneDiePerSurvivingFighterInAGroup()
    {
        var table = OrdnanceTable.Build(targetPointDefense: 0);
        table.RunToFiring();
        // Six fighters, so six dice. The target's screens stop everything but the top face under
        // this profile, so that is what they roll.
        table.Dice.Script(8, 8, 8, 8, 8, 8);

        var result = table.FighterStrike();

        var shot = Assert.Single(result.FiringResults);
        Assert.Equal(6, shot.DiceRolls.Count);
        Assert.Equal(6, shot.Damage);
    }

    [Fact]
    public void FireWeapon_ThinsAFighterGroupByItsLossesOnTheNextStrike()
    {
        var table = OrdnanceTable.Build(targetPointDefense: 0);
        table.RunToFiring();
        table.KillFighters(4);
        table.Dice.Script(5, 5);

        var result = table.FighterStrike();

        // Two fighters left, so two dice.
        Assert.Equal(2, Assert.Single(result.FiringResults).DiceRolls.Count);
    }

    [Fact]
    public void FireWeapon_LetsPointDefenceShootDownFightersBeforeTheyAttack()
    {
        var table = OrdnanceTable.Build(targetPointDefense: 2);
        table.RunToFiring();
        // The first system rolls the chaining face for two kills and chains into one more; the
        // second takes one. Four down, and the range is inside what this profile's turrets reach.
        table.Dice.Script(8, 7, 7, 8, 8);

        var result = table.FighterStrike(range: 4);

        var defense = Assert.Single(result.MatchLog, entry => entry.Category == "PointDefense");
        Assert.Contains("shot down 4 of 6 fighter(s)", defense.Message, StringComparison.Ordinal);
        var group = result.Ships.Single(ship => ship.Id == table.FighterGroupId);
        Assert.Equal(4, group.HullDamage);
        // Two fighters left to roll.
        Assert.Equal(2, Assert.Single(result.FiringResults).DiceRolls.Count);
    }

    [Fact]
    public void FireWeapon_StopsAFighterStrikeThatPointDefenceWipesOut()
    {
        var table = OrdnanceTable.Build(targetPointDefense: 3);
        table.RunToFiring();
        // The chaining face runs into far more kills than the group has fighters.
        table.Dice.Script(8, 8, 8, 8, 1, 1, 1);

        var result = table.FighterStrike(range: 4);

        Assert.Empty(result.FiringResults);
        Assert.True(result.Ships.Single(ship => ship.Id == table.FighterGroupId).IsDestroyed);
        Assert.Contains(result.MatchLog, entry => entry.Message.Contains("wiped out by point defence", StringComparison.Ordinal));
    }

    [Fact]
    public void FireWeapon_SpendsAFighterGroupsEnduranceOncePerCombatTurn()
    {
        var table = OrdnanceTable.Build(targetPointDefense: 0);
        table.RunToFiring();

        var afterStrike = table.FighterStrike();

        var group = afterStrike.Ships.Single(ship => ship.Id == table.FighterGroupId);
        Assert.Equal(1, group.FighterEnduranceUsed);
        Assert.Contains(afterStrike.MatchLog, entry => entry.Category == "Fighters"
            && entry.Message.Contains("spent a turn of endurance", StringComparison.Ordinal));

        // A second mount firing in the same turn does not spend another turn of endurance.
        var afterSecond = table.FighterStrike(secondMount: true);
        Assert.Equal(1, afterSecond.Ships.Single(ship => ship.Id == table.FighterGroupId).FighterEnduranceUsed);
    }

    [Fact]
    public void FireWeapon_RefusesAnExhaustedFighterGroup()
    {
        var table = OrdnanceTable.Build(targetPointDefense: 0, fighterEnduranceUsed: 6);
        table.RunToFiring();

        var error = Assert.Throws<InvalidOperationException>(() => table.FighterStrike());

        Assert.Contains("out of combat endurance", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_RefusesAMainBatteryFiringAtAFighterGroup()
    {
        // Batteries cannot engage fighters: point defence is the answer, and it fires when the group
        // attacks rather than being aimed at it.
        var table = OrdnanceTable.Build(targetPointDefense: 0);
        table.RunToFiring();

        var error = Assert.Throws<InvalidOperationException>(table.BeamTheFighters);

        Assert.Contains("cannot engage fighters", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_LetsAFighterGroupShootAtAnotherFighterGroup()
    {
        // Fighters may fire on each other inside 6mu through their fore arc, scoring kills on the same
        // numbers as anti-fighter fire. A dogfight at base contact is a separate rule and not built.
        var table = OrdnanceTable.Build(targetPointDefense: 0, opposingFlight: true);
        table.RunToFiring();
        table.Dice.Script(5, 5, 5, 6, 4, 4);

        var result = table.StrikeOpposingFlight();

        var enemyFlight = result.Ships.Single(ship => ship.Id == table.OpposingFlightId);
        Assert.True(enemyFlight.HullDamage > 0);
    }

    /// <summary>A missile cruiser and a fighter group against one target, all inside 6mu of it.</summary>
    private sealed record OrdnanceTable(
        InMemoryMatchService Service,
        ScriptedDice Dice,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid LauncherId,
        Guid FighterGroupId,
        Guid TargetId,
        Guid FighterMount,
        Guid SecondFighterMount,
        Guid BeamMount,
        Guid OpposingFlightId)
    {
        public static OrdnanceTable Build(int targetPointDefense, int fighterEnduranceUsed = 0, bool opposingFlight = false)
        {
            var dice = new ScriptedDice { Fallback = 4 };
            var service = new InMemoryMatchService(dice.Next);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Ordnance Table", Rules: TestRules.Invented));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            var beamMount = Guid.NewGuid();
            var launcher = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Archer", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 20, ArmorMax: 0,
                StartX: 20, StartY: 40, FireControlMax: 1,
                Weapons: [new WeaponMountDto(beamMount, "Class-3 Beam", 3, 36, [FiringArc.Fore])]))
                .Ships.Single(s => s.Name == "Archer");

            Guid fighterMount = Guid.NewGuid(), secondMount = Guid.NewGuid();
            var group = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Hawk Flight", "Fighter Group", 6,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 6, ArmorMax: 0,
                StartX: 20, StartY: 24,
                Weapons:
                [
                    new WeaponMountDto(fighterMount, "Fighter Attack", 6, 6, [FiringArc.Fore]),
                    new WeaponMountDto(secondMount, "Second Pass", 6, 6, [FiringArc.Fore]),
                ],
                IconKey: "fighter-group",
                FighterEnduranceMax: 6,
                FighterEnduranceUsed: fighterEnduranceUsed,
                FireControlMax: 1)).Ships.Single(s => s.Name == "Hawk Flight");

            var target = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Bulwark", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: 40, ArmorMax: 8,
                StartX: 20, StartY: 20, ScreenRating: 2,
                FireControlMax: 1, PointDefenseSystems: targetPointDefense)).Ships.Single(s => s.Name == "Bulwark");

            // An enemy flight just off the friendly group's bow, for fighter-versus-fighter fire.
            var opposing = opposingFlight
                ? service.CreateShip(redFleet.Id, new CreateShipRequest(
                    opponent.ParticipantToken, "Red Talons", "Fighter Group", 6,
                    InitialVelocity: 0, InitialCourse: 6, HullMax: 6, ArmorMax: 0,
                    StartX: 20, StartY: 21,
                    Weapons: [new WeaponMountDto(Guid.NewGuid(), "Fighter Attack", 6, 6, [FiringArc.Fore])],
                    IconKey: "fighter-group",
                    FighterEnduranceMax: 6,
                    FireControlMax: 1)).Ships.Single(s => s.Name == "Red Talons")
                : null;

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);

            return new OrdnanceTable(
                service, dice, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken,
                launcher.Id, group.Id, target.Id, fighterMount, secondMount, beamMount, opposing?.Id ?? Guid.Empty);
        }

        public void LaunchSalvo(decimal aimX, decimal aimY) =>
            Service.CreateOrdnanceMarker(MatchId, new CreateOrdnanceMarkerRequest(
                OwnerToken, "Salvo One", "Salvo", LauncherId, null, aimX, aimY,
                Course: 12, Speed: 0, EnduranceRemaining: 1, AttackDice: 0, MaxRange: 24));

        /// <summary>Closes plotting for both sides and advances into the firing phase.</summary>
        public MatchSnapshotDto RunToFiring()
        {
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OwnerToken));
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OpponentToken));
            return Service.AdvanceTurn(MatchId, OwnerToken);
        }

        public MatchSnapshotDto FighterStrike(int range = 4, bool secondMount = false) =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(
                OwnerToken, FighterGroupId, TargetId, secondMount ? SecondFighterMount : FighterMount, range));

        /// <summary>Tries to bring the cruiser's main battery to bear on the friendly flight.</summary>
        public MatchSnapshotDto BeamTheFighters() =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, LauncherId, FighterGroupId, BeamMount, 16));

        public MatchSnapshotDto StrikeOpposingFlight() =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, FighterGroupId, OpposingFlightId, FighterMount, 3));

        /// <summary>Shoots down fighters by hand, standing in for earlier losses.</summary>
        public void KillFighters(int count)
        {
            var group = Service.GetSnapshot(MatchId).Ships.Single(s => s.Id == FighterGroupId);
            Service.UpdateShipDamage(FighterGroupId, new UpdateShipDamageRequest(
                OwnerToken, group.HullDamage + count, group.ArmorDamage, group.FireControlDamage, group.DriveDamage, group.WeaponDamage));
        }
    }
}
