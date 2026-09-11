using ForceSignal.Application.Ground;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Ground;
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

    [Fact]
    public void AFighterGroupThatEnteredNoNumbersComesBackWithNone()
    {
        // Exactly what the client's "Fighters" preset sends: a class and an icon, and every number
        // at the form's zero. This came back with six turns of endurance and a reach of twenty-four,
        // and nothing on screen said where either had come from. The endurance was then spent by
        // SpendFighterEndurance and the reach was drawn on the map as a range ring, so both were
        // live rules numbers rather than decoration.
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Admiral", "Table", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Home Watch", null)).Fleets.Single();

        var ship = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Gold Flight",
            "Fighter Group",
            ThrustRating: 0,
            InitialVelocity: 0,
            InitialCourse: 1,
            HullMax: 0,
            ArmorMax: 0,
            IconKey: "fighter-group",
            FighterEnduranceMax: 0,
            FighterMaxRange: 0)).Ships.Single();

        // Reached-the-subject: it really is a fighter group, so the fighter fields are live for it.
        Assert.Equal("fighter-group", ship.IconKey);

        Assert.Equal(0, ship.FighterEnduranceMax);
        Assert.Equal(0, ship.FighterMaxRange);
    }

    [Fact]
    public void AFighterGroupKeepsTheNumbersItDidEnter()
    {
        // The control. A normalizer that zeroed everything would satisfy the test above and be
        // useless, so the same two fields have to survive being filled in.
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Admiral", "Table", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Home Watch", null)).Fleets.Single();

        var ship = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Gold Flight",
            "Fighter Group",
            ThrustRating: 0,
            InitialVelocity: 0,
            InitialCourse: 1,
            HullMax: 0,
            ArmorMax: 0,
            IconKey: "fighter-group",
            FighterEnduranceMax: 9,
            FighterMaxRange: 33)).Ships.Single();

        Assert.Equal(9, ship.FighterEnduranceMax);
        Assert.Equal(33, ship.FighterMaxRange);
    }

    [Fact]
    public void AGroundWeaponThatNamedNoSupportDieComesBackWithNone()
    {
        // The same habit in the other engine, and the sharpest remaining instance of it: a *die
        // rating*, which is what a record card is mostly made of. Every weapon anybody entered came
        // back carrying a D6 support firepower die, because the DTO and the profile both defaulted
        // to one. Most weapons never join a squad volley at all, so this was a number written onto
        // a weapon for a rule it will never be read by - until the day it is.
        var service = new StarGruntGameService();
        var game = service.CreateGame(new CreateStarGruntGameRequest("Hill 43"));

        var snapshot = service.AddUnit(game.GameId, new AddStarGruntUnitRequest(
            Id: "alpha",
            Name: "Alpha Squad",
            Side: "Blue",
            Level: "Squad",
            QualityDie: 8,
            LeadershipValue: 2,
            Fatigue: "Fresh",
            Figures: [new StarGruntFigureDto(6)],
            Weapons: [new StarGruntWeaponDto("Rifles", 10)]));

        // Reached-the-subject: the unit was accepted and it is the weapon that was asked for, so
        // there is a die rating to read.
        var weapon = snapshot.Units.Single().Weapons.Single();
        Assert.Equal("Rifles", weapon.Name);

        // The numbers the card did give, unchanged.
        Assert.Equal(10, weapon.ImpactDie);

        // The one it did not.
        Assert.Equal(0, weapon.SupportFirepowerDie);
    }

    [Fact]
    public void AGroundWeaponKeepsTheSupportDieItDidEnter()
    {
        // The control that must be accepted. A weapon whose card does give a firepower die still
        // carries it, and it is still a different number from the impact die - the two are
        // deliberately unrelated, and a fix that collapsed them would satisfy the test above.
        var service = new StarGruntGameService();
        var game = service.CreateGame(new CreateStarGruntGameRequest("Hill 43"));

        var snapshot = service.AddUnit(game.GameId, new AddStarGruntUnitRequest(
            Id: "alpha",
            Name: "Alpha Squad",
            Side: "Blue",
            Level: "Squad",
            QualityDie: 8,
            LeadershipValue: 2,
            Fatigue: "Fresh",
            Figures: [new StarGruntFigureDto(6)],
            Weapons: [new StarGruntWeaponDto("Squad Support", 12, IsSupport: true, SupportFirepowerDie: 4)]));

        var weapon = snapshot.Units.Single().Weapons.Single();
        Assert.Equal("Squad Support", weapon.Name);
        Assert.True(weapon.IsSupport);
        Assert.Equal(12, weapon.ImpactDie);
        Assert.Equal(4, weapon.SupportFirepowerDie);
    }

    [Fact]
    public void ASalvoIsNotRefusedAgainstAReachNobodyEntered()
    {
        // The sharpest instance of this habit anywhere in the codebase, because it is not a stored
        // default a player might notice and overwrite - it is a refusal. A player who entered no
        // reach was told their point of aim was "past the 24 this salvo can reach", quoting a number
        // the app had written for them.
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Admiral", "Table", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Home Watch", null)).Fleets.Single();
        var source = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Valiant",
            "Cruiser",
            ThrustRating: 0,
            InitialVelocity: 0,
            InitialCourse: 1,
            HullMax: 0,
            ArmorMax: 0,
            StartX: 0,
            StartY: 0)).Ships.Single();

        // Well past any reach the app used to invent, and past nothing the player said.
        var snapshot = service.CreateOrdnanceMarker(owner.MatchId, new CreateOrdnanceMarkerRequest(
            owner.ParticipantToken,
            "Valiant Salvo",
            "Salvo",
            SourceShipId: source.Id,
            TargetShipId: null,
            PositionX: 60,
            PositionY: 0,
            Course: 1,
            Speed: 0,
            EnduranceRemaining: 0,
            AttackDice: 0,
            MaxRange: 0));

        var marker = Assert.Single(snapshot.OrdnanceMarkers);
        Assert.Equal(60, marker.PositionX);
        Assert.Equal(0, marker.MaxRange);
    }

    [Fact]
    public void ASalvoIsStillRefusedAgainstTheReachThePlayerDidEnter()
    {
        // The control for the one above: the check is still a check, on the table's own number.
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Admiral", "Table", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Home Watch", null)).Fleets.Single();
        var source = service.CreateShip(fleet.Id, new CreateShipRequest(
            owner.ParticipantToken,
            "Valiant",
            "Cruiser",
            ThrustRating: 0,
            InitialVelocity: 0,
            InitialCourse: 1,
            HullMax: 0,
            ArmorMax: 0,
            StartX: 0,
            StartY: 0)).Ships.Single();

        var refusal = Assert.Throws<InvalidOperationException>(() => service.CreateOrdnanceMarker(
            owner.MatchId,
            new CreateOrdnanceMarkerRequest(
                owner.ParticipantToken,
                "Valiant Salvo",
                "Salvo",
                SourceShipId: source.Id,
                TargetShipId: null,
                PositionX: 60,
                PositionY: 0,
                Course: 1,
                Speed: 0,
                EnduranceRemaining: 0,
                AttackDice: 0,
                MaxRange: 10)));

        // The refusal quotes the reach the player entered and no other number.
        Assert.Contains("past the 10", refusal.Message);
    }
}
