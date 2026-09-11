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
/// The fire engine itself is tested in <see cref="FireCombatTests"/> against a worked firefight and
/// an invented range table. These are about what the game does with the answer - and, since the range
/// table became the players', about the game refusing a shot the table cannot settle before it costs
/// anything.
/// </remarks>
public sealed class GameFireTests
{
    [Fact]
    public void FireTakesCasualtiesOffTheTarget()
    {
        var game = GameFixtures.Firefight();

        var after = game.Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        Assert.Equal(7, after.Status(GameFixtures.Bravo).FiguresAlive);
    }

    [Fact]
    public void EffectiveFireSuppressesTheTarget()
    {
        var game = GameFixtures.Firefight();

        var after = game.Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        Assert.Equal(1, after.Status(GameFixtures.Bravo).SuppressionMarkers);
    }

    [Fact]
    public void SuppressionStacksNoHigherThanTheRulesAllow()
    {
        var game = GameFixtures.Firefight()
            .WithStatus(GameFixtures.Bravo, status => status with { SuppressionMarkers = 3 });

        var after = game.Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        Assert.Equal(3, after.Status(GameFixtures.Bravo).SuppressionMarkers);
    }

    [Fact]
    public void TheWholeExchangeIsRecordedForTheTableToReadBack()
    {
        var game = GameFixtures.Firefight();

        var after = game.Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        Assert.Contains(after.Log, entry => entry.Contains("Bravo Squad", StringComparison.Ordinal));
    }

    [Fact]
    public void FireSpendsAnActionOfTheActivation()
    {
        var game = GameFixtures.Firefight();

        var after = game.Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        Assert.Contains(after.Session.CurrentFrame!.Steps, step => step.Kind.Contains("Fire", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSameWeaponCannotFireTwiceInOneActivation()
    {
        var game = GameFixtures.Firefight()
            .Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        var again = game.Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.False(again.IsAllowed);
        Assert.False(string.IsNullOrWhiteSpace(again.Reason));
    }

    [Fact]
    public void FiringAtAUnitThatIsNotThereIsRefused()
    {
        var game = GameFixtures.Firefight();

        var refused = game.Fire(GameFixtures.Volley() with { Target = new UnitId("ghost") }, new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.False(refused.IsAllowed);
        Assert.Contains("ghost", refused.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void FiringAWeaponTheUnitDoesNotCarryIsRefused()
    {
        var game = GameFixtures.Firefight();

        var refused = game.Fire(GameFixtures.Volley() with { WeaponName = "Railgun" }, new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.False(refused.IsAllowed);
        Assert.Contains("Railgun", refused.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AWipedOutUnitIsNotStillShooting()
    {
        var game = GameFixtures.Firefight()
            .WithStatus(GameFixtures.Alpha, status => status with { FiguresAlive = 0 });

        var refused = game.Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.False(refused.IsAllowed);
        Assert.Contains("nobody left", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FiringOutsideAnActivationIsRefused()
    {
        var game = GameFixtures.TwoSquadGame();

        var refused = game.Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.False(refused.IsAllowed);
        Assert.Contains("Nothing is activated", refused.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AGameWithNoRangeTableRefusesTheShotAndSaysWhatItWants()
    {
        // The game every stored StarGrunt row reads back as, and what a create request without a
        // profile makes. Its first shot used to be settled on a band the size of the firer's die and
        // a walk from the bottom of the ladder; now it is refused, naming the entry.
        var game = GameFixtures.Firefight();
        var dice = new ScriptedDice(GameFixtures.AKillAndAStop);

        var refused = game.Fire(GameFixtures.Volley(), dice, TestRangeTable.Blank, GameFixtures.HitsARifleman());

        Assert.False(refused.IsAllowed);
        Assert.Contains("range band is for D8 troops", refused.Reason!, StringComparison.Ordinal);
        // Reached-the-subject: this is the table's refusal, not some other one on the way there.
        Assert.Contains("range table", refused.Reason!, StringComparison.Ordinal);
        Assert.Equal(GameFixtures.AKillAndAStop.Length, dice.Remaining);
    }

    [Fact]
    public void TheRefusedShotIsNotChargedToTheUnit()
    {
        // Refused before the step, so the rifles are still loaded afterwards. Every other refusal on
        // the way to the step is something the player could have known; this one is a gap in what
        // they typed, and taking the volley for it would punish them for this app's policy.
        var game = GameFixtures.Firefight();

        var refused = game.Fire(
            GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Blank, GameFixtures.HitsARifleman());
        Assert.False(refused.IsAllowed);

        // The control that must be accepted: the same game, the same volley, the table entered.
        var fired = game.Fire(
            GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman());
        Assert.True(fired.IsAllowed, fired.Reason);
        Assert.Equal(7, fired.Value!.Status(GameFixtures.Bravo).FiguresAlive);
    }

    [Fact]
    public void AShotPastEffectiveRangeIsTakenAndAchievesNothing()
    {
        // The other kind of "no effective shot", and the reason the two are told apart. The rules
        // let a unit waste a volley on a target too far away; the step is spent and nothing lands.
        var game = GameFixtures.Firefight();
        var dice = new ScriptedDice(GameFixtures.AKillAndAStop);

        var after = game.Fire(
            GameFixtures.Volley() with { DistanceInches = 40 }, dice, TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.True(after.IsAllowed, after.Reason);
        Assert.Equal(8, after.Value!.Status(GameFixtures.Bravo).FiguresAlive);
        Assert.Contains(after.Value.Session.CurrentFrame!.Steps, step => step.Kind.Contains("Fire", StringComparison.Ordinal));
        Assert.Equal(GameFixtures.AKillAndAStop.Length, dice.Remaining);
    }
}
