using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Game;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.Dirtside.Sequence;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// Gaps 4, 5 and 6: the morale resolvers were built, tested and called by nothing outside an assault.
/// The arithmetic is the resolvers' and is covered beside them; what is covered here is that the game
/// reaches them, writes down what they say, and that the gates that read those flags now close.
/// Every die, leadership value and threat level is invented.
/// </summary>
public sealed class DirtsideGameMoraleTests
{
    private const int Passes = 8;
    private const int Fails = 1;

    private static readonly FireCommand AtBravo = new(
        GameFixtures.Alpha, GameFixtures.AlphaOne, "Main Gun",
        GameFixtures.Bravo, GameFixtures.BravoOne, WeaponRangeBand.Close);

    private static readonly DamageChit Killing = DamageChit.Numerical(ChitColour.Red, 8);

    private static ConfidenceLevel WantsATestToAdvance =>
        Enum.GetValues<ConfidenceLevel>().First(level =>
            DirtsideConfidence.NeedsReactionTestToAdvance(level, DirtsideUnitKind.Armour));

    [Fact]
    public void AFailedConfidenceTestMovesTheMarkerAndAPassLeavesIt()
    {
        var game = Table();

        var held = game.TakeConfidenceTest(GameFixtures.Alpha, 1, new ScriptedDice(Passes)).Value!;
        Assert.Equal(ConfidenceLevel.Confident, held.Status(GameFixtures.Alpha).Confidence);
        Assert.Contains(held.Log, entry => entry.Contains("tested its nerve", StringComparison.Ordinal));

        var dropped = game.TakeConfidenceTest(GameFixtures.Alpha, 1, new ScriptedDice(Fails)).Value!;
        Assert.NotEqual(ConfidenceLevel.Confident, dropped.Status(GameFixtures.Alpha).Confidence);
    }

    [Fact]
    public void AConfidenceTestIsNotAnActionAndNeedsNoActivationOfItsOwn()
    {
        var game = Begin(Table(), GameFixtures.Alpha);

        var tested = game.TakeConfidenceTest(GameFixtures.Bravo, 1, new ScriptedDice(Passes));

        Assert.True(tested.IsAllowed);
        Assert.True(tested.Value!.Session.CurrentFrame!.Steps.IsDefaultOrEmpty);
    }

    [Fact]
    public void AConfidenceTestIsRefusedByNameWhereThereIsNothingToTest()
    {
        var dice = new ScriptedDice(Passes);

        var noCard = Table(alpha: platoon => platoon)
            .TakeConfidenceTest(GameFixtures.Alpha, 1, dice);
        Assert.Contains("nerve cannot be tested", noCard.Reason!, StringComparison.Ordinal);

        var cybertank = Table(alpha: platoon => Nerve(platoon) with { IsCybertank = true })
            .TakeConfidenceTest(GameFixtures.Alpha, 1, dice);
        Assert.Contains("cybertank", cybertank.Reason!, StringComparison.Ordinal);

        Assert.False(Table().TakeConfidenceTest(GameFixtures.Alpha, -1, dice).IsAllowed);
        Assert.False(Table().TakeConfidenceTest(new UnitId("nobody"), 1, dice).IsAllowed);

        // None of those threw the die.
        Assert.Equal(1, dice.Remaining);
    }

    [Fact]
    public void AVehicleIsMarkedOnlyWhenSomethingLands()
    {
        var missed = Begin(Table(), GameFixtures.Alpha).Fire(
            AtBravo, new ScriptedDice(8, 1), new ScriptedChitPot(), TestDieTables.Invented).Value!;
        Assert.False(missed.Status(GameFixtures.Bravo).IsUnderFire);

        var hit = Begin(Table(), GameFixtures.Alpha).Fire(
            AtBravo, new ScriptedDice(1, 8), new ScriptedChitPot(Killing), TestDieTables.Invented).Value!;
        Assert.True(hit.Status(GameFixtures.Bravo).IsUnderFire);
    }

