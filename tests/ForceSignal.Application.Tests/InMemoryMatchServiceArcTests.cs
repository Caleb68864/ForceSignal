using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Firing arcs are geometry, not a declaration: the service works out which arc a target lies in
/// from the two ships' positions and the firing ship's course, then holds the mount to it.
/// </summary>
public sealed class InMemoryMatchServiceArcTests
{
    [Fact]
    public void FireWeapon_RecordsTheArcTheTargetActuallyBearsIn()
    {
        // Attacker on course 12 (up the table) with the target off its starboard bow.
        var table = ArcTable.Build(attackerCourse: 12, targetX: 30, targetY: 20, mountArcs: [.. FiringArcs.Firable]);

        var result = table.Fire();

        var shot = Assert.Single(result.FiringResults);
        Assert.Equal(FiringArc.ForeStarboard, shot.Arc);
        Assert.Contains(result.MatchLog, entry => entry.Category == "Fire" && entry.Message.Contains("through fore starboard arc", StringComparison.Ordinal));
    }

    [Fact]
    public void FireWeapon_RefusesATargetDeadAsternEvenForAnAllRoundMount()
    {
        // Target directly behind an attacker on course 12: squarely in the aft blind spot.
        var table = ArcTable.Build(attackerCourse: 12, targetX: 20, targetY: 40, mountArcs: [.. FiringArcs.Firable]);

        var error = Assert.Throws<InvalidOperationException>(() => table.Fire());

        Assert.Contains("aft arc", error.Message);
    }

    [Fact]
    public void FireWeapon_RefusesATargetOutsideTheMountsArcs()
    {
        // A fore-only battery cannot reach a target off the port quarter.
        var table = ArcTable.Build(attackerCourse: 12, targetX: 8, targetY: 34, mountArcs: [FiringArc.Fore]);

        var error = Assert.Throws<InvalidOperationException>(() => table.Fire());

        Assert.Contains("does not bear", error.Message);
        Assert.Contains("aft port", error.Message);
    }

    [Fact]
    public void FireWeapon_RejectsADeclaredArcThatDisagreesWithTheTable()
    {
        // The client thinks the target is dead ahead; the positions say otherwise. Rather than
        // silently firing, the service names the real bearing so the table can be reconciled.
        var table = ArcTable.Build(attackerCourse: 12, targetX: 30, targetY: 20, mountArcs: [.. FiringArcs.Firable]);

        var error = Assert.Throws<InvalidOperationException>(() => table.Fire(FiringArc.Fore));

        Assert.Contains("bears fore starboard", error.Message);
        Assert.Contains("not fore", error.Message);
    }

    [Fact]
    public void FireWeapon_AcceptsADeclaredArcThatMatchesTheTable()
    {
        var table = ArcTable.Build(attackerCourse: 12, targetX: 30, targetY: 20, mountArcs: [.. FiringArcs.Firable]);

        var result = table.Fire(FiringArc.ForeStarboard);

        Assert.Equal(FiringArc.ForeStarboard, Assert.Single(result.FiringResults).Arc);
    }

    [Fact]
    public void FireWeapon_FollowsTheShipsCourseRatherThanTheTable()
    {
        // Same table positions as the fore-starboard case, but the attacker has come about to
        // course 3, which puts the target off its port bow instead.
        var table = ArcTable.Build(attackerCourse: 3, targetX: 30, targetY: 20, mountArcs: [.. FiringArcs.Firable]);

        Assert.Equal(FiringArc.ForePort, Assert.Single(table.Fire().FiringResults).Arc);
    }

    [Fact]
    public void CreateShip_ExpandsTheLegacyFourArcMountNames()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Legacy Arcs"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();

        var ship = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Old Salt", "Cruiser", 4, 0, 12, 12, 0,
            StartX: 20, StartY: 30,
            Weapons:
            [
                // Written by a build that only knew four 90 degree arcs.
                new WeaponMountDto(Guid.NewGuid(), "Port Battery", 2, 24) { Arc = "Port" },
                new WeaponMountDto(Guid.NewGuid(), "Starboard Battery", 2, 24) { Arc = "Starboard" },
                new WeaponMountDto(Guid.NewGuid(), "Turret", 1, 12) { Arc = "All" },
                new WeaponMountDto(Guid.NewGuid(), "Stern Chaser", 1, 12) { Arc = "Aft" },
            ])).Ships.Single();

        // Each old 90 degree side arc becomes the two 60 degree arcs on that side.
        Assert.Equal(
            [FiringArc.ForePort, FiringArc.AftPort],
            ship.Weapons.Single(w => w.Name == "Port Battery").Arcs);
        Assert.Equal(
            [FiringArc.ForeStarboard, FiringArc.AftStarboard],
            ship.Weapons.Single(w => w.Name == "Starboard Battery").Arcs);
        // An all-round mount bears everywhere a weapon may fire, which excludes the blind spot.
        Assert.Equal(FiringArcs.Firable, ship.Weapons.Single(w => w.Name == "Turret").Arcs);
        // The old aft arc becomes the two quarters either side of the blind spot.
        Assert.Equal(
            [FiringArc.AftPort, FiringArc.AftStarboard],
            ship.Weapons.Single(w => w.Name == "Stern Chaser").Arcs);
    }

    [Fact]
    public void CreateShip_StripsTheAftArcFromAnyMountThatAsksForIt()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Blind Spot"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();

        var ship = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Hopeful", "Cruiser", 4, 0, 12, 12, 0,
            Weapons: [new WeaponMountDto(Guid.NewGuid(), "Wishful Mount", 2, 24, [FiringArc.Aft, FiringArc.AftPort])])).Ships.Single();

        Assert.Equal([FiringArc.AftPort], ship.Weapons.Single().Arcs);
    }

    /// <summary>Two stationary ships in the firing phase, positioned to put the target in a chosen arc.</summary>
    private sealed record ArcTable(InMemoryMatchService Service, Guid MatchId, string OwnerToken, Guid AttackerId, Guid TargetId, Guid WeaponId)
    {
        public static ArcTable Build(int attackerCourse, decimal targetX, decimal targetY, IReadOnlyList<FiringArc> mountArcs)
        {
            var service = new InMemoryMatchService(() => 6);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Arc Table"));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            var weaponId = Guid.NewGuid();
            var attacker = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Gunner", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: attackerCourse, HullMax: 12, ArmorMax: 0,
                StartX: 20, StartY: 30,
                Weapons: [new WeaponMountDto(weaponId, "Class-3 Beam", 3, 36, mountArcs)])).Ships.Single(s => s.Name == "Gunner");
            var target = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Mark", "Frigate", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: 12, ArmorMax: 0,
                StartX: targetX, StartY: targetY)).Ships.Single(s => s.Name == "Mark");

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
            var hold = new MovementOrder(0, 0, TurnDirection.None);
            service.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, attacker.Id, hold, "b"));
            service.CommitOrder(owner.MatchId, new CommitOrderRequest(opponent.ParticipantToken, target.Id, hold, "r"));
            service.RevealOrder(owner.MatchId, new RevealOrderRequest(owner.ParticipantToken, attacker.Id, hold, "b"));
            service.RevealOrder(owner.MatchId, new RevealOrderRequest(opponent.ParticipantToken, target.Id, hold, "r"));
            service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

            return new ArcTable(service, owner.MatchId, owner.ParticipantToken, attacker.Id, target.Id, weaponId);
        }

        public MatchSnapshotDto Fire(FiringArc? declaredArc = null) =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, AttackerId, TargetId, WeaponId, 8, declaredArc));
    }
}
