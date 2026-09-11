using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Covers the firing solution query. Its whole value is agreeing with <c>FireWeapon</c>: a console
/// that offers a shot the server then refuses is worse than no console at all, and that is what the
/// client's own partial copy of these rules had been doing.
/// </summary>
public sealed class InMemoryMatchServiceFiringSolutionTests
{
    [Fact]
    public void FiringSolution_AgreesWithFireWeaponWhenTheShotIsGood()
    {
        var table = FiringTable.Create();

        var solution = table.Solution(table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 10);

        Assert.True(solution.CanFire);
        Assert.Null(solution.Blocker);

        // The shot the query blessed really is allowed.
        var fired = table.Service.FireWeapon(table.MatchId, new FireWeaponRequest(
            table.OwnerToken, table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 10, null));
        Assert.Contains(fired.FiringResults, r => r.WeaponId == table.BlueWeaponId);
    }

    [Fact]
    public void FiringSolution_ReportsTheSameWordsFireWeaponWouldRefuseWith()
    {
        var table = FiringTable.Create();
        table.Service.FireWeapon(table.MatchId, new FireWeaponRequest(
            table.OwnerToken, table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 10, null));

        // The same mount, a second time this turn.
        var solution = table.Solution(table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 10);
        Assert.False(solution.CanFire);
        Assert.NotNull(solution.Blocker);

        var refused = Assert.Throws<InvalidOperationException>(() => table.Service.FireWeapon(
            table.MatchId,
            new FireWeaponRequest(table.OwnerToken, table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 10, null)));

        Assert.Equal(refused.Message, solution.Blocker);
    }

    [Fact]
    public void FiringSolution_KnowsAboutAmmunitionWhichTheClientNeverDid()
    {
        var table = FiringTable.Create();

        var solution = table.Solution(table.BlueLead.Id, table.RedLead.Id, table.DryWeaponId, 10);

        Assert.False(solution.CanFire);
        Assert.Contains("ammunition", solution.Blocker!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FiringSolution_ReportsTheArcTheTargetActuallyBearsIn()
    {
        var table = FiringTable.Create();

        var solution = table.Solution(table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 10);

        Assert.False(string.IsNullOrWhiteSpace(solution.TargetArc));
        Assert.True(solution.MapRange > 0);
    }

    [Fact]
    public void FiringSolution_FlagsADeclaredRangeThatDisagreesWithTheMap()
    {
        var table = FiringTable.Create();

        var honest = table.Solution(table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 4);
        var wild = table.Solution(table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 20);

        Assert.False(honest.RangeDisagreesWithMap);
        Assert.True(wild.RangeDisagreesWithMap);
    }

    [Fact]
    public void FiringSolution_ListsOnlyLiveSystemsForANeedle()
    {
        var table = FiringTable.Create();

        var solution = table.Solution(table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 10);

        Assert.Contains(solution.NeedleTargets, option => option.Kind == "FireControl");
        Assert.Contains(solution.NeedleTargets, option => option.Kind == "Weapon");
        Assert.NotEmpty(solution.NeedleTargets);
        Assert.All(solution.NeedleTargets, option => Assert.False(string.IsNullOrWhiteSpace(option.Label)));
    }

    [Fact]
    public void FiringSolution_AsksForATargetBeforeItAsksForAnythingElse()
    {
        var table = FiringTable.Create();

        var solution = table.Service.GetFiringSolution(table.MatchId, new FiringSolutionRequest(
            table.OwnerToken, table.BlueLead.Id, null, null));

        Assert.False(solution.CanFire);
        Assert.Contains("target", solution.Blocker!, StringComparison.OrdinalIgnoreCase);
        // The attacker's own numbers are still worth having with nothing picked.
        Assert.Equal(1, solution.WorkingFireControl);
    }

    [Fact]
    public void FiringSolution_ChangesNothingAboutTheMatch()
    {
        var table = FiringTable.Create();
        var before = table.Service.GetSnapshot(table.MatchId);

        table.Solution(table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 10);

        var after = table.Service.GetSnapshot(table.MatchId);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.MatchLog.Count, after.MatchLog.Count);
        Assert.Equal(before.FiringShipId, after.FiringShipId);
    }

    [Fact]
    public void FiringSolution_RefusesAShipTheCallerDoesNotOwn()
    {
        var table = FiringTable.Create();

        Assert.Throws<UnauthorizedAccessException>(() => table.Service.GetFiringSolution(table.MatchId, new FiringSolutionRequest(
            table.OpponentToken, table.BlueLead.Id, table.RedLead.Id, table.BlueWeaponId, 10)));
    }

    private sealed record FiringTable(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        ShipDto BlueLead,
        ShipDto RedLead,
        Guid BlueWeaponId,
        Guid DryWeaponId)
    {
        public FiringSolutionDto Solution(Guid attackerId, Guid targetId, Guid weaponId, int range) =>
            Service.GetFiringSolution(MatchId, new FiringSolutionRequest(OwnerToken, attackerId, targetId, weaponId, range));

        public static FiringTable Create()
        {
            var service = new InMemoryMatchService(_ => 4);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue Admiral", "Firing Solution Test", Rules: TestRules.Invented));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red Admiral"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", "Test"))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", "Test"))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            var beamId = Guid.NewGuid();
            var dryId = Guid.NewGuid();
            var blueLead = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken,
                "Blue Lead",
                "Cruiser",
                ThrustRating: 4,
                InitialVelocity: 0,
                InitialCourse: 3,
                HullMax: 12,
                ArmorMax: 0,
                StartX: 18,
                StartY: 24,
                FireControlMax: 1,
                Weapons:
                [
                    new WeaponMountDto(beamId, "Class-3 Beam", 3, 24, [.. FiringArcs.Firable]),
                    // A mount with one shot already spent: the client's rules never modelled ammo.
                    new WeaponMountDto(dryId, "Salvo Rack", 2, 24, [.. FiringArcs.Firable], AmmoMax: 1, AmmoUsed: 1),
                ])).Ships.Single(s => s.Name == "Blue Lead");

            var redLead = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken,
                "Red Lead",
                "Destroyer",
                ThrustRating: 4,
                InitialVelocity: 0,
                InitialCourse: 9,
                HullMax: 10,
                ArmorMax: 0,
                StartX: 22,
                StartY: 24,
                FireControlMax: 1,
                Weapons: [new WeaponMountDto(Guid.NewGuid(), "Class-1 Beam", 1, 12, [.. FiringArcs.Firable])])).Ships.Single(s => s.Name == "Red Lead");

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(owner.ParticipantToken));
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(opponent.ParticipantToken));
            var firing = service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);
            Assert.Equal("Firing", firing.Phase);

            return new FiringTable(service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, blueLead, redLead, beamId, dryId);
        }
    }
}