    [Fact]
    public void FootAreMarkedJustByBeingFiredOn()
    {
        var foot = Table(bravo: platoon => Nerve(platoon) with { Kind = DirtsideUnitKind.DismountedInfantry });

        var missed = Begin(foot, GameFixtures.Alpha).Fire(
            AtBravo, new ScriptedDice(8, 1), new ScriptedChitPot(), TestDieTables.Invented).Value!;

        Assert.True(missed.Status(GameFixtures.Bravo).IsUnderFire);
    }

    [Fact]
    public void ACybertankCarriesNoMarker()
    {
        var cybertank = Table(bravo: platoon => Nerve(platoon) with { IsCybertank = true });

        var hit = Begin(cybertank, GameFixtures.Alpha).Fire(
            AtBravo, new ScriptedDice(1, 8), new ScriptedChitPot(Killing), TestDieTables.Invented).Value!;

        Assert.False(hit.Status(GameFixtures.Bravo).IsUnderFire);
    }

    [Fact]
    public void AMarkedUnitOwesATestToMoveAndAPassLetsEveryElementGo()
    {
        var game = Begin(Marked(), GameFixtures.Bravo);

        Assert.False(game.MoveElement(GameFixtures.BravoOne, false).IsAllowed);

        var cleared = game.TakeReactionTest(GameFixtures.Bravo, 1, new ScriptedDice(Passes)).Value!;

        Assert.True(cleared.Status(GameFixtures.Bravo).ReactionTestCleared);
        Assert.True(cleared.MoveElement(GameFixtures.BravoOne, false).IsAllowed);
        Assert.True(cleared.MoveElement(GameFixtures.BravoTwo, false).IsAllowed);
    }

    [Fact]
    public void AFailedReactionTestCostsTheMoveAndNeverALevelAndCannotBeRetakenThatActivation()
    {
        var game = Begin(Marked(), GameFixtures.Bravo);
        var before = game.Status(GameFixtures.Bravo).Confidence;

        var failed = game.TakeReactionTest(GameFixtures.Bravo, 1, new ScriptedDice(Fails)).Value!;

        Assert.Equal(before, failed.Status(GameFixtures.Bravo).Confidence);
        Assert.False(failed.MoveElement(GameFixtures.BravoOne, false).IsAllowed);

        var again = failed.TakeReactionTest(GameFixtures.Bravo, 1, new ScriptedDice(Passes));
        Assert.Contains("already refused", again.Reason!, StringComparison.Ordinal);

        // And the element is left to do something else with the activation.
        Assert.True(failed.StandDown(GameFixtures.BravoOne).IsAllowed);
    }

    [Fact]
    public void AReactionTestOwedByNobodyIsRefusedWithoutARoll()
    {
        var dice = new ScriptedDice(Passes);

        var refused = Begin(Table(), GameFixtures.Alpha).TakeReactionTest(GameFixtures.Alpha, 1, dice);

        Assert.False(refused.IsAllowed);
        Assert.Equal(1, dice.Remaining);
    }

    [Fact]
    public void AReactionTestBelongsToThePlatoonActivating()
    {
        var dice = new ScriptedDice(Passes);

        var refused = Begin(Marked(), GameFixtures.Alpha).TakeReactionTest(GameFixtures.Bravo, 1, dice);

        Assert.False(refused.IsAllowed);
        Assert.Equal(1, dice.Remaining);
    }

    [Fact]
    public void TheMarkerComesOffAtTheEndOfItsOwnActivationWithTheFailureAndTheDeclaration()
    {
        var failed = Begin(Marked(), GameFixtures.Bravo)
            .SetNextMoveAdvances(GameFixtures.Bravo, true).Value!
            .TakeReactionTest(GameFixtures.Bravo, 1, new ScriptedDice(Fails)).Value!
            .StandDown(GameFixtures.BravoOne).Value!
            .StandDown(GameFixtures.BravoTwo).Value!;

        var closed = failed.EndActivation().Value!.Status(GameFixtures.Bravo);

        Assert.False(closed.IsUnderFire);
        Assert.False(closed.ReactionTestFailed);
        Assert.False(closed.NextMoveAdvancesOnTheEnemy);
    }

