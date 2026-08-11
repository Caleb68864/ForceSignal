using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Assault;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers close assault played through the game rather than through the engine.
/// </summary>
public sealed class GameAssaultTests
{
    [Fact]
    public void AChargeAtNoThreatGoesInOnAnyDecentRoll()
    {
        // Threat zero, so any roll above leadership 2 carries it in.
        var after = Activated().DeclareCloseAssault(GameFixtures.Alpha, GameFixtures.Bravo, threatLevel: 0, new ScriptedDice(6));

        Assert.True(after.IsAllowed);
        Assert.Contains(after.Value!.Log, entry => entry.Contains("went in on", StringComparison.Ordinal));
    }

    [Fact]
    public void ASquadThatHasLostItsNerveWillNotCharge()
    {
        var game = Activated().WithStatus(GameFixtures.Alpha, status => status with { Confidence = ConfidenceLevel.Broken });

        var refused = game.DeclareCloseAssault(GameFixtures.Alpha, GameFixtures.Bravo, threatLevel: 0, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
        Assert.Contains("will not charge", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailingTheChargeLosesTheActionRatherThanTheNerve()
    {
        // A charge is a reaction test, so refusing it costs the action and leaves confidence alone.
        // Shaken going in, shaken coming out, and one action gone.
        // The threat is the player's; what is tested here is what a failed test does.
        var shaken = Activated()
            .WithStatus(GameFixtures.Alpha, status => status with { Confidence = ConfidenceLevel.Shaken });

        var after = shaken.DeclareCloseAssault(GameFixtures.Alpha, GameFixtures.Bravo, threatLevel: 3, new ScriptedDice(1)).Value!;

        Assert.Equal(
            shaken.Status(GameFixtures.Alpha).Confidence,
            after.Status(GameFixtures.Alpha).Confidence);
        Assert.Single(after.Session.CurrentFrame!.Steps);
    }

    [Fact]
    public void AChargeSpendsTheWholeActivation()
    {
        // The rules give the whole activation to a close assault even when the move to contact needs
        // only one action. Found by playing one in a browser and noticing the squad still had an
        // action in hand afterwards.
        var after = Activated().DeclareCloseAssault(GameFixtures.Alpha, GameFixtures.Bravo, threatLevel: 0, new ScriptedDice(6)).Value!;

        var refused = after.TakeStep(StarGruntSteps.Simple(StarGruntAction.Observe));

        Assert.False(refused.IsAllowed);
        Assert.Contains("both its actions", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AUnitCannotDoSomethingElseFirstAndThenCharge()
    {
        // Costing the whole activation is also what forbids this, without a rule of its own.
        var game = Activated().TakeStep(StarGruntSteps.Simple(StarGruntAction.Observe)).Value!;

        var refused = game.DeclareCloseAssault(GameFixtures.Alpha, GameFixtures.Bravo, threatLevel: 0, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void AUnitCannotChargeItsOwnSide()
    {
        var game = Activated().WithUnit(GameFixtures.Squad(new UnitId("charlie"), "Charlie Squad", GameFixtures.Blue));

        var refused = game.DeclareCloseAssault(GameFixtures.Alpha, new UnitId("charlie"), threatLevel: 0, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
        Assert.Contains("own side", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADefenderThatStandsKeepsItsNerve()
    {
        // Even odds is threat 1, so the score to beat is 3 and a 6 holds.
        var after = Activated().DefenderStands(GameFixtures.Alpha, GameFixtures.Bravo, terror: false, new ScriptedDice(6)).Value!;

        Assert.Equal(ConfidenceLevel.Confident, after.Status(GameFixtures.Bravo).Confidence);
        Assert.Contains(after.Log, entry => entry.Contains("and stood", StringComparison.Ordinal));
    }

    [Fact]
    public void ADefenderThatGivesWayLosesConfidence()
    {
        var after = Activated().DefenderStands(GameFixtures.Alpha, GameFixtures.Bravo, terror: false, new ScriptedDice(1)).Value!;

        Assert.True(after.Status(GameFixtures.Bravo).Confidence < ConfidenceLevel.Confident);
    }

    [Fact]
    public void TerrorIsRecordedInTheReasonTheDefenderHadToTest()
    {
        var after = Activated().DefenderStands(GameFixtures.Alpha, GameFixtures.Bravo, terror: true, new ScriptedDice(6)).Value!;

        Assert.Contains(after.Log, entry => entry.Contains("doubled for terror", StringComparison.Ordinal));
    }

    [Fact]
    public void ABrokenDefenderRoutsWithoutBeingAsked()
    {
        var game = Activated().WithStatus(GameFixtures.Bravo, status => status with { Confidence = ConfidenceLevel.Broken });

        var after = game.DefenderStands(GameFixtures.Alpha, GameFixtures.Bravo, terror: false, new ScriptedDice(6)).Value!;

        Assert.Equal(ConfidenceLevel.Routed, after.Status(GameFixtures.Bravo).Confidence);
    }

    [Fact]
    public void AMeleeRoundTakesFiguresOffBothSides()
    {
        // Two pairings: the attacker wins the first and loses the second.
        var after = Activated().FightMeleeRound(
            GameFixtures.Alpha,
            GameFixtures.Bravo,
            [new MeleePairing(), new MeleePairing()],
            defendersInCover: false,
            new ScriptedDice(6, 2, 2, 6)).Value!;

        Assert.Equal(7, after.Status(GameFixtures.Alpha).FiguresAlive);
        Assert.Equal(7, after.Status(GameFixtures.Bravo).FiguresAlive);
    }

    [Fact]
    public void ATiedPairCostsNeitherSideAnybody()
    {
        var after = Activated().FightMeleeRound(
            GameFixtures.Alpha,
            GameFixtures.Bravo,
            [new MeleePairing()],
            defendersInCover: false,
            new ScriptedDice(5, 5)).Value!;

        Assert.Equal(8, after.Status(GameFixtures.Alpha).FiguresAlive);
        Assert.Equal(8, after.Status(GameFixtures.Bravo).FiguresAlive);
    }

    [Fact]
    public void ARoundWithNobodyPairedOffIsRefused()
    {
        var refused = Activated().FightMeleeRound(
            GameFixtures.Alpha, GameFixtures.Bravo, [], defendersInCover: false, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void TheDiceEachManThrewAreRecorded()
    {
        var after = Activated().FightMeleeRound(
            GameFixtures.Alpha,
            GameFixtures.Bravo,
            [new MeleePairing(AttackerShift: 2)],
            defendersInCover: false,
            new ScriptedDice(9, 2)).Value!;

        Assert.Contains(after.Log, entry => entry.Contains("D12 9 against D8 2", StringComparison.Ordinal));
    }

    [Fact]
    public void AStunnedManGetsUpAgainIfHisSideHeldTheGround()
    {
        var game = Activated().WithStatus(GameFixtures.Alpha, status => status with { FiguresAlive = 6 });

        var after = game.SettleTheDowned(GameFixtures.Alpha, downed: 2, wonTheAssault: true, deadUpTo: 2, woundedUpTo: 4, new ScriptedDice(5, 3)).Value!;

        // One stunned man back on his feet, one wounded who is a casualty rather than a rifle.
        Assert.Equal(7, after.Status(GameFixtures.Alpha).FiguresAlive);
        Assert.Equal(1, after.Status(GameFixtures.Alpha).FiguresWounded);
    }

    [Fact]
    public void AStunnedManOnTheLosingSideIsLostWithTheRest()
    {
        var game = Activated().WithStatus(GameFixtures.Alpha, status => status with { FiguresAlive = 6 });

        var after = game.SettleTheDowned(GameFixtures.Alpha, downed: 1, wonTheAssault: false, deadUpTo: 2, woundedUpTo: 4, new ScriptedDice(5)).Value!;

        Assert.Equal(6, after.Status(GameFixtures.Alpha).FiguresAlive);
        Assert.Contains(after.Log, entry => entry.Contains("left to the victors", StringComparison.Ordinal));
    }

    [Fact]
    public void SettlingNobodyIsRefused()
    {
        var refused = Activated().SettleTheDowned(GameFixtures.Alpha, downed: 0, wonTheAssault: true, deadUpTo: 2, woundedUpTo: 4, new ScriptedDice(5));

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void AThreatLevelBelowNothingIsRefused()
    {
        var refused = Activated().DeclareCloseAssault(
            GameFixtures.Alpha, GameFixtures.Bravo, threatLevel: -1, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void BandsThatDoNotClimbAreRefused()
    {
        // Wounded has to sit above dead, or the roll reads as nonsense.
        var refused = Activated().SettleTheDowned(
            GameFixtures.Alpha, downed: 1, wonTheAssault: true, deadUpTo: 4, woundedUpTo: 2, new ScriptedDice(3));

        Assert.False(refused.IsAllowed);
        Assert.Contains("climb", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    private static StarGruntGame Activated() =>
        GameFixtures.TwoSquadGame()
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;
}
