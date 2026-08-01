using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Fire control directs the guns: a ship with none cannot shoot, and each working system holds one
/// target for the turn. Also covers when a threshold check rolls - after the firing ship is done,
/// not after each mount - so one volley earns one check against the deepest row it reached.
/// </summary>
public sealed class InMemoryMatchServiceFireControlTests
{
    [Fact]
    public void FireWeapon_WithEveryFireControlKnockedOut_IsRefused()
    {
        var table = FireControlTable.Build(fireControlMax: 1);
        table.KnockOutFireControl(1);

        var error = Assert.Throws<InvalidOperationException>(() => table.Fire(table.FirstMount, table.PrimaryTargetId));

        Assert.Contains("no working fire control", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_WithOneFireControl_HoldsTheShipToASingleTarget()
    {
        var table = FireControlTable.Build(fireControlMax: 1);

        // Everything may fire, so long as it all fires at the one target that firecon is holding.
        table.Fire(table.FirstMount, table.PrimaryTargetId);
        table.Fire(table.SecondMount, table.PrimaryTargetId);

        var error = Assert.Throws<InvalidOperationException>(() => table.Fire(table.ThirdMount, table.SecondaryTargetId));
        Assert.Contains("1 working fire control system", error.Message, StringComparison.Ordinal);
        Assert.Contains("already engaging Mark", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_WithTwoFireControls_SplitsFireBetweenTwoTargets()
    {
        var table = FireControlTable.Build(fireControlMax: 2);

        table.Fire(table.FirstMount, table.PrimaryTargetId);
        var result = table.Fire(table.SecondMount, table.SecondaryTargetId);

        Assert.Equal(2, result.FiringResults.Select(shot => shot.TargetShipId).Distinct().Count());

        // A third target is one too many.
        var error = Assert.Throws<InvalidOperationException>(() => table.Fire(table.ThirdMount, table.ThirdTargetId));
        Assert.Contains("2 working fire control systems", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_AfterAFireControlIsLost_StopsTheShipTakingOnANewTarget()
    {
        var table = FireControlTable.Build(fireControlMax: 2);
        table.Fire(table.FirstMount, table.PrimaryTargetId);
        table.KnockOutFireControl(1);

        // One firecon left, and it is already holding the first target.
        var error = Assert.Throws<InvalidOperationException>(() => table.Fire(table.SecondMount, table.SecondaryTargetId));

        Assert.Contains("1 working fire control system", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CeaseFire_RollsOneThresholdCheckForTheWholeVolley()
    {
        // Two mounts of three 6s each is twelve damage, tearing through two rows of a 20-box hull.
        // The rules make that one check against the deeper row, one point worse for the extra row -
        // not the two separate first- and second-row checks a per-shot resolution would give.
        var table = FireControlTable.Build(fireControlMax: 1, rollDie: () => 6, targetHull: 20);

        table.Fire(table.FirstMount, table.PrimaryTargetId);
        table.Fire(table.SecondMount, table.PrimaryTargetId);
        var result = table.CeaseFire();

        var check = Assert.Single(result.MatchLog, entry => entry.Category == "Threshold");
        Assert.Contains("completed hull row 2 of 4", check.Message, StringComparison.Ordinal);
        Assert.Contains("+1 rows in one attack", check.Message, StringComparison.Ordinal);
        Assert.Contains("systems lost on 3 or less", check.Message, StringComparison.Ordinal);
        Assert.Null(result.FiringShipId);
    }

    [Fact]
    public void FireWeapon_LeavesTheVolleyOpenUntilItIsClosed()
    {
        var table = FireControlTable.Build(fireControlMax: 1, rollDie: () => 6, targetHull: 20);

        var afterShot = table.Fire(table.FirstMount, table.PrimaryTargetId);

        // The check is owed but not yet rolled: the ship may still fire its other mounts.
        Assert.Equal(table.AttackerId, afterShot.FiringShipId);
        Assert.DoesNotContain(afterShot.MatchLog, entry => entry.Category == "Threshold");

        var afterCease = table.CeaseFire();
        Assert.Null(afterCease.FiringShipId);
        Assert.Contains(afterCease.MatchLog, entry => entry.Category == "Threshold");
    }

    [Fact]
    public void FireWeapon_FromASecondShipClosesTheFirstShipsVolley()
    {
        var table = FireControlTable.Build(fireControlMax: 1, rollDie: () => 6, targetHull: 20, secondAttacker: true);

        table.Fire(table.FirstMount, table.PrimaryTargetId);
        var result = table.FireFromSecondShip();

        // The first ship's checks rolled as soon as another ship opened up.
        Assert.Contains(result.MatchLog, entry => entry.Category == "Threshold");
        Assert.Equal(table.SecondAttackerId, result.FiringShipId);
    }

    [Fact]
    public void AdvanceTurn_ClosesAVolleyLeftOpenAtTheEndOfTheFiringPhase()
    {
        var table = FireControlTable.Build(fireControlMax: 1, rollDie: () => 6, targetHull: 20);
        table.Fire(table.FirstMount, table.PrimaryTargetId);

        var result = table.AdvanceTurn();

        Assert.Contains(result.MatchLog, entry => entry.Category == "Threshold");
        Assert.Null(result.FiringShipId);
    }

    [Fact]
    public void CeaseFire_ForAShipThatNeverFired_DoesNothing()
    {
        var table = FireControlTable.Build(fireControlMax: 1);

        var result = table.CeaseFire();

        Assert.DoesNotContain(result.MatchLog, entry => entry.Category == "Threshold");
        Assert.Null(result.FiringShipId);
    }

    /// <summary>A gunship with three fore mounts and three targets lined up ahead of it.</summary>
    private sealed record FireControlTable(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid AttackerId,
        Guid SecondAttackerId,
        Guid SecondAttackerMount,
        Guid FirstMount,
        Guid SecondMount,
        Guid ThirdMount,
        Guid PrimaryTargetId,
        Guid SecondaryTargetId,
        Guid ThirdTargetId)
    {
        public static FireControlTable Build(int fireControlMax, Func<int>? rollDie = null, int targetHull = 40, bool secondAttacker = false)
        {
            var service = new InMemoryMatchService(rollDie ?? (() => 4));
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Fire Control Table"));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            Guid first = Guid.NewGuid(), second = Guid.NewGuid(), third = Guid.NewGuid(), secondShipMount = Guid.NewGuid();
            var attacker = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Gunner", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 12, ArmorMax: 0,
                StartX: 20, StartY: 40,
                Weapons:
                [
                    new WeaponMountDto(first, "Battery A", 3, 36, [FiringArc.Fore]),
                    new WeaponMountDto(second, "Battery B", 3, 36, [FiringArc.Fore]),
                    new WeaponMountDto(third, "Battery C", 3, 36, [FiringArc.Fore]),
                ],
                FireControlMax: fireControlMax)).Ships.Single(s => s.Name == "Gunner");

            var consort = secondAttacker
                ? service.CreateShip(blueFleet.Id, new CreateShipRequest(
                    owner.ParticipantToken, "Consort", "Cruiser", 4,
                    InitialVelocity: 0, InitialCourse: 12, HullMax: 12, ArmorMax: 0,
                    StartX: 24, StartY: 40,
                    Weapons: [new WeaponMountDto(secondShipMount, "Consort Battery", 3, 36, [FiringArc.Fore])],
                    FireControlMax: 1)).Ships.Single(s => s.Name == "Consort")
                : null;

            // Three marks strung out ahead, all inside the fore arc.
            var marks = new[] { ("Mark", 20m, 24m), ("Mark Two", 22m, 22m), ("Mark Three", 18m, 22m) }
                .Select(spec => service.CreateShip(redFleet.Id, new CreateShipRequest(
                    opponent.ParticipantToken, spec.Item1, "Cruiser", 4,
                    InitialVelocity: 0, InitialCourse: 6, HullMax: targetHull, ArmorMax: 0,
                    StartX: spec.Item2, StartY: spec.Item3)).Ships.Single(s => s.Name == spec.Item1))
                .ToArray();

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
            var hold = new MovementOrder(0, 0, TurnDirection.None);
            var crews = new List<(string Token, Guid ShipId)> { (owner.ParticipantToken, attacker.Id) };
            if (consort is not null)
            {
                crews.Add((owner.ParticipantToken, consort.Id));
            }

            crews.AddRange(marks.Select(mark => (opponent.ParticipantToken, mark.Id)));
            foreach (var (token, shipId) in crews)
            {
                service.CommitOrder(owner.MatchId, new CommitOrderRequest(token, shipId, hold, shipId.ToString()));
            }

            foreach (var (token, shipId) in crews)
            {
                service.RevealOrder(owner.MatchId, new RevealOrderRequest(token, shipId, hold, shipId.ToString()));
            }

            service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

            return new FireControlTable(
                service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken,
                attacker.Id, consort?.Id ?? Guid.Empty, secondShipMount,
                first, second, third,
                marks[0].Id, marks[1].Id, marks[2].Id);
        }

        public MatchSnapshotDto Fire(Guid weaponId, Guid targetId) =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, AttackerId, targetId, weaponId, 8));

        public MatchSnapshotDto FireFromSecondShip() =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, SecondAttackerId, PrimaryTargetId, SecondAttackerMount, 8));

        public MatchSnapshotDto CeaseFire() =>
            Service.CeaseFire(MatchId, new CeaseFireRequest(OwnerToken, AttackerId));

        public MatchSnapshotDto AdvanceTurn() => Service.AdvanceTurn(MatchId, OwnerToken);

        /// <summary>Marks fire control systems as knocked out, as a threshold check would.</summary>
        public void KnockOutFireControl(int count)
        {
            var attacker = Service.GetSnapshot(MatchId).Ships.Single(s => s.Id == AttackerId);
            Service.UpdateShipDamage(AttackerId, new UpdateShipDamageRequest(
                OwnerToken, attacker.HullDamage, attacker.ArmorDamage,
                FireControlDamage: attacker.FireControlDamage + count,
                DriveDamage: attacker.DriveDamage,
                WeaponDamage: attacker.WeaponDamage));
        }
    }
}
