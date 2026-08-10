using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Covers the worked-out fields the snapshot carries on each ship. They exist so no client has to
/// subtract damage off a built rating to find out what a ship still has - a small sum, but one that
/// was being done again in the browser, and getting the needle-killed rule wrong is the kind of
/// mistake a second implementation makes.
/// </summary>
public sealed class InMemoryMatchServiceDerivedShipFieldsTests
{
    [Fact]
    public void Snapshot_ReportsWhatAShipStillGeneratesRatherThanWhatItWasBuiltWith()
    {
        var (service, matchId, token, shipId) = Table();

        service.UpdateShipDamage(shipId, new UpdateShipDamageRequest(
            token, HullDamage: 0, ArmorDamage: 0, FireControlDamage: 1, DriveDamage: 0, WeaponDamage: 0, ScreenDamage: 1));

        var ship = service.GetSnapshot(matchId).Ships.Single(s => s.Id == shipId);

        Assert.Equal(2, ship.ScreenRating);
        Assert.Equal(1, ship.EffectiveScreens);
        Assert.Equal(2, ship.FireControlMax);
        Assert.Equal(1, ship.WorkingFireControl);
    }

    [Fact]
    public void Snapshot_NeverReportsLessThanNothingGenerating()
    {
        var (service, matchId, token, shipId) = Table();

        service.UpdateShipDamage(shipId, new UpdateShipDamageRequest(
            token, HullDamage: 0, ArmorDamage: 0, FireControlDamage: 9, DriveDamage: 0, WeaponDamage: 0, ScreenDamage: 9));

        var ship = service.GetSnapshot(matchId).Ships.Single(s => s.Id == shipId);

        Assert.Equal(0, ship.EffectiveScreens);
        Assert.Equal(0, ship.WorkingFireControl);
    }

    [Fact]
    public void Snapshot_ListsDamagedSystemsAsRepairJobs()
    {
        var (service, matchId, token, shipId) = Table();

        service.UpdateShipDamage(shipId, new UpdateShipDamageRequest(
            token, HullDamage: 4, ArmorDamage: 0, FireControlDamage: 1, DriveDamage: 2, WeaponDamage: 0, ScreenDamage: 1));

        var jobs = service.GetSnapshot(matchId).Ships.Single(s => s.Id == shipId).RepairableSystems!;

        Assert.Contains(jobs, job => job.Kind == "FireControl");
        Assert.Contains(jobs, job => job.Kind == "Drive");
        Assert.Contains(jobs, job => job.Kind == "Screen");
        // Damage control patches systems, not structure.
        Assert.DoesNotContain(jobs, job => job.Kind == "Hull");
    }

    [Fact]
    public void Snapshot_OffersOnlyRepairJobsTheRepairPathWouldAccept()
    {
        var (service, matchId, token, shipId) = Table();
        service.UpdateShipDamage(shipId, new UpdateShipDamageRequest(
            token, HullDamage: 0, ArmorDamage: 0, FireControlDamage: 2, DriveDamage: 1, WeaponDamage: 0, ScreenDamage: 1));
        service.SetReady(matchId, token, true);

        var jobs = service.GetSnapshot(matchId).Ships.Single(s => s.Id == shipId).RepairableSystems!;

        // Every job the snapshot advertises is one the repair path really takes. Assigning them all
        // at once would exceed the party budget, so each is checked on its own.
        foreach (var job in jobs)
        {
            var fresh = Table();
            fresh.Service.UpdateShipDamage(fresh.ShipId, new UpdateShipDamageRequest(
                fresh.Token, HullDamage: 0, ArmorDamage: 0, FireControlDamage: 2, DriveDamage: 1, WeaponDamage: 0, ScreenDamage: 1));
            fresh.Service.SetReady(fresh.MatchId, fresh.Token, true);

            var accepted = Record.Exception(() => fresh.Service.AttemptRepairs(
                fresh.ShipId,
                new AttemptRepairsRequest(fresh.Token, [new RepairJobDto(Enum.Parse<ShipSystemKind>(job.Kind), job.WeaponId, 1)])));

            Assert.Null(accepted);
        }
    }

    [Fact]
    public void Snapshot_HasNoRepairJobsForAnUndamagedShip()
    {
        var (service, matchId, _, shipId) = Table();

        Assert.Empty(service.GetSnapshot(matchId).Ships.Single(s => s.Id == shipId).RepairableSystems!);
    }

    [Fact]
    public void Snapshot_GivesAFighterGroupTheReachItsEnduranceStillBuys()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Admiral", "Derived Fields"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Wing", null)).Fleets.Single();
        var group = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Alpha Flight",
            "Fighter Group",
            ThrustRating: 0,
            InitialVelocity: 6,
            InitialCourse: 12,
            HullMax: 6,
            ArmorMax: 0,
            StartX: 20,
            StartY: 20,
            IconKey: "fighter-group",
            FighterEnduranceMax: 4,
            FighterEnduranceUsed: 2,
            FighterMaxRange: 24)).Ships.Single();

        // Two turns of endurance left at speed 6 is 12, inside the 24 the layer allows.
        Assert.Equal(12, group.FighterReach);
    }

    private static (InMemoryMatchService Service, Guid MatchId, string Token, Guid ShipId) Table()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Admiral", "Derived Fields"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Home Watch", null)).Fleets.Single();
        var ship = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Valiant",
            "Cruiser",
            ThrustRating: 4,
            InitialVelocity: 0,
            InitialCourse: 3,
            HullMax: 12,
            ArmorMax: 4,
            StartX: 20,
            StartY: 24,
            ScreenRating: 2,
            FireControlMax: 2,
            DamageControlParties: 3)).Ships.Single();

        return (service, owner.MatchId, owner.ParticipantToken, ship.Id);
    }
}
