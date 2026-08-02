using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The firing phase opens with a die-off. The winner fires one ship completely, then the players
/// alternate a ship at a time, and damage lands as it is rolled - so a ship can lose its guns
/// before its turn comes round.
/// </summary>
public sealed class InMemoryMatchServiceInitiativeTests
{
    [Fact]
    public void AdvanceTurn_OpensTheFiringPhaseWithADieOff()
    {
        // Blue rolls 5, Red rolls 2. Blue fires first.
        var table = InitiativeTable.Build(5, 2);

        var firing = table.Snapshot();

        Assert.Equal("Firing", firing.Phase);
        Assert.Equal(table.OwnerParticipantId, firing.FiringParticipantId);
        Assert.Contains(firing.MatchLog, entry => entry.Category == "Initiative"
            && entry.Message.Contains("Blue rolled 5", StringComparison.Ordinal)
            && entry.Message.Contains("Red rolled 2", StringComparison.Ordinal)
            && entry.Message.Contains("Blue fires first", StringComparison.Ordinal));
    }

    [Fact]
    public void AdvanceTurn_GivesTheInitiativeToWhoeverRollsHighest()
    {
        // Red out-rolls Blue this time.
        var table = InitiativeTable.Build(2, 6);

        Assert.Equal(table.OpponentParticipantId, table.Snapshot().FiringParticipantId);
    }

    [Fact]
    public void AdvanceTurn_RerollsATiedDieOff()
    {
        // Tied on 4s, then Red takes it with a 6 against a 3.
        var table = InitiativeTable.Build(4, 4, 3, 6);

        var firing = table.Snapshot();

        Assert.Equal(table.OpponentParticipantId, firing.FiringParticipantId);
        Assert.Contains(firing.MatchLog, entry => entry.Category == "Initiative"
            && entry.Message.Contains("Tied, rolling again", StringComparison.Ordinal));
    }

