using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Game;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// Systems-down recovery as an activation step. The roll is the resolver's; what is covered here is
/// that the game knows which activation the marker went on, refuses the attempt on that one, spends
/// the combat action on every later one, and takes the marker off when the crew get there.
/// </summary>
public sealed class DirtsideGameRecoveryTests
{
    [Fact]
    public void RepairsCannotStartOnTheActivationTheDamageHappened()
    {
        var down = SystemsDownByMisfire();
        var dice = new ScriptedDice(6);

        var refused = down.RecoverSystems(GameFixtures.AlphaOne, dice);

        Assert.False(refused.IsAllowed);
        Assert.Contains("an activation after", refused.Reason!, StringComparison.Ordinal);
        Assert.Equal(1, dice.Remaining);
    }

    [Fact]
    public void OnALaterActivationTheCrewMayTryAndAFailureLeavesTheMarkerOn()
    {
        var next = NextActivation(SystemsDownByMisfire());

        var failed = next.RecoverSystems(GameFixtures.AlphaOne, new ScriptedDice(5)).Value!;

        Assert.True(failed.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).IsSystemsDown);
        Assert.Contains(failed.Log, entry => entry.Contains("Still down", StringComparison.Ordinal));

        // The combat action is gone; the marker is not. The vehicle may try again next activation.
        Assert.False(failed.RecoverSystems(GameFixtures.AlphaOne, new ScriptedDice(6)).IsAllowed);
    }

    [Fact]
    public void ReachingTheNumberTakesTheMarkerOff()
    {
        var next = NextActivation(SystemsDownByMisfire());

        var cleared = next.RecoverSystems(GameFixtures.AlphaOne, new ScriptedDice(6)).Value!;

        var element = cleared.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne);
        Assert.False(element.IsSystemsDown);
        Assert.Null(element.SystemsDownOnActivation);
        Assert.Contains(cleared.Log, entry => entry.Contains("Systems back up", StringComparison.Ordinal));
    }

    [Fact]
    public void BackupSystemsLowerTheNumber()
    {
        var withBackup = SystemsDownByMisfire(hasBackupSystems: true);

        var cleared = NextActivation(withBackup).RecoverSystems(GameFixtures.AlphaOne, new ScriptedDice(3)).Value!;

        Assert.False(cleared.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).IsSystemsDown);
    }

    [Fact]
    public void AVehicleWhoseSystemsAreUpHasNothingToRecover()
    {
        var refused = GameFixtures.Activated().RecoverSystems(GameFixtures.AlphaOne, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
        Assert.Contains("not down", refused.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheActivationTheMarkerWentOnSurvivesASave()
    {
        var down = SystemsDownByMisfire();

        var restored = DirtsideGameSerialization.Restore(DirtsideGameSerialization.Save(down));

        Assert.Equal(
            down.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).SystemsDownOnActivation,
            restored.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).SystemsDownOnActivation);
        Assert.NotNull(restored.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).SystemsDownOnActivation);
        Assert.Equal(down, restored);
    }

    [Fact]
    public void TheReasonARecoveryIsRefusedIsTheSameWordsTheCommandWouldUse()
    {
        var down = SystemsDownByMisfire();

        Assert.Equal(
            down.WhyRecoverSystemsIsRefused(GameFixtures.AlphaOne),
            down.RecoverSystems(GameFixtures.AlphaOne, new ScriptedDice(6)).Reason);
    }

    /// <summary>Alpha One's gun fails on Alpha's first activation, putting its own systems down.</summary>
    private static DirtsideGame SystemsDownByMisfire(bool hasBackupSystems = false)
    {
        var table = DirtsideGame.Create("Table")
            .WithUnit(GameFixtures.Platoon(
                GameFixtures.Alpha, "Alpha Troop", GameFixtures.Blue,
                GameFixtures.Vehicle(GameFixtures.AlphaOne, "Alpha One") with { HasBackupSystems = hasBackupSystems },
                GameFixtures.Vehicle(GameFixtures.AlphaTwo, "Alpha Two")))
            .WithUnit(GameFixtures.Platoon(
                GameFixtures.Bravo, "Bravo Troop", GameFixtures.Red,
                GameFixtures.Vehicle(GameFixtures.BravoOne, "Bravo One"),
                GameFixtures.Vehicle(GameFixtures.BravoTwo, "Bravo Two")))
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;

        return table.Fire(
            new FireCommand(GameFixtures.Alpha, GameFixtures.AlphaOne, "Main Gun", GameFixtures.Bravo, GameFixtures.BravoOne, WeaponRangeBand.Close),
            new ScriptedDice(1, 8),
            new ScriptedChitPot(DamageChit.Of(ChitSpecial.SystemsDownFirer))).Value!;
    }

    /// <summary>Plays the turn out and opens Alpha's activation in the next one.</summary>
    private static DirtsideGame NextActivation(DirtsideGame game) =>
        game.StandDown(GameFixtures.AlphaTwo).Value!
            .EndActivation().Value!
            .BeginActivation(GameFixtures.Red, GameFixtures.Bravo).Value!
            .StandDown(GameFixtures.BravoOne).Value!
            .StandDown(GameFixtures.BravoTwo).Value!
            .EndActivation().Value!
            .EndTurn().Value!
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;
}
