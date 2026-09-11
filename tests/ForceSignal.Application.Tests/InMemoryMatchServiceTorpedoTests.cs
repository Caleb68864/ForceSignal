using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A pulse torpedo launcher resolves in two rolls - to hit by range band, then a damage die - and
/// screens do not touch it. The service has to pick those rules rather than the beam table.
/// </summary>
public sealed class InMemoryMatchServiceTorpedoTests
{
    [Fact]
    public void FireWeapon_ResolvesATorpedoAsAToHitRollThenADamageDie()
    {
        // This profile asks a 4 at range 10. Rolls a 4 to hit and a 5 for damage, against the
        // heaviest screens it allows - which would have flattened a beam die to a single point.
        var table = TorpedoTable.Build(targetScreens: 2);
        table.Dice.Script(4, 5);

        var result = table.Fire(range: 10);

        var shot = Assert.Single(result.FiringResults);
        Assert.Equal(WeaponKind.PulseTorpedo, shot.WeaponKind);
        Assert.Equal(4, shot.ToHitNumber);
        Assert.True(shot.IsHit);
        Assert.Equal([4, 5], shot.DiceRolls);
        Assert.Equal(5, shot.Damage);
        Assert.Equal(0, shot.ScreenReduction);
        Assert.Equal(5, result.Ships.Single(s => s.Id == table.TargetId).HullDamage);
        Assert.Contains(result.MatchLog, entry => entry.Category == "Fire"
            && entry.Message.Contains("needed 4+, rolled 4, damage die 5", StringComparison.Ordinal)
            && entry.Message.Contains("ignoring screens", StringComparison.Ordinal));
    }

    [Fact]
    public void FireWeapon_LogsATorpedoMissWithoutDamage()
    {
        var table = TorpedoTable.Build(targetScreens: 0);
        table.Dice.Script(2);

        var result = table.Fire(range: 20);

        var shot = Assert.Single(result.FiringResults);
        Assert.False(shot.IsHit);
        Assert.Equal(5, shot.ToHitNumber);
        Assert.Equal(0, shot.Damage);
        Assert.Equal(0, result.Ships.Single(s => s.Id == table.TargetId).HullDamage);
        Assert.Contains(result.MatchLog, entry => entry.Message.Contains("rolled 2 and missed", StringComparison.Ordinal));
    }

    [Fact]
    public void FireWeapon_RefusesATorpedoBeyondThirtyUnits()
    {
        var table = TorpedoTable.Build(targetScreens: 0);

        var error = Assert.Throws<InvalidOperationException>(() => table.Fire(range: 31));

        Assert.Contains("out of range", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateShip_KeepsAMountsKindThroughTheSnapshot()
    {
        var table = TorpedoTable.Build(targetScreens: 0);

        var mounts = table.Service.GetSnapshot(table.MatchId).Ships
            .Single(s => s.Id == table.AttackerId).Weapons;

        Assert.Equal(WeaponKind.PulseTorpedo, mounts.Single(m => m.Name == "Torpedo Tube").Kind);
        Assert.Equal(WeaponKind.Beam, mounts.Single(m => m.Name == "Class-2 Beam").Kind);
    }

    /// <summary>A ship with one torpedo tube and one beam, lined up on a target dead ahead.</summary>
    private sealed record TorpedoTable(
        InMemoryMatchService Service,
        ScriptedDice Dice,
        Guid MatchId,
        string OwnerToken,
        Guid AttackerId,
        Guid TargetId,
        Guid TorpedoId)
    {
        public static TorpedoTable Build(int targetScreens)
        {
            var dice = new ScriptedDice { Fallback = 4 };
            var service = new InMemoryMatchService(dice.Next);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Torpedo Table", Rules: TestRules.Invented));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            var torpedoId = Guid.NewGuid();
            var attacker = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Lancer", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 20, ArmorMax: 0,
                StartX: 20, StartY: 40, FireControlMax: 1,
                Weapons:
                [
                    new WeaponMountDto(torpedoId, "Torpedo Tube", 1, 30, [FiringArc.Fore], Kind: WeaponKind.PulseTorpedo),
                    new WeaponMountDto(Guid.NewGuid(), "Class-2 Beam", 2, 24, [FiringArc.Fore]),
                ])).Ships.Single(s => s.Name == "Lancer");
            var target = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Mark", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: 40, ArmorMax: 0,
                StartX: 20, StartY: 20, ScreenRating: targetScreens)).Ships.Single(s => s.Name == "Mark");

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(owner.ParticipantToken));
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(opponent.ParticipantToken));
            service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

            return new TorpedoTable(service, dice, owner.MatchId, owner.ParticipantToken, attacker.Id, target.Id, torpedoId);
        }

        public MatchSnapshotDto Fire(int range) =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, AttackerId, TargetId, TorpedoId, range));
    }
}
