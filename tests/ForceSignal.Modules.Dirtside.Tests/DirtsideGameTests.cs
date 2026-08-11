using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Game;
using ForceSignal.Modules.Dirtside.Sequence;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// Dirtside played through the game aggregate rather than through the engine directly - which until
/// now nothing did. The engine was complete and unreachable; these cover the layer that reaches it.
/// </summary>
public sealed class DirtsideGameTests
{
    [Fact]
    public void ATurnOpensAndASideTakesTheFirstActivation()
    {
        var game = GameFixtures.TwoPlatoonGame().BeginTurn();

        Assert.True(game.IsAllowed);
        Assert.True(game.Value!.ChooseFirstActivator(GameFixtures.Blue, takeIt: true).IsAllowed);
    }

    [Fact]
    public void APlatoonWithNothingLeftCannotBeActivated()
    {
        // The shared layer has no view on this - a wiped-out platoon is still a face-up marker as far
        // as the sequence is concerned - so the game has to refuse it. Letting it through would open
        // a frame that could never close, because a frame closes when every element has chosen and
        // there would be none.
        var wiped = GameFixtures.TwoPlatoonGame()
            .WithStatus(GameFixtures.Alpha, status => status
                .WithElement(GameFixtures.AlphaOne, element => element with { IsDestroyed = true })
                .WithElement(GameFixtures.AlphaTwo, element => element with { IsDestroyed = true }))
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!;

        var refused = wiped.BeginActivation(GameFixtures.Blue, GameFixtures.Alpha);

        Assert.False(refused.IsAllowed);
        Assert.Contains("nothing left to activate", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnActivationWillNotCloseWhileAnElementHasNotChosen()
    {
        // The rule this protects: an element that sits out its platoon's activation has given up its
        // go for the whole turn. Closing without asking would silently make that choice for it.
        var game = GameFixtures.Activated()
            .MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: false).Value!;

        var refused = game.EndActivation();

        Assert.False(refused.IsAllowed);
        Assert.Contains("alpha-2", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnActivationClosesOnceEveryElementHasSaidWhatItIsDoing()
    {
        var game = GameFixtures.Activated()
            .MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: false).Value!
            .StandDown(GameFixtures.AlphaTwo).Value!;

        Assert.True(game.EndActivation().IsAllowed);
    }

    [Fact]
    public void AnElementThatStoodDownIsOutForTheTurn()
    {
        var game = GameFixtures.Activated().StandDown(GameFixtures.AlphaOne).Value!;

        var refused = game.MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: false);

        Assert.False(refused.IsAllowed);
        Assert.Contains("out for the turn", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EachElementGetsOneMoveAndOneCombatAction()
    {
        // There is no budget on the platoon. Each element independently gets one of each, which is
        // what makes this game's activation a different shape rather than the other game's in
        // different words.
        var game = GameFixtures.Activated()
            .MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: false).Value!;

        Assert.False(game.MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: false).IsAllowed);
        // Its sibling has done nothing yet and is unaffected.
        Assert.True(game.MoveElement(GameFixtures.AlphaTwo, overHalfItsMovement: false).IsAllowed);
    }

    [Fact]
    public void AFixedMountMayFireBeforeMovingButNotAfter()
    {
        // The one rule in the game that reads the order the steps were taken in rather than just
        // which of them were: a weapon aimed by pointing the vehicle can be laid before it sets off.
        var table = WithHullGun();
        var afterMoving = table
            .MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: false).Value!
            .Fire(Shot("Hull Gun"), new ScriptedDice(8, 1), Pot());

        Assert.False(afterMoving.IsAllowed);
        Assert.Contains("never after", afterMoving.Reason!, StringComparison.OrdinalIgnoreCase);

        Assert.True(table.Fire(Shot("Hull Gun"), new ScriptedDice(8, 1), Pot()).IsAllowed);
    }

