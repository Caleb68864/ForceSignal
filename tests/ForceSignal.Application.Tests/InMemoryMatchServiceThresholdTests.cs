using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Completing a hull row makes every surviving system roll to stay alive. These drive the whole
/// path: a shot lands, a row finishes, systems are knocked out, and what is lost actually bites.
/// </summary>
public sealed class InMemoryMatchServiceThresholdTests
{
    [Fact]
    public void FireWeapon_ThatCompletesAHullRow_RollsForEverySurvivingSystem()
    {
        // Every die the top face: the attack lands hard, and the threshold rolls that follow come
        // off the same stream. The top face never loses a system, so this isolates the check itself
        // from its losses.
        var table = ThresholdTable.Build(fallbackFace: 8, targetHull: 9, targetScreens: 0);

        table.Fire();
        var result = table.CeaseFire();

        var target = result.Ships.Single(s => s.Id == table.TargetId);
        // Three unscreened top faces is 6 damage, which completes two rows of a 9-box hull.
        Assert.Equal(6, target.HullDamage);
        Assert.Equal(2, target.HullRowsCompleted);
        Assert.Equal([3, 3, 3], target.HullRows);
        var check = Assert.Single(result.MatchLog, entry => entry.Category == "Threshold");
        Assert.Contains("completed hull row 2 of 3", check.Message, StringComparison.Ordinal);
        // Two rows in one attack: the second threshold, one point worse for the extra row.
        Assert.Contains("+1 rows in one attack", check.Message, StringComparison.Ordinal);
        Assert.Contains("systems lost on 3 or less", check.Message, StringComparison.Ordinal);
        Assert.Contains("nothing knocked out", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_WhenThresholdRollsAreLow_KnocksOutSystemsAndSaysWhat()
    {
        // Top faces for the attack dice, then ones for the threshold rolls, so everything is lost.
        var table = ThresholdTable.Build(targetHull: 9, targetScreens: 2, fallbackFace: 8);
        table.Dice.Script(8, 8, 8, 1, 1, 1, 1, 1, 1, 1, 1);

        table.Fire();
        var result = table.CeaseFire();
        var target = result.Ships.Single(s => s.Id == table.TargetId);

        // Screens, drives and the mount all fail their rolls. Each screen level is its own
        // generator, so a pair of 1s takes both down - the rating still says what it was built with.
        Assert.Equal(2, target.ScreenRating);
        Assert.Equal(2, target.ScreenDamage);
        Assert.Equal(0, target.FireControlMax - target.FireControlDamage);
        Assert.True(target.DriveDamage > 0);
        Assert.True(target.Weapons.All(weapon => weapon.IsDestroyed));
        var check = Assert.Single(result.MatchLog, entry => entry.Category == "Threshold");
        Assert.Contains("drives", check.Message, StringComparison.Ordinal);
        Assert.Contains("last fire control lost", check.Message, StringComparison.Ordinal);
        Assert.Contains("screens down", check.Message, StringComparison.Ordinal);
        Assert.Contains("knocked out", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_ThatDoesNotFinishARow_RollsNoThresholdCheck()
    {
        // A single die on this profile's one-point face is not enough to finish a row of four.
        var table = ThresholdTable.Build(fallbackFace: 5, targetHull: 12, targetScreens: 0, attackDice: 1);

        table.Fire();
        var result = table.CeaseFire();

        Assert.Equal(1, result.Ships.Single(s => s.Id == table.TargetId).HullDamage);
        Assert.DoesNotContain(result.MatchLog, entry => entry.Category == "Threshold");
    }

    [Fact]
    public void FireWeapon_ThatDestroysTheShip_RollsNoThresholdCheck()
    {
        // A 4-box hull dies to a single volley, and a dead ship rolls nothing.
        var table = ThresholdTable.Build(fallbackFace: 8, targetHull: 4, targetScreens: 0);

        table.Fire();
        var result = table.CeaseFire();

        Assert.True(result.Ships.Single(s => s.Id == table.TargetId).IsDestroyed);
        Assert.DoesNotContain(result.MatchLog, entry => entry.Category == "Threshold");
    }

    [Fact]
    public void FireWeapon_RefusesAMountAThresholdCheckKnockedOut()
    {
        // The attacker's own mount is knocked out by hand to stand in for a threshold loss.
        var table = ThresholdTable.Build(fallbackFace: 6, targetHull: 20, targetScreens: 0);
        table.KnockOutAttackerMount();

        var error = Assert.Throws<InvalidOperationException>(() => table.Fire());

        Assert.Contains("knocked out by a threshold check", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FireWeapon_LostDrivesReduceTheThrustAvailableForOrders()
    {
        // Top faces for the attack, then a 1 on the drive roll and top faces for everything else.
        var table = ThresholdTable.Build(targetHull: 9, targetScreens: 0, fallbackFace: 8);
        table.Dice.Script(8, 8, 8, 1);

        table.Fire();
        var result = table.CeaseFire();
        var target = result.Ships.Single(s => s.Id == table.TargetId);

        // The first drive hit halves thrust rather than killing it outright.
        Assert.Equal(2, target.DriveDamage);
        Assert.Equal(4, target.ThrustRating);
        // Orders may now spend only the thrust that survives.
        table.AdvanceToOrderEntry();
        var error = Assert.Throws<InvalidOperationException>(() => table.OrderTarget(velocityDelta: 3));
        Assert.Contains("thrust rating", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An attacker with one fore mount lined up on a stationary target dead ahead.</summary>
    private sealed record ThresholdTable(
        InMemoryMatchService Service,
        ScriptedDice Dice,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        Guid AttackerId,
        Guid TargetId,
        Guid WeaponId)
    {
        public static ThresholdTable Build(int targetHull, int targetScreens, int attackDice = 3, int fallbackFace = 6)
        {
            var dice = new ScriptedDice { Fallback = fallbackFace };
            var service = new InMemoryMatchService(dice.Next);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Threshold Table", Rules: TestRules.Invented));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
                .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
                .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            var weaponId = Guid.NewGuid();
            var attacker = service.CreateShip(blueFleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Gunner", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 12, HullMax: 12, ArmorMax: 0,
                StartX: 20, StartY: 30,
                Weapons: [new WeaponMountDto(weaponId, "Class-3 Beam", attackDice, 36, [FiringArc.Fore])]))
                .Ships.Single(s => s.Name == "Gunner");
            var target = service.CreateShip(redFleet.Id, new CreateShipRequest(
                opponent.ParticipantToken, "Mark", "Cruiser", 4,
                InitialVelocity: 0, InitialCourse: 6, HullMax: targetHull, ArmorMax: 0,
                StartX: 20, StartY: 18, ScreenRating: targetScreens,
                Weapons: [new WeaponMountDto(Guid.NewGuid(), "Class-2 Beam", 2, 24, [FiringArc.Fore])]))
                .Ships.Single(s => s.Name == "Mark");

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
            var hold = new MovementOrder(0, 0, TurnDirection.None);
            service.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, attacker.Id, hold, "b"));
            service.CommitOrder(owner.MatchId, new CommitOrderRequest(opponent.ParticipantToken, target.Id, hold, "r"));
            service.RevealOrder(owner.MatchId, new RevealOrderRequest(owner.ParticipantToken, attacker.Id, hold, "b"));
            service.RevealOrder(owner.MatchId, new RevealOrderRequest(opponent.ParticipantToken, target.Id, hold, "r"));
            service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

            return new ThresholdTable(service, dice, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, attacker.Id, target.Id, weaponId);
        }

        public MatchSnapshotDto Fire() =>
            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, AttackerId, TargetId, WeaponId, 8));

        /// <summary>Closes the attacker's volley, which is when its threshold checks roll.</summary>
        public MatchSnapshotDto CeaseFire() =>
            Service.CeaseFire(MatchId, new CeaseFireRequest(OwnerToken, AttackerId));

        public void KnockOutAttackerMount()
        {
            var snapshot = Service.GetSnapshot(MatchId);
            var attacker = snapshot.Ships.Single(s => s.Id == AttackerId);
            var mount = attacker.Weapons.Single(weapon => weapon.Id == WeaponId);
            Service.UpdateShipProfile(AttackerId, new UpdateShipProfileRequest(
                OwnerToken, attacker.Name, attacker.ClassName, attacker.ThrustRating,
                attacker.CurrentVelocity, attacker.CurrentCourse, attacker.HullMax, attacker.ArmorMax,
                attacker.PositionX, attacker.PositionY, attacker.ScreenRating,
                Weapons: [mount with { IsDestroyed = true }]));
        }

        /// <summary>Closes the firing phase so the next turn's orders can be plotted.</summary>
        public void AdvanceToOrderEntry() => Service.AdvanceTurn(MatchId, OwnerToken);

        /// <summary>Tries to plot an order for the target, which is how lost thrust becomes visible.</summary>
        public void OrderTarget(int velocityDelta) =>
            Service.CommitOrder(MatchId, new CommitOrderRequest(
                OpponentToken, TargetId, new MovementOrder(velocityDelta, 0, TurnDirection.None), "next"));
    }
}
