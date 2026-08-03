using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A needle beam snipes a named system rather than breaking hull. It ignores screens, takes the system
/// outright on a 6, and needs a fire control system of its own that can direct nothing else that turn.
/// </summary>
public sealed class InMemoryMatchServiceNeedleBeamTests
{
    [Fact]
    public void FireWeapon_NeedleTakesTheNamedSystemOnASixWithoutTouchingTheHull()
    {
        var table = NeedleTable.Build(fireControlMax: 2);
        table.Dice.Script(6);

        var result = table.Needle(ShipSystemKind.FireControl);

        var target = result.Ships.Single(ship => ship.Id == table.TargetId);
        Assert.Equal(1, target.FireControlDamage);
        Assert.Equal(0, target.HullDamage);
        Assert.Equal(0, target.ArmorDamage);
        var shot = Assert.Single(result.FiringResults);
        Assert.Equal(WeaponKind.NeedleBeam, shot.WeaponKind);
        Assert.True(shot.IsHit);
        Assert.Equal(0, shot.Damage);
        Assert.Contains(result.MatchLog, entry => entry.Category == "Fire"
            && entry.Message.Contains("at the fire control", StringComparison.Ordinal));
    }

    [Fact]
    public void FireWeapon_NeedleDoesNothingOnAnythingLessThanASix()
    {
        var table = NeedleTable.Build(fireControlMax: 2);
        table.Dice.Script(5);

        var result = table.Needle(ShipSystemKind.FireControl);

        var target = result.Ships.Single(ship => ship.Id == table.TargetId);
        Assert.Equal(0, target.FireControlDamage);
        Assert.Equal(0, target.HullDamage);
        Assert.Contains(result.MatchLog, entry => entry.Message.Contains("nothing hit", StringComparison.Ordinal));
    }

    [Fact]
    public void FireWeapon_NeedleIgnoresScreensEntirely()
    {
        // The target carries level-3 screens, which would cap a beam die at a single point.
        var table = NeedleTable.Build(fireControlMax: 2, targetScreens: 3);
        table.Dice.Script(6);

        var result = table.Needle(ShipSystemKind.Drive);

        Assert.Equal(0, Assert.Single(result.FiringResults).ScreenReduction);
        Assert.True(result.Ships.Single(ship => ship.Id == table.TargetId).DriveDamage > 0);
    }

    [Fact]
    public void FireWeapon_NeedleCanSnipeANamedMount()
    {
        var table = NeedleTable.Build(fireControlMax: 2);
        table.Dice.Script(6);

        var result = table.Needle(ShipSystemKind.Weapon, table.TargetMountId);

        Assert.True(result.Ships.Single(ship => ship.Id == table.TargetId)
            .Weapons.Single(mount => mount.Id == table.TargetMountId).IsDestroyed);
    }

    [Fact]
    public void FireWeapon_NeedleMustNameASystem()
    {
        var table = NeedleTable.Build(fireControlMax: 2);

        var error = Assert.Throws<InvalidOperationException>(() => table.Needle(null));

        Assert.Contains("has to name the system", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_NeedleRefusesASystemTheTargetDoesNotHave()
    {
        // The target carries no bays at all.
        var table = NeedleTable.Build(fireControlMax: 2);

        var error = Assert.Throws<InvalidOperationException>(() => table.Needle(ShipSystemKind.FighterBay));

        Assert.Contains("no working FighterBay", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_ASingleFireconCanDirectANeedleAndThenNothingElse()
    {
        // A needle needs a firecon to itself. With only one aboard, the needle may fire - and that is
        // the ship's whole turn of shooting.
        var table = NeedleTable.Build(fireControlMax: 1);
        table.Dice.Script(6);

        table.Needle(ShipSystemKind.FireControl);

        var beamError = Assert.Throws<InvalidOperationException>(table.Beam);
        Assert.Contains("tied up directing needle fire", beamError.Message, StringComparison.Ordinal);
        var secondError = Assert.Throws<InvalidOperationException>(() => table.SecondNeedle(ShipSystemKind.Drive));
        Assert.Contains("no fire control free", secondError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_AFireconDirectingANeedleCannotAlsoFireABeam()
    {
        // Two firecons: the needle spends one, and the beam then has one left for its own target.
        var table = NeedleTable.Build(fireControlMax: 2);
        table.Dice.Script(6);
        table.Needle(ShipSystemKind.FireControl);

        // The beam can still engage, because one firecon remains.
        table.Dice.Script(4, 4, 4);
        table.Beam();

        // A third call has nothing left to direct it.
        var error = Assert.Throws<InvalidOperationException>(() => table.SecondNeedle(ShipSystemKind.Drive));
        Assert.Contains("no fire control free", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A sniper with a needle, a beam, and a spare needle, against one target dead ahead.</summary>
    private sealed record NeedleTable(
        InMemoryMatchService Service,
        ScriptedDice Dice,
        Guid MatchId,
        string OwnerToken,
        Guid AttackerId,
        Guid TargetId,
        Guid NeedleId,
        Guid SecondNeedleId,
        Guid BeamId,
        Guid TargetMountId)
    {
        public static NeedleTable Build(int fireControlMax, int targetScreens = 0)
        {
            var dice = new ScriptedDice { Fallback = 4 };
            var service = new InMemoryMatchService(dice.Next);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Needle Table"));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            Guid needleId = Guid.NewGuid(), secondNeedleId = Guid.NewGuid(), beamId = Guid.NewGuid();
            var attacker = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Stiletto", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 20, ArmorMax: 0,
                StartX: 20, StartY: 28,
                Weapons:
                [
                    new WeaponMountDto(needleId, "Needle Beam", 1, 9, [FiringArc.Fore], Kind: WeaponKind.NeedleBeam),
                    new WeaponMountDto(secondNeedleId, "Needle Beam Two", 1, 9, [FiringArc.Fore], Kind: WeaponKind.NeedleBeam),
                    new WeaponMountDto(beamId, "Class-2 Beam", 2, 24, [FiringArc.Fore]),
                ],
                FireControlMax: fireControlMax)).Ships.Single(s => s.Name == "Stiletto");

            var targetMountId = Guid.NewGuid();
            var target = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Mark", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: 20, ArmorMax: 4,
                StartX: 20, StartY: 22, ScreenRating: targetScreens,
                Weapons: [new WeaponMountDto(targetMountId, "Mark Battery", 2, 24, [FiringArc.Fore])],
                FireControlMax: 2)).Ships.Single(s => s.Name == "Mark");

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(owner.ParticipantToken));
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(opponent.ParticipantToken));
            service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

            return new NeedleTable(service, dice, owner.MatchId, owner.ParticipantToken,
                attacker.Id, target.Id, needleId, secondNeedleId, beamId, targetMountId);
        }

        public MatchSnapshotDto Needle(ShipSystemKind? system, Guid? mountId = null) =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(
                OwnerToken, AttackerId, TargetId, NeedleId, 6, TargetSystem: system, TargetSystemWeaponId: mountId));

        public MatchSnapshotDto SecondNeedle(ShipSystemKind? system) =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(
                OwnerToken, AttackerId, TargetId, SecondNeedleId, 6, TargetSystem: system));

        public MatchSnapshotDto Beam() =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, AttackerId, TargetId, BeamId, 6));
    }
}
