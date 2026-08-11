using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A match is played against a profile its players fill in, and the numbers that differ between two
/// tables' rulebooks follow from that profile rather than from anything this app knows.
/// </summary>
/// <remarks>
/// Both profiles used here are invented. Where an older version of these tests named two published
/// rules layers and asserted their numbers, they now assert only that the app reads whatever it is
/// handed - which is the property that actually matters, and the one that keeps the numbers out of
/// this repository.
/// </remarks>
public sealed class InMemoryMatchServiceRulesProfileTests
{
    /// <summary>A profile with a lower screen ceiling, longer fighter legs and enhanced needles.</summary>
    private static readonly RulesProfile Other = TestRules.Invented with
    {
        Name = "The Other Set",
        MaxScreenLevel = 1,
        FighterMoveAllowance = 30,
        NeedleBeamRange = 11,
        EnhancedNeedleBeams = true,
        NeedleHullDamageRoll = 7,
    };

    [Fact]
    public void AMatchOpensAgainstTheProfileItWasGiven()
    {
        var service = new InMemoryMatchService(() => 4);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Profiles", Rules: TestRules.Invented));

        Assert.Equal(TestRules.Invented.Name, service.GetSnapshot(owner.MatchId).Rules.Name);
    }

    [Fact]
    public void AMatchOpenedWithNoProfileHasNothingToPlayAgainstYet()
    {
        var service = new InMemoryMatchService(() => 4);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Profiles"));

        // Blank rather than a helpful default: this app ships nobody's numbers.
        Assert.False(service.GetSnapshot(owner.MatchId).Rules.IsPlayable);
    }

    [Fact]
    public void AShipCannotCarryScreensAboveTheProfilesCeiling()
    {
        var table = ProfileTable.Build(Other);

        Assert.Equal(1, table.AddShip(screens: 3).ScreenRating);
    }

    [Fact]
    public void AProfileWithAHigherCeilingKeepsTheScreens()
    {
        var table = ProfileTable.Build(TestRules.Invented);

        Assert.Equal(2, table.AddShip(screens: 3).ScreenRating);
    }

