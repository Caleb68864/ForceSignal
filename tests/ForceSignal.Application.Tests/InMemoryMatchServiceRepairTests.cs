using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Damage control works between turns, while orders are being written. Parties jury-rig systems lost
/// to threshold checks - one repairs on a 6, more on the same job lower the number - and a failure can
/// be tried again next turn. Hull damage never comes back, and neither do dead parties.
/// </summary>
public sealed class InMemoryMatchServiceRepairTests
{
    [Fact]
    public void AttemptRepairs_BringsFireControlBackOnASix()
    {
        var table = RepairTable.Build();
        table.BreakFireControl();
        table.Dice.Script(6);

        var result = table.Repair(new RepairJobDto(ShipSystemKind.FireControl, null, 1));

        Assert.Equal(0, table.ShipIn(result).FireControlDamage);
        Assert.Contains(result.MatchLog, entry => entry.Category == "Repair"
            && entry.Message.Contains("fire control back online (needed 6, rolled 6)", StringComparison.Ordinal));
    }

    [Fact]
    public void AttemptRepairs_LeavesTheSystemDownOnAShortRollAndSaysSo()
    {
        var table = RepairTable.Build();
        table.BreakFireControl();
        table.Dice.Script(5);

        var result = table.Repair(new RepairJobDto(ShipSystemKind.FireControl, null, 1));

        Assert.Equal(1, table.ShipIn(result).FireControlDamage);
        Assert.Contains(result.MatchLog, entry => entry.Message.Contains("fire control still down", StringComparison.Ordinal));
    }

    [Fact]
    public void AttemptRepairs_LowersTheNumberNeededWhenPartiesCrowdAJob()
    {
        // Three parties on one job repair on a 4 or better, and they roll once between them.
        var table = RepairTable.Build(parties: 3);
        table.BreakFireControl();
        table.Dice.Script(4);

        var result = table.Repair(new RepairJobDto(ShipSystemKind.FireControl, null, 3));

        Assert.Equal(0, table.ShipIn(result).FireControlDamage);
        Assert.Contains(result.MatchLog, entry => entry.Message.Contains("needed 4, rolled 4", StringComparison.Ordinal));
    }

    [Fact]
    public void AttemptRepairs_BringsAKnockedOutMountBack()
    {
        var table = RepairTable.Build();
        table.BreakTheMount();
        table.Dice.Script(6);

        var result = table.Repair(new RepairJobDto(ShipSystemKind.Weapon, table.MountId, 1));

        Assert.False(table.ShipIn(result).Weapons.Single(mount => mount.Id == table.MountId).IsDestroyed);
    }

    [Fact]
    public void AttemptRepairs_BringsDeadDrivesBackInHalves()
    {
        // Drives were lost outright: one success gets half the thrust back, a second clears the rest.
        var table = RepairTable.Build(parties: 2);
        table.KillDrives();
        table.Dice.Script(6);

        var half = table.Repair(new RepairJobDto(ShipSystemKind.Drive, null, 1));
        Assert.Equal(2, table.ShipIn(half).DriveDamage);

        table.NextTurn();
        table.Dice.Script(6);
        var whole = table.Repair(new RepairJobDto(ShipSystemKind.Drive, null, 1));
        Assert.Equal(0, table.ShipIn(whole).DriveDamage);
    }

