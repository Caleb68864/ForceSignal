using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The rules layers replace parts of one another rather than stacking, so a match settles on one and
/// the numbers that differ between them follow from that choice instead of being hard-coded.
/// </summary>
public sealed class InMemoryMatchServiceRulesLayerTests
{
    [Fact]
    public void CreateMatch_DefaultsToTheLightCinematicLayer()
    {
        var service = new InMemoryMatchService(() => 4);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Layers"));

        Assert.Equal("LightCinematic", service.GetSnapshot(owner.MatchId).RulesLayer);
    }

    [Fact]
    public void CreateMatch_CanOpenUnderTheFleetBookLayer()
    {
        var service = new InMemoryMatchService(() => 4);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Layers", RulesLayer: "FleetBook"));

        Assert.Equal("FleetBook", service.GetSnapshot(owner.MatchId).RulesLayer);
    }

    [Fact]
    public void CreateShip_UnderFleetBookCannotCarryLevelThreeScreens()
    {
        // The Fleet Book design system has no level-three screens at all.
        var table = LayerTable.Build("FleetBook");

        var ship = table.AddShip(screens: 3);

        Assert.Equal(2, ship.ScreenRating);
    }

    [Fact]
    public void CreateShip_UnderLightCinematicKeepsLevelThreeScreens()
    {
        var table = LayerTable.Build("LightCinematic");

        Assert.Equal(3, table.AddShip(screens: 3).ScreenRating);
    }

    [Fact]
    public void UpdateRulesLayer_BringsScreensDownToTheNewLayersCeiling()
    {
        var table = LayerTable.Build("LightCinematic");
        table.AddShip(screens: 3);

        var snapshot = table.SwitchTo("FleetBook");

        Assert.Equal("FleetBook", snapshot.RulesLayer);
        Assert.Equal(2, snapshot.Ships.Single(ship => ship.Name == "Vigilant").ScreenRating);
        Assert.Contains(snapshot.MatchLog, entry => entry.Message.Contains("Rules layer set to FleetBook", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateRulesLayer_IsSettledDuringFleetSetup()
    {
        var table = LayerTable.Build("LightCinematic");
        table.AddShip(screens: 1);
        table.StartPlaying();

        var error = Assert.Throws<InvalidOperationException>(() => table.SwitchTo("FleetBook"));

        Assert.Contains("settled during fleet setup", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateRulesLayer_IsTheOwnersCall()
    {
        var table = LayerTable.Build("LightCinematic");

        Assert.Throws<UnauthorizedAccessException>(() =>
            table.Service.UpdateRulesLayer(table.MatchId, new UpdateRulesLayerRequest(table.OpponentToken, "FleetBook")));
    }

    [Fact]
    public void MoveFighterGroup_FliesFurtherUnderTheFleetBookLayer()
    {
        // Twelve units under the light rules, twenty-four under the Fleet Book.
        var light = LayerTable.Build("LightCinematic");
        var group = light.AddFighterGroup();
        light.StartPlaying();
        var tooFar = Assert.Throws<InvalidOperationException>(() => light.Fly(group.Id, 20, 22));
        Assert.Contains("past the 12", tooFar.Message, StringComparison.Ordinal);

        var fleetBook = LayerTable.Build("FleetBook");
        var farGroup = fleetBook.AddFighterGroup();
        fleetBook.StartPlaying();
        var flown = fleetBook.Fly(farGroup.Id, 20, 22);
        Assert.Equal(22m, flown.Ships.Single(ship => ship.Id == farGroup.Id).PositionY);
    }

    /// <summary>A match under a chosen layer, with ships added as each test needs them.</summary>
    private sealed record LayerTable(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid FleetId)
    {
        public static LayerTable Build(string layer)
        {
            var service = new InMemoryMatchService(() => 4);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Layers", RulesLayer: layer));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            // The opponent needs a hull of its own, or readiness leaves the match in fleet setup.
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
            service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Mark", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: 12, ArmorMax: 0,
                StartX: 20, StartY: 20));
            return new LayerTable(service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, fleet.Id);
        }

        public ShipDto AddShip(int screens) =>
            Service.CreateShip(FleetId, new CreateShipRequest(
                OwnerToken, "Vigilant", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 12, ArmorMax: 0,
                StartX: 20, StartY: 40, ScreenRating: screens)).Ships.Single(s => s.Name == "Vigilant");

        public ShipDto AddFighterGroup() =>
            Service.CreateShip(FleetId, new CreateShipRequest(
                OwnerToken, "Hawk Flight", "Fighter Group", 6,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 6, ArmorMax: 0,
                StartX: 20, StartY: 40,
                IconKey: "fighter-group",
                FighterEnduranceMax: 6)).Ships.Single(s => s.Name == "Hawk Flight");

        public MatchSnapshotDto SwitchTo(string layer) =>
            Service.UpdateRulesLayer(MatchId, new UpdateRulesLayerRequest(OwnerToken, layer));

        /// <summary>Leaves fleet setup, which is when the layer stops being negotiable.</summary>
        public void StartPlaying()
        {
            Service.SetReady(MatchId, OwnerToken, true);
            Service.SetReady(MatchId, OpponentToken, true);
        }

        public MatchSnapshotDto Fly(Guid groupId, decimal x, decimal y) =>
            Service.MoveFighterGroup(MatchId, new MoveFighterGroupRequest(OwnerToken, groupId, x, y));
    }
}