    [Fact]
    public void ADeclaredAdvanceReachesTheConfidenceGate()
    {
        var wavering = Table().WithStatus(GameFixtures.Alpha, status => status with { Confidence = WantsATestToAdvance });
        var declared = Begin(wavering, GameFixtures.Alpha).SetNextMoveAdvances(GameFixtures.Alpha, true).Value!;
        var withdrawn = declared.SetNextMoveAdvances(GameFixtures.Alpha, false).Value!;

        Assert.NotNull(declared.WhyMoveIsRefused(GameFixtures.AlphaOne, false));
        Assert.Null(withdrawn.WhyMoveIsRefused(GameFixtures.AlphaOne, false));
    }

    [Fact]
    public void ChangingTheDeclarationGivesBackAnyNerveAlreadyFound()
    {
        // The same shape StarGrunt has: a passed test cannot be banked against a different order.
        var wavering = Table().WithStatus(GameFixtures.Alpha, status => status with { Confidence = WantsATestToAdvance });
        var cleared = Begin(wavering, GameFixtures.Alpha)
            .SetNextMoveAdvances(GameFixtures.Alpha, true).Value!
            .TakeReactionTest(GameFixtures.Alpha, 1, new ScriptedDice(Passes)).Value!;

        Assert.True(cleared.Status(GameFixtures.Alpha).ReactionTestCleared);
        Assert.False(cleared.SetNextMoveAdvances(GameFixtures.Alpha, false).Value!.Status(GameFixtures.Alpha).ReactionTestCleared);
    }

    [Fact]
    public void ADeclaredBreakUpReachesTheIntegrityGate()
    {
        var disorganised = Begin(Table(), GameFixtures.Alpha).SetDisorganised(GameFixtures.Alpha, true).Value!;

        Assert.True(disorganised.Status(GameFixtures.Alpha).IsDisorganised);
        Assert.False(disorganised.Fire(
            AtBravo, new ScriptedDice(1, 8), new ScriptedChitPot(Killing), TestDieTables.Invented).IsAllowed);
    }

    [Fact]
    public void WhatATestLeftSurvivesASave()
    {
        var failed = Begin(Marked(), GameFixtures.Bravo)
            .TakeReactionTest(GameFixtures.Bravo, 1, new ScriptedDice(Fails)).Value!;

        var restored = DirtsideGameSerialization.Restore(DirtsideGameSerialization.Save(failed));

        Assert.True(restored.Status(GameFixtures.Bravo).IsUnderFire);
        Assert.True(restored.Status(GameFixtures.Bravo).ReactionTestFailed);
    }

    // Gap 10, the firefight. Leadership 2 on the invented marker: below it nobody fires, below double
    // it half the stands do, and from double it they all do.
    private static PlatoonDefinition Foot(PlatoonDefinition platoon) =>
        Nerve(platoon) with
        {
            Kind = DirtsideUnitKind.DismountedInfantry,
            Elements = [.. platoon.Elements.Select(stand => stand with { KillThreshold = 5 })],
        };

    private static FirefightCommand Firefight(params ElementId[] stands) => new(
        GameFixtures.Alpha, "Main Gun", [.. stands], GameFixtures.Bravo, GameFixtures.BravoOne, WeaponRangeBand.Close);

    private static DirtsideGame FootActivated() => Begin(Table(alpha: Foot, bravo: Foot), GameFixtures.Alpha);