    [Fact]
    public void ChangingProfileBringsScreensDownToTheNewCeiling()
    {
        var table = ProfileTable.Build(TestRules.Invented);
        table.AddShip(screens: 2);

        var snapshot = table.SwitchTo(Other);

        Assert.Equal(Other.Name, snapshot.Rules.Name);
        Assert.Equal(1, snapshot.Ships.Single(ship => ship.Name == "Vigilant").ScreenRating);
        Assert.Contains(
            snapshot.MatchLog,
            entry => entry.Message.Contains("Playing against 'The Other Set'", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIncompleteProfileIsRefusedWithWhatIsMissing()
    {
        var table = ProfileTable.Build(TestRules.Invented);

        var error = Assert.Throws<InvalidOperationException>(() => table.SwitchTo(RulesProfile.Empty));

        Assert.Contains("not complete enough", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProfileIsSettledDuringFleetSetup()
    {
        var table = ProfileTable.Build(TestRules.Invented);
        table.AddShip(screens: 1);
        table.StartPlaying();

        var error = Assert.Throws<InvalidOperationException>(() => table.SwitchTo(Other));

        Assert.Contains("settled during fleet setup", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingTheProfileIsTheOwnersCall()
    {
        var table = ProfileTable.Build(TestRules.Invented);

        Assert.Throws<UnauthorizedAccessException>(() =>
            table.Service.UpdateRulesProfile(table.MatchId, new UpdateRulesProfileRequest(table.OpponentToken, Other)));
    }

    [Fact]
    public void AFighterGroupFliesAsFarAsItsProfileAllows()
    {
        var near = ProfileTable.Build(TestRules.Invented);
        var group = near.AddFighterGroup();
        near.StartPlaying();
        var tooFar = Assert.Throws<InvalidOperationException>(() => near.Fly(group.Id, 20, 22));
        Assert.Contains($"past the {TestRules.Invented.FighterMoveAllowance}", tooFar.Message, StringComparison.Ordinal);

        var far = ProfileTable.Build(Other);
        var farGroup = far.AddFighterGroup();
        far.StartPlaying();
        var flown = far.Fly(farGroup.Id, 20, 22);
        Assert.Equal(22m, flown.Ships.Single(ship => ship.Id == farGroup.Id).PositionY);
    }

    [Fact]
    public void APlainNeedleTakesTheSystemAndAnEnhancedOneAlsoHolesTheHull()
    {
        // The kill roll takes the named system under either profile. Only an enhanced needle also
        // puts a point into the hull, and that point goes past armour boxes still standing.
        var plain = NeedleTable.Build(TestRules.Invented);
        plain.Dice.Script(8);
        var plainTarget = plain.Needle(ShipSystemKind.FireControl).Ships.Single(ship => ship.Id == plain.TargetId);

        var enhanced = NeedleTable.Build(Other);
        enhanced.Dice.Script(8);
        var enhancedResult = enhanced.Needle(ShipSystemKind.FireControl);
        var enhancedTarget = enhancedResult.Ships.Single(ship => ship.Id == enhanced.TargetId);

        Assert.Equal(1, plainTarget.FireControlDamage);
        Assert.Equal(0, plainTarget.HullDamage);

        Assert.Equal(1, enhancedTarget.FireControlDamage);
        Assert.Equal(1, enhancedTarget.HullDamage);
        // The target carries armour, and the needle went straight past it.
        Assert.Equal(0, enhancedTarget.ArmorDamage);
        Assert.Contains(
            enhancedResult.MatchLog,
            entry => entry.Message.Contains("ignoring armour", StringComparison.Ordinal));
    }

    [Fact]
    public void TheBloodRollIsAMissForAPlainNeedleAndBloodWithoutTheSystemForAnEnhancedOne()
    {
        var plain = NeedleTable.Build(TestRules.Invented);
        plain.Dice.Script(7);
        var plainResult = plain.Needle(ShipSystemKind.FireControl);
        var plainTarget = plainResult.Ships.Single(ship => ship.Id == plain.TargetId);

        var enhanced = NeedleTable.Build(Other);
        enhanced.Dice.Script(7);
        var enhancedResult = enhanced.Needle(ShipSystemKind.FireControl);
        var enhancedTarget = enhancedResult.Ships.Single(ship => ship.Id == enhanced.TargetId);

        Assert.Equal(0, plainTarget.FireControlDamage);
        Assert.Equal(0, plainTarget.HullDamage);
        Assert.Contains(plainResult.MatchLog, entry => entry.Message.Contains("nothing hit", StringComparison.Ordinal));

        // The system rides it out; the hull does not.
        Assert.Equal(0, enhancedTarget.FireControlDamage);
        Assert.Equal(1, enhancedTarget.HullDamage);
        Assert.Contains(
            enhancedResult.MatchLog,
            entry => entry.Message.Contains("system held, hull holed", StringComparison.Ordinal));
    }

    [Fact]
    public void ANeedleReachesAsFarAsItsProfileSays()
    {
        // Whatever range the mount was written down with, the profile is what caps it.
        var near = NeedleTable.Build(TestRules.Invented);
        near.Dice.Script(8);
        var tooFar = Assert.Throws<InvalidOperationException>(() => near.Needle(ShipSystemKind.FireControl, range: 10));

        var far = NeedleTable.Build(Other);
        far.Dice.Script(8);
        var reached = far.Needle(ShipSystemKind.FireControl, range: 10);

        Assert.Contains("out of range", tooFar.Message, StringComparison.Ordinal);
        Assert.True(Assert.Single(reached.FiringResults).IsHit);
    }

    /// <summary>A needle-armed cruiser and an armoured target, under a chosen profile.</summary>
    private sealed record NeedleTable(
        InMemoryMatchService Service,
        ScriptedDice Dice,
        Guid MatchId,
        string OwnerToken,
        Guid AttackerId,
        Guid TargetId,
        Guid NeedleId)
    {
        public static NeedleTable Build(RulesProfile rules)
        {
            var dice = new ScriptedDice { Fallback = 4 };
            var service = new InMemoryMatchService(dice.Next);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Needles", Rules: rules));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            var needleId = Guid.NewGuid();
            // The mount is written down with the longer reach; the profile is what actually caps it.
            var attacker = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Stiletto", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 20, ArmorMax: 0,
                StartX: 20, StartY: 34,
                Weapons: [new WeaponMountDto(needleId, "Needle Beam", 1, 12, [FiringArc.Fore], Kind: WeaponKind.NeedleBeam)],
                FireControlMax: 2)).Ships.Single(s => s.Name == "Stiletto");

            var target = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Mark", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: 20, ArmorMax: 6,
                StartX: 20, StartY: 28, FireControlMax: 2)).Ships.Single(s => s.Name == "Mark");

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(owner.ParticipantToken));
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(opponent.ParticipantToken));
            service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

            return new NeedleTable(service, dice, owner.MatchId, owner.ParticipantToken, attacker.Id, target.Id, needleId);
        }

        public MatchSnapshotDto Needle(ShipSystemKind system, int range = 6) =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(
                OwnerToken, AttackerId, TargetId, NeedleId, range, TargetSystem: system));
    }

    /// <summary>A match under a chosen profile, with ships added as each test needs them.</summary>
    private sealed record ProfileTable(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid FleetId)
    {
        public static ProfileTable Build(RulesProfile rules)
        {
            var service = new InMemoryMatchService(() => 4);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Profiles", Rules: rules));
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
            return new ProfileTable(service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, fleet.Id);
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

        public MatchSnapshotDto SwitchTo(RulesProfile rules) =>
            Service.UpdateRulesProfile(MatchId, new UpdateRulesProfileRequest(OwnerToken, rules));

        /// <summary>Leaves fleet setup, which is when the profile stops being negotiable.</summary>
        public void StartPlaying()
        {
            Service.SetReady(MatchId, OwnerToken, true);
            Service.SetReady(MatchId, OpponentToken, true);
        }

        public MatchSnapshotDto Fly(Guid groupId, decimal x, decimal y) =>
            Service.MoveFighterGroup(MatchId, new MoveFighterGroupRequest(OwnerToken, groupId, x, y));
    }
}