    [Fact]
    public void FireWeapon_OutOfTurnIsRefused()
    {
        var table = InitiativeTable.Build(5, 2);

        var error = Assert.Throws<InvalidOperationException>(() => table.FireRed());

        Assert.Contains("It is Blue's turn to fire", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CeaseFire_PassesTheTurnToTheOtherPlayer()
    {
        var table = InitiativeTable.Build(5, 2);

        table.FireBlue();
        var afterBlue = table.CeaseFireBlue();

        Assert.Equal(table.OpponentParticipantId, afterBlue.FiringParticipantId);
        Assert.Contains(afterBlue.MatchLog, entry => entry.Category == "Initiative"
            && entry.Message.Contains("Red fires next", StringComparison.Ordinal));

        // And now Red may fire while Blue may not.
        var blocked = Assert.Throws<InvalidOperationException>(() => table.FireBlueSecondShip());
        Assert.Contains("It is Red's turn to fire", blocked.Message, StringComparison.Ordinal);
        table.FireRed();
    }

    [Fact]
    public void CeaseFire_WithoutFiringPassesTheTurn()
    {
        // Declining to fire a ship is a legitimate activation: it uses the ship's turn.
        var table = InitiativeTable.Build(5, 2);

        var passed = table.CeaseFireBlue();

        Assert.Equal(table.OpponentParticipantId, passed.FiringParticipantId);
        Assert.Contains(passed.MatchLog, entry => entry.Message.Contains("held its fire", StringComparison.Ordinal));
    }

    [Fact]
    public void CeaseFire_ComesBackAroundWhenTheOtherPlayerHasNothingLeft()
    {
        var table = InitiativeTable.Build(5, 2);

        // Blue fires its first ship, Red activates its only ship, and the turn returns to Blue.
        table.FireBlue();
        table.CeaseFireBlue();
        table.FireRed();
        var afterRed = table.CeaseFireRed();

        Assert.Equal(table.OwnerParticipantId, afterRed.FiringParticipantId);
        table.FireBlueSecondShip();
    }

    [Fact]
    public void CeaseFire_LeavesNobodyWithTheInitiativeOnceEveryShipHasFired()
    {
        var table = InitiativeTable.Build(5, 2);

        table.FireBlue();
        table.CeaseFireBlue();
        table.FireRed();
        table.CeaseFireRed();
        table.FireBlueSecondShip();
        var finished = table.CeaseFireBlueSecondShip();

        Assert.Null(finished.FiringParticipantId);
        Assert.Contains(finished.MatchLog, entry => entry.Category == "Initiative"
            && entry.Message.Contains("Every ship has fired", StringComparison.Ordinal));
    }

    [Fact]
    public void AdvanceTurn_ClearsTheTurnOrderAndRollsAgainNextTurn()
    {
        var table = InitiativeTable.Build(5, 2, 2, 6);

        var orderEntry = table.AdvanceTurn();
        Assert.Equal("OrderEntry", orderEntry.Phase);
        Assert.Null(orderEntry.FiringParticipantId);
        Assert.Empty(orderEntry.ActivatedShipIds);

        // Next turn's die-off uses the next scripted pair, and Red wins it.
        var nextFiring = table.RunTurnToFiring();
        Assert.Equal(table.OpponentParticipantId, nextFiring.FiringParticipantId);
    }

    /// <summary>Blue with two ships against Red with one, all lined up bow to bow.</summary>
    private sealed record InitiativeTable(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid OwnerParticipantId,
        Guid OpponentParticipantId,
        Guid BlueLeadId,
        Guid BlueSecondId,
        Guid RedShipId,
        Guid BlueLeadMount,
        Guid BlueSecondMount,
        Guid RedMount)
    {
        public static InitiativeTable Build(params int[] initiativeFaces)
        {
            var dice = new ScriptedDice { Fallback = 4 };
            var service = new InMemoryMatchService(dice.Next);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Initiative Table"));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            Guid blueLeadMount = Guid.NewGuid(), blueSecondMount = Guid.NewGuid(), redMount = Guid.NewGuid();
            var blueLead = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Blue Lead", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 40, ArmorMax: 0, StartX: 20, StartY: 40,
                Weapons: [new WeaponMountDto(blueLeadMount, "Lead Battery", 2, 36, [FiringArc.Fore])]))
                .Ships.Single(s => s.Name == "Blue Lead");
            var blueSecond = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Blue Second", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 40, ArmorMax: 0, StartX: 24, StartY: 40,
                Weapons: [new WeaponMountDto(blueSecondMount, "Second Battery", 2, 36, [FiringArc.Fore])]))
                .Ships.Single(s => s.Name == "Blue Second");
            var redShip = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Red Lead", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: 40, ArmorMax: 0, StartX: 20, StartY: 20,
                Weapons: [new WeaponMountDto(redMount, "Red Battery", 2, 36, [FiringArc.Fore])]))
                .Ships.Single(s => s.Name == "Red Lead");

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);

            var table = new InitiativeTable(
                service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken,
                owner.ParticipantId, opponent.ParticipantId,
                blueLead.Id, blueSecond.Id, redShip.Id,
                blueLeadMount, blueSecondMount, redMount);

            // Script the die-off, then open the firing phase so the roll lands on the script.
            dice.Script(initiativeFaces);
            table.Dice = dice;
            table.RunTurnToFiring();
            return table;
        }

        /// <summary>The scripted die, so a test can lay out the next turn's rolls.</summary>
        public ScriptedDice Dice { get; set; } = new();

        /// <summary>Declares plotting done for both sides and advances into the firing phase.</summary>
        public MatchSnapshotDto RunTurnToFiring()
        {
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OwnerToken));
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OpponentToken));
            return Service.AdvanceTurn(MatchId, OwnerToken);
        }

        public MatchSnapshotDto Snapshot() => Service.GetSnapshot(MatchId);

        public MatchSnapshotDto AdvanceTurn() => Service.AdvanceTurn(MatchId, OwnerToken);

        public MatchSnapshotDto FireBlue() =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, BlueLeadId, RedShipId, BlueLeadMount, 10));

        public MatchSnapshotDto FireBlueSecondShip() =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, BlueSecondId, RedShipId, BlueSecondMount, 10));

        public MatchSnapshotDto FireRed() =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(OpponentToken, RedShipId, BlueLeadId, RedMount, 10));

        public MatchSnapshotDto CeaseFireBlue() =>
            Service.CeaseFire(MatchId, new CeaseFireRequest(OwnerToken, BlueLeadId));

        public MatchSnapshotDto CeaseFireBlueSecondShip() =>
            Service.CeaseFire(MatchId, new CeaseFireRequest(OwnerToken, BlueSecondId));

        public MatchSnapshotDto CeaseFireRed() =>
            Service.CeaseFire(MatchId, new CeaseFireRequest(OpponentToken, RedShipId));
    }
}