    [Fact]
    public void AFullyEffectiveUnitDrawsPerStandAndRemovesOnePerWinningDraw()
    {
        var after = FootActivated().Firefight(
            Firefight(GameFixtures.AlphaOne, GameFixtures.AlphaTwo),
            new ScriptedDice(4),
            new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 8)),
            TestDieTables.Invented).Value!;

        Assert.True(after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne).IsDestroyed);
        Assert.True(after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoTwo).IsDestroyed);
        Assert.Contains(after.Log, entry => entry.Contains("Bravo One, Bravo Two removed", StringComparison.Ordinal));
    }

    [Fact]
    public void ChitsAreNeverPooledAcrossStands()
    {
        // Three chits of one is three a stand and six between them; the threshold is five. Pooled, this
        // would kill. Drawn apart, it does nothing.
        var after = FootActivated().Firefight(
            Firefight(GameFixtures.AlphaOne, GameFixtures.AlphaTwo),
            new ScriptedDice(4),
            new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 1)),
            TestDieTables.Invented).Value!;

        Assert.False(after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne).IsDestroyed);
    }

    [Fact]
    public void APartialCheckFiresTheFirstStandsNamedAndSpendsThemAll()
    {
        var after = FootActivated().Firefight(
            Firefight(GameFixtures.AlphaTwo, GameFixtures.AlphaOne),
            new ScriptedDice(3),
            new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 8)),
            TestDieTables.Invented).Value!;

        Assert.True(after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne).IsDestroyed);
        Assert.False(after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoTwo).IsDestroyed);

        // The stand that did not get to draw tried anyway, so its combat action is gone.
        Assert.False(after.Firefight(
            Firefight(GameFixtures.AlphaOne), new ScriptedDice(4), new ScriptedChitPot(), TestDieTables.Invented).IsAllowed);
    }

    [Fact]
    public void AnIneffectiveFirefightStillMarksTheTarget()
    {
        var after = FootActivated().Firefight(
            Firefight(GameFixtures.AlphaOne), new ScriptedDice(1), new ScriptedChitPot(), TestDieTables.Invented).Value!;

        Assert.True(after.Status(GameFixtures.Bravo).IsUnderFire);
        Assert.False(after.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne).IsDestroyed);
    }

    [Fact]
    public void AMarkedUnitWithNoEntryForTheCostIsRefusedByNameBeforeAnythingIsSpent()
    {
        var marked = FootActivated().WithStatus(GameFixtures.Alpha, status => status with { IsUnderFire = true });
        var dice = new ScriptedDice(4);

        var refused = marked.Firefight(Firefight(GameFixtures.AlphaOne), dice, new ScriptedChitPot(), TestDieTables.Invented);

        Assert.Contains("rungs an Under Fire marker costs", refused.Reason!, StringComparison.Ordinal);
        Assert.Equal(1, dice.Remaining);
    }

    [Fact]
    public void TheCostOfAMarkerIsTheProfileEntry()
    {
        var marked = FootActivated().WithStatus(GameFixtures.Alpha, status => status with { IsUnderFire = true });
        var rolled = new List<QualityDie>();

        var after = marked.Firefight(
            Firefight(GameFixtures.AlphaOne),
            new RecordingDice(rolled),
            new ScriptedChitPot(),
            TestDieTables.Invented with { RungsLostUnderFire = 2 });

        Assert.True(after.IsAllowed);
        Assert.Equal(QualityDie.D4, Assert.Single(rolled));
    }

    [Fact]
    public void AFirefightIsRefusedWhereItMeansNothing()
    {
        var pot = new ScriptedChitPot();
        var dice = new ScriptedDice(4);

        var vehicles = Begin(Table(), GameFixtures.Alpha);
        Assert.Contains("not infantry", vehicles.Firefight(Firefight(GameFixtures.AlphaOne), dice, pot, TestDieTables.Invented).Reason!, StringComparison.Ordinal);

        var atVehicles = Begin(Table(alpha: Foot), GameFixtures.Alpha);
        Assert.Contains("not infantry", atVehicles.Firefight(Firefight(GameFixtures.AlphaOne), dice, pot, TestDieTables.Invented).Reason!, StringComparison.Ordinal);

        var noThreshold = Begin(
            Table(alpha: Foot, bravo: platoon => Nerve(platoon) with { Kind = DirtsideUnitKind.DismountedInfantry }),
            GameFixtures.Alpha);
        Assert.Contains("kill threshold", noThreshold.Firefight(Firefight(GameFixtures.AlphaOne), dice, pot, TestDieTables.Invented).Reason!, StringComparison.Ordinal);

        Assert.False(FootActivated().Firefight(Firefight(), dice, pot, TestDieTables.Invented).IsAllowed);
        Assert.False(FootActivated().Firefight(Firefight(GameFixtures.AlphaOne, GameFixtures.AlphaOne), dice, pot, TestDieTables.Invented).IsAllowed);

        Assert.Equal(1, dice.Remaining);
    }

    private sealed class RecordingDice(List<QualityDie> rolled) : IQualityDiceRoller
    {
        public int Roll(QualityDie die)
        {
            rolled.Add(die);
            return 1;
        }
    }

    // Gap 7. The window and its cost were built and the board could only ever say nobody was watching.
    [Fact]
    public void AMoveNobodySawOpensNothing()
    {
        var moved = Begin(Table(), GameFixtures.Alpha).MoveElement(GameFixtures.AlphaOne, false).Value!;

        Assert.Null(moved.Session.AwaitingAnswer);
    }

    [Fact]
    public void AMoveSeenOpensAWindowAndHoldsTheMoverUntilItIsAnswered()
    {
        var seen = Begin(Table(), GameFixtures.Alpha)
            .MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: true, [GameFixtures.Bravo]).Value!;

        Assert.Equal(GameFixtures.Bravo, Assert.Single(seen.Session.AwaitingAnswer!.EligibleResponders));
        Assert.False(seen.StandDown(GameFixtures.AlphaTwo).IsAllowed);

        // The move is declared before the shot it draws, so the mover's penalty is already on it.
        Assert.True(seen.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).MovedOverHalf);
    }

    [Fact]
    public void DecliningLetsTheMoverGoAndKeepsTheActivation()
    {
        var declined = Begin(Table(), GameFixtures.Alpha)
            .MoveElement(GameFixtures.AlphaOne, false, [GameFixtures.Bravo]).Value!
            .DeclineOpportunityFire(GameFixtures.Bravo).Value!;

        Assert.Null(declined.Session.AwaitingAnswer);
        Assert.Contains(GameFixtures.Bravo, declined.Session.Side(GameFixtures.Red).Unactivated);
        Assert.True(declined.StandDown(GameFixtures.AlphaTwo).IsAllowed);
    }

    [Fact]
    public void AnsweringFiresOnTheMoverSpendsTheActivationAndHandsPlayBack()
    {
        var reacting = Begin(Table(), GameFixtures.Alpha)
            .MoveElement(GameFixtures.AlphaOne, false, [GameFixtures.Bravo]).Value!
            .AnswerOpportunityFire(GameFixtures.Bravo).Value!;
        var atTheMover = new FireCommand(
            GameFixtures.Bravo, GameFixtures.BravoOne, "Main Gun",
            GameFixtures.Alpha, GameFixtures.AlphaOne, WeaponRangeBand.Close);

        Assert.False(reacting.MoveElement(GameFixtures.BravoOne, false).IsAllowed);

        var fired = reacting.Fire(atTheMover, new ScriptedDice(1, 8), new ScriptedChitPot(Killing), TestDieTables.Invented).Value!;
        Assert.True(fired.Status(GameFixtures.Alpha).Element(GameFixtures.AlphaOne).IsDestroyed);

        // One element having fired is enough: the rest of the platoon has chosen nothing.
        var back = fired.EndActivation().Value!;

        Assert.Equal(GameFixtures.Alpha, back.Session.CurrentFrame!.Unit);
        Assert.DoesNotContain(GameFixtures.Bravo, back.Session.Side(GameFixtures.Red).Unactivated);
        Assert.Equal(0, back.Session.Side(GameFixtures.Red).ForfeitedSlots);
    }

    [Fact]
    public void AResponderFiresOnTheMoverAndNothingElse()
    {
        var third = GameFixtures.Platoon(
            new UnitId("charlie"), "Charlie Troop", GameFixtures.Blue,
            GameFixtures.Vehicle(new ElementId("charlie-1"), "Charlie One"),
            GameFixtures.Vehicle(new ElementId("charlie-2"), "Charlie Two"));

        // Red has the fewer platoons now, so the choice of who goes first is Red's to give away.
        var reacting = Table().WithUnit(third)
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Red, takeIt: false).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!
            .MoveElement(GameFixtures.AlphaOne, false, [GameFixtures.Bravo]).Value!
            .AnswerOpportunityFire(GameFixtures.Bravo).Value!;
        var dice = new ScriptedDice(1, 8);

        var refused = reacting.Fire(
            new FireCommand(GameFixtures.Bravo, GameFixtures.BravoOne, "Main Gun", third.Id, new ElementId("charlie-1"), WeaponRangeBand.Close),
            dice, new ScriptedChitPot(Killing), TestDieTables.Invented);

        Assert.Contains("Alpha Troop", refused.Reason!, StringComparison.Ordinal);
        Assert.Equal(2, dice.Remaining);
    }

    [Fact]
    public void ABadWatcherIsRefusedByNameAndTheMoveIsNotTaken()
    {
        var game = Begin(Table(), GameFixtures.Alpha);

        Assert.Contains("own side", game.MoveElement(GameFixtures.AlphaOne, false, [GameFixtures.Alpha]).Reason!, StringComparison.Ordinal);
        Assert.Contains("nobody", game.MoveElement(GameFixtures.AlphaOne, false, [new UnitId("nobody")]).Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AWatcherThatHasAlreadyGoneOpensNothing()
    {
        var bravoGone = Begin(Table(), GameFixtures.Bravo)
            .StandDown(GameFixtures.BravoOne).Value!
            .StandDown(GameFixtures.BravoTwo).Value!
            .EndActivation().Value!;

        var moved = bravoGone.BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!
            .MoveElement(GameFixtures.AlphaOne, false, [GameFixtures.Bravo]).Value!;

        Assert.Null(moved.Session.AwaitingAnswer);
    }

    [Fact]
    public void NobodyCanAnswerAWindowThatIsNotOpen()
    {
        Assert.False(Begin(Table(), GameFixtures.Alpha).AnswerOpportunityFire(GameFixtures.Bravo).IsAllowed);
        Assert.False(Begin(Table(), GameFixtures.Alpha).DeclineOpportunityFire(GameFixtures.Bravo).IsAllowed);
    }

    [Fact]
    public void AnOpenWindowSurvivesASave()
    {
        var seen = Begin(Table(), GameFixtures.Alpha)
            .MoveElement(GameFixtures.AlphaOne, false, [GameFixtures.Bravo]).Value!;

        var restored = DirtsideGameSerialization.Restore(DirtsideGameSerialization.Save(seen));

        Assert.True(restored.AnswerOpportunityFire(GameFixtures.Bravo).IsAllowed);
    }

    /// <summary>Two platoons with a die and a leadership value on each marker, unless a test edits them.</summary>
    private static DirtsideGame Table(
        Func<PlatoonDefinition, PlatoonDefinition>? alpha = null,
        Func<PlatoonDefinition, PlatoonDefinition>? bravo = null) =>
        DirtsideGame.Create("Table")
            .WithUnit((alpha ?? Nerve)(GameFixtures.Platoon(
                GameFixtures.Alpha, "Alpha Troop", GameFixtures.Blue,
                GameFixtures.Vehicle(GameFixtures.AlphaOne, "Alpha One"),
                GameFixtures.Vehicle(GameFixtures.AlphaTwo, "Alpha Two"))))
            .WithUnit((bravo ?? Nerve)(GameFixtures.Platoon(
                GameFixtures.Bravo, "Bravo Troop", GameFixtures.Red,
                GameFixtures.Vehicle(GameFixtures.BravoOne, "Bravo One"),
                GameFixtures.Vehicle(GameFixtures.BravoTwo, "Bravo Two"))));

    private static PlatoonDefinition Nerve(PlatoonDefinition platoon) =>
        platoon with { Quality = QualityDie.D8, LeadershipValue = 2 };

    private static DirtsideGame Marked() =>
        Table().WithStatus(GameFixtures.Bravo, status => status with { IsUnderFire = true });

    private static DirtsideGame Begin(DirtsideGame game, UnitId unit)
    {
        var side = unit == GameFixtures.Alpha ? GameFixtures.Blue : GameFixtures.Red;
        return game.BeginTurn().Value!.ChooseFirstActivator(side, takeIt: true).Value!.BeginActivation(side, unit).Value!;
    }
}
