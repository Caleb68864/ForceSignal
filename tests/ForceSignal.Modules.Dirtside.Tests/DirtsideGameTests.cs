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
        // By the name on the model, not the id. Ids are whatever the client generated, and the web
        // client generates eight random characters - so a refusal that listed them was telling a
        // player to go and find "ze09ww91". Found by driving the screen in a browser.
        Assert.Contains("Alpha Two", refused.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha-2", refused.Reason!, StringComparison.Ordinal);
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
            .Fire(Shot("Hull Gun"), new ScriptedDice(8, 1), Pot(), TestDieTables.Invented);

        Assert.False(afterMoving.IsAllowed);
        Assert.Contains("never after", afterMoving.Reason!, StringComparison.OrdinalIgnoreCase);

        Assert.True(table.Fire(Shot("Hull Gun"), new ScriptedDice(8, 1), Pot(), TestDieTables.Invented).IsAllowed);
    }

    [Fact]
    public void ADeclaredPostureReachesTheDiceAndTheLogSaysSo()
    {
        // The mechanic that was inert. `PostureDie` had been wired into `Fire` since Dirtside had a
        // screen, and nothing in production ever moved a target off None - there was no field on any
        // contract to carry one - so every target in every game was in the open and the whole
        // die-shift rule did nothing.
        //
        // Scripted: the target throws its signature die first, then its posture die, then the
        // barrel. Signature 3 is a D6 in the invented tables and hull down is a D8, so a posture
        // throw of 7 is a number the signature die could not have produced - it is the second die
        // or it is nothing.
        var afterPosture = GameFixtures.Activated()
            .Fire(
                Shot(posture: DefensivePosture.HullDown),
                new ScriptedDice(2, 7, 6),
                Pot(),
                TestDieTables.Invented).Value!;

        Assert.Contains(afterPosture.Log, entry => entry.Contains("best of two", StringComparison.Ordinal));
        Assert.Contains(afterPosture.Log, entry => entry.Contains("against 7", StringComparison.Ordinal));
    }

    [Fact]
    public void ATargetInTheOpenStillThrowsOneDie()
    {
        // The other half. A shot that declares nothing is the shot this game has always taken, and
        // it must not have quietly acquired a second die.
        var after = GameFixtures.Activated()
            .Fire(Shot(), new ScriptedDice(2, 6), Pot(), TestDieTables.Invented).Value!;

        Assert.DoesNotContain(after.Log, entry => entry.Contains("best of two", StringComparison.Ordinal));
    }

    [Fact]
    public void APostureTheProfileHasNoRowForRefusesTheShotWithoutSpendingIt()
    {
        // Posture rows are die table rows like any other, so a posture nobody has entered a die for
        // is refused by name and costs the element nothing - the same treatment as a missing
        // gunnery row, because it is the same kind of gap.
        var table = GameFixtures.Activated();
        var noTurretDown = TestDieTables.Invented with
        {
            PostureDice = TestDieTables.Invented.PostureDice.Remove(DefensivePosture.TurretDown),
        };

        var refused = table.Fire(
            Shot(posture: DefensivePosture.TurretDown), new ScriptedDice(2, 7, 6), Pot(), noTurretDown);

        Assert.False(refused.IsAllowed);
        Assert.Contains("TurretDown", refused.Reason!, StringComparison.Ordinal);
        Assert.True(table.Fire(Shot(), new ScriptedDice(2, 6), Pot(), noTurretDown).IsAllowed);
    }

    [Fact]
    public void AShotThatLandsHardPutsTheTargetOutOfTheBattle()
    {
        // The target rolls first and the barrel second, so this is a 1 against an 8: a solid hit.
        // Three chits of eight against three points of armour is far past what the vehicle can take.
        var after = GameFixtures.Activated()
            .Fire(Shot(), new ScriptedDice(1, 8), Pot(DamageChit.Numerical(ChitColour.Red, 8)), TestDieTables.Invented).Value!;

        var target = after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne);
        Assert.True(target.IsDestroyed);
        Assert.Contains(after.Log, entry => entry.Contains("knocked out", StringComparison.Ordinal));
    }

    [Fact]
    public void AShotThatMissesChangesNothingButIsStillSpent()
    {
        // An 8 for the target against a 1 for the barrel: nothing lands.
        var after = GameFixtures.Activated()
            .Fire(Shot(), new ScriptedDice(8, 1), Pot(DamageChit.Numerical(ChitColour.Red, 8)), TestDieTables.Invented).Value!;

        Assert.False(after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne).IsDestroyed);
        Assert.Contains(after.Log, entry => entry.Contains("Nothing landed", StringComparison.Ordinal));
        // The combat action is gone all the same.
        Assert.False(after.Fire(Shot(), new ScriptedDice(1, 8), Pot(), TestDieTables.Invented).IsAllowed);
    }

    [Fact]
    public void ADeclarationIsBindingAndCannotBeRePointedAtSomethingAlive()
    {
        // The cost of information the player did not have when they declared. An engine that let the
        // second shot look around for a fresh target would be playing a more forgiving game.
        var afterFirst = GameFixtures.Activated()
            .Fire(Shot(), new ScriptedDice(1, 8), Pot(DamageChit.Numerical(ChitColour.Red, 8)), TestDieTables.Invented).Value!;

        var second = afterFirst.Fire(
            Shot(element: GameFixtures.AlphaTwo),
            new ScriptedDice(1, 8),
            Pot(DamageChit.Numerical(ChitColour.Red, 8)), TestDieTables.Invented);

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
            Pot(), TestDieTables.Invented);

        Assert.False(refused.IsAllowed);
        Assert.Contains("own side", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AWeaponTheElementIsNotCarryingIsRefused()
    {
        var refused = GameFixtures.Activated().Fire(Shot("Railgun"), new ScriptedDice(8, 1), Pot(), TestDieTables.Invented);

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

        Assert.Equal(game.WhyFireIsRefused(shot, TestDieTables.Invented), game.Fire(shot, new ScriptedDice(8, 1), Pot(), TestDieTables.Invented).Reason);
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
    public void FiringFirstAndDeclaringAMoveOverHalfIsPenalisedLikeMovingFirst()
    {
        // The resolver's contract is "has moved, or will move". Fire-then-move used to escape the
        // penalty because the flag was only written by the move. Declared at the shot, the element
        // is marked as having moved over half before a die is thrown, which is what the resolver
        // reads to drop the firer's die a step.
        var shot = Shot() with { WillMoveOverHalf = true };
        var after = GameFixtures.Activated().Fire(shot, new ScriptedDice(8, 1), Pot(), TestDieTables.Invented).Value!;

        Assert.True(after.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).MovedOverHalf);
        Assert.Contains(after.Log, entry => entry.Contains("on the move", StringComparison.Ordinal));
        // And the move it promised is the move it takes.
        Assert.True(after.MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: true).IsAllowed);
    }

    [Fact]
    public void AnElementThatFiredWithoutDeclaringMayNotThenMoveOverHalf()
    {
        var after = GameFixtures.Activated().Fire(Shot(), new ScriptedDice(8, 1), Pot(), TestDieTables.Invented).Value!;

        var refused = after.MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: true);

        Assert.False(refused.IsAllowed);
        Assert.Contains("without declaring", refused.Reason!, StringComparison.Ordinal);
        Assert.Equal(refused.Reason, after.WhyMoveIsRefused(GameFixtures.AlphaOne, overHalfItsMovement: true));
        // A short move is still its own to make.
        Assert.True(after.MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: false).IsAllowed);
    }

    [Fact]
    public void AnImmobilisedElementIsWrittenDownAndWillNotMoveButMayStillFire()
    {
        // The mobility chit used to be said in the log and written nowhere, so the vehicle drove
        // off next activation. Now it is on the element, and the move is refused by name.
        var hit = GameFixtures.Activated()
            .Fire(Shot(), new ScriptedDice(1, 8), Pot(DamageChit.Of(ChitSpecial.Mobility)), TestDieTables.Invented).Value!;
        Assert.True(hit.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne).IsImmobilised);

        var red = hit.StandDown(GameFixtures.AlphaTwo).Value!
            .EndActivation().Value!
            .BeginActivation(GameFixtures.Red, GameFixtures.Bravo).Value!;

        var refused = red.MoveElement(GameFixtures.BravoOne, overHalfItsMovement: false);
        Assert.False(refused.IsAllowed);
        Assert.Contains("immobilised", refused.Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.True(red.Fire(
            new FireCommand(GameFixtures.Bravo, GameFixtures.BravoOne, "Main Gun", GameFixtures.Alpha, GameFixtures.AlphaOne, WeaponRangeBand.Close),
            new ScriptedDice(8, 1),
            Pot(), TestDieTables.Invented).IsAllowed);
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
        Assert.False(game.Fire(Shot(), new ScriptedDice(8, 1), Pot(), TestDieTables.Invented).IsAllowed);
    }

    [Fact]
    public void SensorsThatWereNeverSwitchedOnCannotIntercept()
    {
        // The point of the combat action. Until the interception rule read this flag, an element
        // that had never spent the action could intercept exactly as freely as one that had, which
        // made the action a purchase of nothing - the worst kind, because the table believes it
        // bought something.
        var game = GameFixtures.Activated();

        var refusal = game.WhyInterceptionIsRefused(GameFixtures.Alpha, GameFixtures.AlphaOne);
        Assert.NotNull(refusal);
        Assert.Contains("area-defence sensors", refusal, StringComparison.OrdinalIgnoreCase);
        Assert.False(game.InterceptWithAreaDefence(GameFixtures.Alpha, GameFixtures.AlphaOne).IsAllowed);

        var live = game.SetAreaDefenceSensors(GameFixtures.AlphaOne, live: true).Value!;
        Assert.Null(live.WhyInterceptionIsRefused(GameFixtures.Alpha, GameFixtures.AlphaOne));
    }

    [Fact]
    public void AnElementWhoseSystemsAreDownCannotInterceptHoweverLiveItsSensorsWere()
    {
        // The sensors were paid for and are still on; the vehicle is simply not answering. Checked
        // because the two flags are set by different rules and could easily contradict each other.
        var live = GameFixtures.Activated().SetAreaDefenceSensors(GameFixtures.AlphaOne, live: true).Value!;
        var down = live.WithStatus(
            GameFixtures.Alpha,
            status => status.WithElement(GameFixtures.AlphaOne, element => element with { IsSystemsDown = true }));

        var refusal = down.WhyInterceptionIsRefused(GameFixtures.Alpha, GameFixtures.AlphaOne);
        Assert.NotNull(refusal);
        Assert.Contains("systems down", refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AGunThatFailsPutsTheFirersOwnSystemsDownAndLeavesTheTargetAlone()
    {
        // The chit that says the firer's own systems failed replaces the result rather than adding to
        // it: the shot is treated as never fired, so nothing reaches the target. Both halves matter,
        // and the second half is the one that was wrong - the log said the gun had failed while the
        // roster showed the vehicle in perfect order, because the whole entry was skipped before the
        // firer was considered. Found by playing a turn through the API rather than by reading it.
        var after = GameFixtures.Activated()
            .Fire(Shot(), new ScriptedDice(1, 8), Pot(DamageChit.Of(ChitSpecial.SystemsDownFirer)), TestDieTables.Invented).Value!;

        Assert.True(after.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).IsSystemsDown);
        Assert.False(after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne).IsDestroyed);
        Assert.False(after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne).IsDamaged);
        Assert.Contains(after.Log, entry => entry.Contains("the gun failed", StringComparison.Ordinal));
    }

    [Fact]
    public void AnElementWithItsSystemsDownCannotFireAgain()
    {
        var after = GameFixtures.Activated()
            .Fire(Shot(), new ScriptedDice(1, 8), Pot(DamageChit.Of(ChitSpecial.SystemsDownFirer)), TestDieTables.Invented).Value!
            .EndActivation();

        // It cannot fire again this activation anyway - it has spent its combat action - so the
        // check that matters is the one the refusal gives.
        Assert.Contains(
            "systems down",
            GameFixtures.Activated()
                .WithStatus(GameFixtures.Alpha, status => status.WithElement(
                    GameFixtures.AlphaOne, element => element with { IsSystemsDown = true }))
                .WhyFireIsRefused(Shot(), TestDieTables.Invented)!,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(after.IsAllowed);
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

    private static FireCommand Shot(
        string weapon = "Main Gun",
        ElementId? element = null,
        DefensivePosture posture = DefensivePosture.None) =>
        new(
            GameFixtures.Alpha,
            element ?? GameFixtures.AlphaOne,
            weapon,
            GameFixtures.Bravo,
            GameFixtures.BravoOne,
            WeaponRangeBand.Close,
            posture);

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