    [Fact]
    public void AttemptRepairs_RefusesMorePartiesThanTheShipHas()
    {
        var table = RepairTable.Build(parties: 1);
        table.BreakFireControl();
        table.BreakTheMount();

        var error = Assert.Throws<InvalidOperationException>(() => table.Repair(
            new RepairJobDto(ShipSystemKind.FireControl, null, 1),
            new RepairJobDto(ShipSystemKind.Weapon, table.MountId, 1)));

        Assert.Contains("has 1 damage control party and 2 were assigned", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptRepairs_RefusesAJobOnSomethingThatIsNotBroken()
    {
        var table = RepairTable.Build();

        var error = Assert.Throws<InvalidOperationException>(() => table.Repair(
            new RepairJobDto(ShipSystemKind.FireControl, null, 1)));

        Assert.Contains("no fire control to repair", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptRepairs_RefusesEverythingWhenOneJobIsInvalid()
    {
        // Jobs are assigned before any dice, so a bad one stops the whole attempt rather than half-running.
        var table = RepairTable.Build(parties: 2);
        table.BreakFireControl();
        table.Dice.Script(6, 6);

        Assert.Throws<InvalidOperationException>(() => table.Repair(
            new RepairJobDto(ShipSystemKind.FireControl, null, 1),
            new RepairJobDto(ShipSystemKind.Drive, null, 1)));

        // The fire control job never rolled, so the damage is still there and the turn is not spent.
        Assert.Equal(1, table.Ship().FireControlDamage);
        table.Dice.Script(6);
        Assert.Equal(0, table.ShipIn(table.Repair(new RepairJobDto(ShipSystemKind.FireControl, null, 1))).FireControlDamage);
    }

    [Fact]
    public void AttemptRepairs_OnlyOncePerTurn()
    {
        var table = RepairTable.Build(parties: 2);
        table.BreakFireControl();
        table.BreakTheMount();
        table.Dice.Script(1);
        table.Repair(new RepairJobDto(ShipSystemKind.FireControl, null, 1));

        var error = Assert.Throws<InvalidOperationException>(() => table.Repair(
            new RepairJobDto(ShipSystemKind.Weapon, table.MountId, 1)));

        Assert.Contains("already worked its damage control this turn", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptRepairs_RefusesAShipWithNoPartiesLeft()
    {
        var table = RepairTable.Build(parties: 0);
        table.BreakFireControl();

        var error = Assert.Throws<InvalidOperationException>(() => table.Repair(
            new RepairJobDto(ShipSystemKind.FireControl, null, 1)));

        Assert.Contains("no damage control parties left", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptRepairs_RefusesWorkThatIsBeyondDamageControl()
    {
        var table = RepairTable.Build();
        table.BreakFireControl();

        var error = Assert.Throws<InvalidOperationException>(() => table.Repair(
            new RepairJobDto(ShipSystemKind.Screen, null, 1)));

        Assert.Contains("cannot be repaired by damage control", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptRepairs_RefusesDuringTheFiringPhase()
    {
        var table = RepairTable.Build();
        table.BreakFireControl();
        table.IntoFiringPhase();

        var error = Assert.Throws<InvalidOperationException>(() => table.Repair(
            new RepairJobDto(ShipSystemKind.FireControl, null, 1)));

        Assert.Contains("works between turns", error.Message, StringComparison.Ordinal);
    }

    /// <summary>One damaged cruiser with a mount, drives, and parties to fix them.</summary>
    private sealed record RepairTable(
        InMemoryMatchService Service,
        ScriptedDice Dice,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid ShipId,
        Guid MountId)
    {
        public static RepairTable Build(int parties = 2)
        {
            var dice = new ScriptedDice { Fallback = 4 };
            var service = new InMemoryMatchService(dice.Next);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Repair Table"));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            var mountId = Guid.NewGuid();
            var ship = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Patchwork", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 20, ArmorMax: 0,
                StartX: 20, StartY: 40, FireControlMax: 2,
                Weapons: [new WeaponMountDto(mountId, "Class-3 Beam", 3, 36, [FiringArc.Fore])],
                DamageControlParties: parties)).Ships.Single(s => s.Name == "Patchwork");
            service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Mark", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: 20, ArmorMax: 0,
                StartX: 20, StartY: 20));

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);

            return new RepairTable(service, dice, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, ship.Id, mountId);
        }

        public ShipDto Ship() => Service.GetSnapshot(MatchId).Ships.Single(s => s.Id == ShipId);

        public ShipDto ShipIn(MatchSnapshotDto snapshot) => snapshot.Ships.Single(s => s.Id == ShipId);

        public MatchSnapshotDto Repair(params RepairJobDto[] jobs) =>
            Service.AttemptRepairs(ShipId, new AttemptRepairsRequest(OwnerToken, jobs));

        /// <summary>Knocks out one fire control system by hand, as a threshold check would.</summary>
        public void BreakFireControl() => SetDamage(fireControl: 1);

        public void KillDrives() => SetDamage(drive: 4);

        private void SetDamage(int fireControl = 0, int drive = 0)
        {
            var ship = Ship();
            Service.UpdateShipDamage(ShipId, new UpdateShipDamageRequest(
                OwnerToken, ship.HullDamage, ship.ArmorDamage,
                fireControl == 0 ? ship.FireControlDamage : fireControl,
                drive == 0 ? ship.DriveDamage : drive,
                ship.WeaponDamage));
        }

        public void BreakTheMount()
        {
            var ship = Ship();
            var mount = ship.Weapons.Single(weapon => weapon.Id == MountId);
            Service.UpdateShipProfile(ShipId, new UpdateShipProfileRequest(
                OwnerToken, ship.Name, ship.ClassName, ship.ThrustRating, ship.CurrentVelocity, ship.CurrentCourse,
                ship.HullMax, ship.ArmorMax, ship.PositionX, ship.PositionY, ship.ScreenRating,
                Weapons: [mount with { IsDestroyed = true }],
                FireControlMax: ship.FireControlMax,
                DamageControlParties: ship.DamageControlParties));
        }

        /// <summary>Runs the turn out and back to order entry, so damage control can work again.</summary>
        public void NextTurn()
        {
            IntoFiringPhase();
            Service.AdvanceTurn(MatchId, OwnerToken);
        }

        public void IntoFiringPhase()
        {
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OwnerToken));
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OpponentToken));
            Service.AdvanceTurn(MatchId, OwnerToken);
        }
    }
}