    [Fact]
    public void AShotThatLandsHardPutsTheTargetOutOfTheBattle()
    {
        // The target rolls first and the barrel second, so this is a 1 against an 8: a solid hit.
        // Three chits of eight against three points of armour is far past what the vehicle can take.
        var after = GameFixtures.Activated()
            .Fire(Shot(), new ScriptedDice(1, 8), Pot(DamageChit.Numerical(ChitColour.Red, 8))).Value!;

        var target = after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne);
        Assert.True(target.IsDestroyed);
        Assert.Contains(after.Log, entry => entry.Contains("knocked out", StringComparison.Ordinal));
    }

    [Fact]
    public void AShotThatMissesChangesNothingButIsStillSpent()
    {
        // An 8 for the target against a 1 for the barrel: nothing lands.
        var after = GameFixtures.Activated()
            .Fire(Shot(), new ScriptedDice(8, 1), Pot(DamageChit.Numerical(ChitColour.Red, 8))).Value!;

        Assert.False(after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne).IsDestroyed);
        Assert.Contains(after.Log, entry => entry.Contains("Nothing landed", StringComparison.Ordinal));
        // The combat action is gone all the same.
        Assert.False(after.Fire(Shot(), new ScriptedDice(1, 8), Pot()).IsAllowed);
    }

    [Fact]
    public void ADeclarationIsBindingAndCannotBeRePointedAtSomethingAlive()
    {
        // The cost of information the player did not have when they declared. An engine that let the
        // second shot look around for a fresh target would be playing a more forgiving game.
        var afterFirst = GameFixtures.Activated()
            .Fire(Shot(), new ScriptedDice(1, 8), Pot(DamageChit.Numerical(ChitColour.Red, 8))).Value!;

        var second = afterFirst.Fire(
            Shot(element: GameFixtures.AlphaTwo),
            new ScriptedDice(1, 8),
            Pot(DamageChit.Numerical(ChitColour.Red, 8)));

        Assert.False(second.IsAllowed);
        Assert.Contains("already destroyed", second.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnElementCannotFireOnItsOwnSide()
    {
        var refused = GameFixtures.Activated().Fire(
            new FireCommand(
                GameFixtures.Alpha, GameFixtures.AlphaOne, "Main Gun",
                GameFixtures.Alpha, GameFixtures.AlphaTwo, WeaponRangeBand.Close),
            new ScriptedDice(8, 1),
            Pot());

        Assert.False(refused.IsAllowed);
        Assert.Contains("own side", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AWeaponTheElementIsNotCarryingIsRefused()
    {
        var refused = GameFixtures.Activated().Fire(Shot("Railgun"), new ScriptedDice(8, 1), Pot());

        Assert.False(refused.IsAllowed);
        Assert.Contains("not carrying", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheReasonAShotIsRefusedIsTheSameWordsTheButtonWouldShow()
    {
        // The projection and the command read the same check, so a disabled button can never explain
        // itself differently from the refusal a player would get by clicking anyway.
        var game = GameFixtures.Activated();
        var shot = Shot("Railgun");

        Assert.Equal(game.WhyFireIsRefused(shot), game.Fire(shot, new ScriptedDice(8, 1), Pot()).Reason);
    }

    [Fact]
    public void MovingOverHalfItsMovementIsRecordedBecauseItSpoilsTheShot()
    {
        var game = GameFixtures.Activated()
            .MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: true).Value!;

        Assert.True(game.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).MovedOverHalf);
        Assert.Contains(game.Log, entry => entry.Contains("over half its movement", StringComparison.Ordinal));
    }

    [Fact]
    public void EndingTheTurnClearsWhatOnlyLastedTheTurn()
    {
        // A whole turn, both sides, so the assertion about what carries over is made against a turn
        // that actually finished rather than one forced shut.
        var blue = GameFixtures.Activated()
            .MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: true).Value!
            .StandDown(GameFixtures.AlphaTwo).Value!;
        var blueDone = blue.EndActivation();
        Assert.True(blueDone.IsAllowed, blueDone.Reason);

        var red = blueDone.Value!.BeginActivation(GameFixtures.Red, GameFixtures.Bravo);
        Assert.True(red.IsAllowed, red.Reason);
        var redDone = red.Value!
            .StandDown(GameFixtures.BravoOne).Value!
            .StandDown(GameFixtures.BravoTwo).Value!
            .EndActivation();
        Assert.True(redDone.IsAllowed, redDone.Reason);

        var next = redDone.Value!.EndTurn();

        Assert.True(next.IsAllowed, next.Reason);
        // Moving over half its movement spoils this turn's shooting, not next turn's.
        Assert.False(next.Value!.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).MovedOverHalf);
    }

    [Fact]
    public void SwitchingTheSensorsOnSpendsTheCombatActionItIsWorth()
    {
        // Live sensors intercept on anybody's activation for the rest of the turn, including one
        // where this element has long since gone. The combat action is the price of that.
        var game = GameFixtures.Activated()
            .SetAreaDefenceSensors(GameFixtures.AlphaOne, live: true).Value!;

        Assert.True(game.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).AreaDefenceSensorsLive);
        Assert.False(game.Fire(Shot(), new ScriptedDice(8, 1), Pot()).IsAllowed);
    }

    [Fact]
    public void TwoGamesBuiltTheSameWayAreEqual()
    {
        // The dictionaries underneath compare by reference, so this is a real assertion rather than a
        // tautology - and the same mistake has bitten this codebase before.
        Assert.Equal(GameFixtures.TwoPlatoonGame(), GameFixtures.TwoPlatoonGame());
        Assert.Equal(GameFixtures.TwoPlatoonGame().GetHashCode(), GameFixtures.TwoPlatoonGame().GetHashCode());
    }

    [Fact]
    public void AGameThatHasHadSomethingHappenToItIsNotEqualToOneThatHasNot()
    {
        Assert.NotEqual(GameFixtures.TwoPlatoonGame(), GameFixtures.Activated());
    }

    private static FireCommand Shot(string weapon = "Main Gun", ElementId? element = null) =>
        new(
            GameFixtures.Alpha,
            element ?? GameFixtures.AlphaOne,
            weapon,
            GameFixtures.Bravo,
            GameFixtures.BravoOne,
            WeaponRangeBand.Close);

    private static ScriptedChitPot Pot(params DamageChit[] chits) =>
        new(chits.Length == 0 ? [DamageChit.Numerical(ChitColour.Red, 1)] : chits);

    /// <summary>The same table, but Alpha One carries a fixed mount instead.</summary>
    private static DirtsideGame WithHullGun() =>
        DirtsideGame.Create("Table")
            .WithUnit(GameFixtures.Platoon(
                GameFixtures.Alpha, "Alpha Troop", GameFixtures.Blue,
                GameFixtures.Vehicle(GameFixtures.AlphaOne, "Alpha One", weapons: GameFixtures.HullGun),
                GameFixtures.Vehicle(GameFixtures.AlphaTwo, "Alpha Two")))
            .WithUnit(GameFixtures.Platoon(
                GameFixtures.Bravo, "Bravo Troop", GameFixtures.Red,
                GameFixtures.Vehicle(GameFixtures.BravoOne, "Bravo One"),
                GameFixtures.Vehicle(GameFixtures.BravoTwo, "Bravo Two")))
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;
}
