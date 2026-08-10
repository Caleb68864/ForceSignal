using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Combat;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers fire taken through the game: the roster supplies the weapon and the armour, the player
/// supplies the range, the posture and the firepower, and what comes back lands on the target.
/// </summary>
/// <remarks>
/// The fire engine itself is tested in <see cref="FireCombatTests"/> against the rulebook's worked
/// examples. These are about what the game does with the answer.
/// </remarks>
public sealed class GameFireTests
{
    // The worked firefight from FireCombatTests: three dice through, two hits, one of them a kill.
    private static readonly int[] AKillAndAStop = [6, 7, 5, 4, 5, 3, 5, 9, 4];

    [Fact]
    public void FireTakesCasualtiesOffTheTarget()
    {
        var game = GameFixtures.Firefight();

        var after = game.Fire(Volley(), new ScriptedDice(AKillAndAStop)).Value!;

        Assert.Equal(7, after.Status(GameFixtures.Bravo).FiguresAlive);
    }

    [Fact]
    public void EffectiveFireSuppressesTheTarget()
    {
        var game = GameFixtures.Firefight();

        var after = game.Fire(Volley(), new ScriptedDice(AKillAndAStop)).Value!;

        Assert.Equal(1, after.Status(GameFixtures.Bravo).SuppressionMarkers);
    }

    [Fact]
    public void SuppressionStacksNoHigherThanTheRulesAllow()
    {
        var game = GameFixtures.Firefight()
            .WithStatus(GameFixtures.Bravo, status => status with { SuppressionMarkers = 3 });

        var after = game.Fire(Volley(), new ScriptedDice(AKillAndAStop)).Value!;

        Assert.Equal(3, after.Status(GameFixtures.Bravo).SuppressionMarkers);
    }

    [Fact]
    public void TheWholeExchangeIsRecordedForTheTableToReadBack()
    {
        var game = GameFixtures.Firefight();

        var after = game.Fire(Volley(), new ScriptedDice(AKillAndAStop)).Value!;

        Assert.Contains(after.Log, entry => entry.Contains("Bravo Squad", StringComparison.Ordinal));
    }

    [Fact]
    public void FireSpendsAnActionOfTheActivation()
    {
        var game = GameFixtures.Firefight();

        var after = game.Fire(Volley(), new ScriptedDice(AKillAndAStop)).Value!;

        Assert.Contains(after.Session.CurrentFrame!.Steps, step => step.Kind.Contains("Fire", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSameWeaponCannotFireTwiceInOneActivation()
    {
        var game = GameFixtures.Firefight()
            .Fire(Volley(), new ScriptedDice(AKillAndAStop)).Value!;

        var again = game.Fire(Volley(), new ScriptedDice(AKillAndAStop));

        Assert.False(again.IsAllowed);
        Assert.False(string.IsNullOrWhiteSpace(again.Reason));
    }

    [Fact]
    public void FiringAtAUnitThatIsNotThereIsRefused()
    {
        var game = GameFixtures.Firefight();

        var refused = game.Fire(Volley() with { Target = new UnitId("ghost") }, new ScriptedDice(AKillAndAStop));

        Assert.False(refused.IsAllowed);
        Assert.Contains("ghost", refused.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void FiringAWeaponTheUnitDoesNotCarryIsRefused()
    {
        var game = GameFixtures.Firefight();

        var refused = game.Fire(Volley() with { WeaponName = "Railgun" }, new ScriptedDice(AKillAndAStop));

        Assert.False(refused.IsAllowed);
        Assert.Contains("Railgun", refused.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AWipedOutUnitIsNotStillShooting()
    {
        var game = GameFixtures.Firefight()
            .WithStatus(GameFixtures.Alpha, status => status with { FiguresAlive = 0 });

        var refused = game.Fire(Volley(), new ScriptedDice(AKillAndAStop));

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void FiringOutsideAnActivationIsRefused()
    {
        var game = GameFixtures.TwoSquadGame();

        var refused = game.Fire(Volley(), new ScriptedDice(AKillAndAStop));

        Assert.False(refused.IsAllowed);
    }

    private static FireCommand Volley() => new()
    {
        Firer = GameFixtures.Alpha,
        Target = GameFixtures.Bravo,
        WeaponName = "Rifles",
        FirepowerDie = QualityDie.D10,
        SupportDice = [QualityDie.D8],
        DistanceInches = 9,
        TargetPosture = new TargetPosture(CoverLevel.Soft),
    };
}
