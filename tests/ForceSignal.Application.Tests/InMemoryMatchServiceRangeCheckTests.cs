using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The physical table is the authority on distance, so a declared range is never overruled - but the
/// service knows where both ships sit, so it says when the two disagree enough to matter.
/// </summary>
public sealed class InMemoryMatchServiceRangeCheckTests
{
    [Fact]
    public void FireWeapon_WithADeclaredRangeMatchingTheMap_SaysNothing()
    {
        // The ships are 20 apart on the map and the player declares 20.
        var table = RangeTable.Build();

        var result = table.Fire(range: 20);

        var shot = Assert.Single(result.FiringResults);
        Assert.Equal(20m, shot.MapRange);
        Assert.False(shot.RangeDisagreed);
        Assert.DoesNotContain(result.MatchLog, entry => entry.Category == "Range");
    }

    [Fact]
    public void FireWeapon_WithADeclaredRangeInADifferentBandWarnsWithoutRefusing()
    {
        // 20 on the map is a beam's second band; declaring 10 would roll a die more.
        var table = RangeTable.Build();

        var result = table.Fire(range: 10);

        var shot = Assert.Single(result.FiringResults);
        Assert.True(shot.RangeDisagreed);
        // The shot still stands: the table decides the distance.
        Assert.Equal(10, shot.Range);
        Assert.Equal(20m, shot.MapRange);
        var warning = Assert.Single(result.MatchLog, entry => entry.Category == "Range");
        Assert.Contains("declared 10", warning.Message, StringComparison.Ordinal);
        Assert.Contains("map measures 20", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_WithinTheSameBandAndCloseEnoughSaysNothing()
    {
        // 18 and a map of 20 are both in the same beam band and only two apart.
        var table = RangeTable.Build();

        var result = table.Fire(range: 18);

        Assert.False(Assert.Single(result.FiringResults).RangeDisagreed);
        Assert.DoesNotContain(result.MatchLog, entry => entry.Category == "Range");
    }

    [Fact]
    public void FireWeapon_WithinTheSameBandButFarApartStillWarns()
    {
        // 13 and 20 share a beam band, so the dice are the same either way - but seven units apart
        // usually means a mistyped range or a ship nobody moved, which is worth a line in the log.
        var table = RangeTable.Build();

        var result = table.Fire(range: 13);

        Assert.True(Assert.Single(result.FiringResults).RangeDisagreed);
    }

    [Fact]
    public void FireWeapon_JudgesATorpedoAgainstItsOwnNarrowerBands()
    {
        // A torpedo's to-hit number worsens every 6mu, so 20 against a map of 20 is fine while 14
        // crosses into a better band and is flagged.
        var table = RangeTable.Build();

        Assert.False(Assert.Single(table.Fire(range: 20, torpedo: true).FiringResults).RangeDisagreed);
        Assert.True(table.Fire(range: 14, torpedo: true, secondTube: true).FiringResults
            .Single(shot => shot.Range == 14).RangeDisagreed);
    }

    /// <summary>A ship 20 units dead ahead of another, with two tubes and a beam.</summary>
    private sealed record RangeTable(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        Guid AttackerId,
        Guid TargetId,
        Guid BeamId,
        Guid TorpedoId,
        Guid SecondTorpedoId)
    {
        public static RangeTable Build()
        {
            var service = new InMemoryMatchService(() => 4);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Range Table", Rules: TestRules.Invented));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            Guid beamId = Guid.NewGuid(), torpedoId = Guid.NewGuid(), secondTorpedoId = Guid.NewGuid();
            var attacker = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Gunner", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 20, ArmorMax: 0,
                StartX: 20, StartY: 40, FireControlMax: 1,
                Weapons:
                [
                    new WeaponMountDto(beamId, "Class-3 Beam", 3, 36, [FiringArc.Fore]),
                    new WeaponMountDto(torpedoId, "Tube One", 1, 30, [FiringArc.Fore], Kind: WeaponKind.PulseTorpedo),
                    new WeaponMountDto(secondTorpedoId, "Tube Two", 1, 30, [FiringArc.Fore], Kind: WeaponKind.PulseTorpedo),
                ])).Ships.Single(s => s.Name == "Gunner");
            var target = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Mark", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: 40, ArmorMax: 0,
                StartX: 20, StartY: 20)).Ships.Single(s => s.Name == "Mark");

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(owner.ParticipantToken));
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(opponent.ParticipantToken));
            service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

            return new RangeTable(service, owner.MatchId, owner.ParticipantToken, attacker.Id, target.Id, beamId, torpedoId, secondTorpedoId);
        }

        public MatchSnapshotDto Fire(int range, bool torpedo = false, bool secondTube = false)
        {
            var weaponId = torpedo ? secondTube ? SecondTorpedoId : TorpedoId : BeamId;
            return Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, AttackerId, TargetId, weaponId, range));
        }
    }
}
