using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The server half of the content policy: <b>the engine owns procedures, the player owns every
/// number those procedures read.</b>
/// </summary>
/// <remarks>
/// <para>
/// The client has its own guard in <c>contentPolicy.test.ts</c>, and it cannot see this side. The
/// defect this pins is the one the invented <c>Class-2 Beam</c> was - a request that entered nothing
/// coming back carrying numbers the server wrote - and the reason it needs a test of its own is that
/// it was invisible: a ship created with an empty weapons list came back armed, and the player had
/// no way to know.
/// </para>
/// <para>
/// A floor is not an invention. <c>HullMax</c> clamps up to one because a ship of no boxes is not a
/// ship, and a one-box hull is visibly unfilled rather than plausibly somebody's cruiser. That is the
/// same bargain a mount nobody filled in already strikes. What this refuses is the other thing: a
/// number that looks like a reading off a card.
/// </para>
/// </remarks>
public sealed class ContentPolicyTests
{
    [Fact]
    public void AShipThatEnteredNothingComesBackWithNothingEntered()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Admiral", "Table", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Home Watch", null)).Fleets.Single();

        // Exactly what the new-ship form now sends: text, a heading, a place on the table, and every
        // number the engine reads left unentered.
        var ships = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Valiant",
            "Cruiser",
            ThrustRating: 0,
            InitialVelocity: 0,
            InitialCourse: 1,
            HullMax: 0,
            ArmorMax: 0,
            StartX: 12,
            StartY: 24,
            ScreenRating: 0,
            IconKey: "cruiser",
            FireControlMax: 0,
            PointDefenseSystems: 0,
            DamageControlParties: 0)).Ships;

        var ship = ships.Single();

        // Reached-the-subject: the request really was accepted rather than refused, and it is the
        // ship that was asked for.
        Assert.Equal("Valiant", ship.Name);

        // The structural floor, and nothing above it.
        Assert.Equal(1, ship.HullMax);
        Assert.Equal(0, ship.ArmorMax);
        Assert.Equal(0, ship.ThrustRating);
        Assert.Equal(0, ship.ScreenRating);
        Assert.Equal(0, ship.FireControlMax);
        Assert.Equal(0, ship.PointDefenseSystems);
        Assert.Equal(0, ship.DamageControlParties);
        Assert.Equal(0, ship.FighterBays);
        Assert.Equal(0, ship.PointsValue);
    }

    [Fact]
    public void AShipThatNamedNoMountsHasNone()
    {
        // This is where the Class-2 Beam lived: an empty weapons list came back holding a class
        // name, two attack dice and a reach of twenty-four, none of which anybody entered.
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Admiral", "Table", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Home Watch", null)).Fleets.Single();

        var ship = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Valiant",
            "Cruiser",
            ThrustRating: 0,
            InitialVelocity: 0,
            InitialCourse: 1,
            HullMax: 0,
            ArmorMax: 0,
            Weapons: [])).Ships.Single();

        Assert.Equal("Valiant", ship.Name);
        Assert.Empty(ship.Weapons);
    }
}
