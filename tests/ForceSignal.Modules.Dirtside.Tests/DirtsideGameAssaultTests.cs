using System.Collections.Immutable;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Game;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// Close assault played through the game rather than through the resolvers. The resolvers are the
/// spec and have their own tests; what is covered here is that the game sequences them in the order
/// the rules give, spends what each step costs, and comes back from a save at the same point.
/// </summary>
public sealed class DirtsideGameAssaultTests
{
    private static readonly ChitValidity AllChits = new(ChitColours.All);

    [Fact]
    public void AWholeAssaultFromLaunchToAftermath()
    {
        // Launch: 6 against leadership 2 plus threat 1 goes in. Stand: the defender rolls 6 against
        // 2 plus 2 and holds. Round: every chit is an 8 against a kill threshold of 4, so every draw
        // on both sides removes a stand - two a side committed, two a side lost, both sides fight to
        // the last because the dead still draw. Aftermath: both sides have nobody left, which the
        // game settles without a die.
        var launched = Activated().LaunchAssault(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, GameFixtures.AlphaTwo, threat: 1), new ScriptedDice(6));
        Assert.True(launched.IsAllowed, launched.Reason);
        Assert.Equal(AssaultStage.AwaitingDefender, launched.Value!.Assault!.Stage);

        // The defender's marker turned on the spot, so Red cannot activate it later this turn.
        Assert.Contains(GameFixtures.Bravo, launched.Value.Session.Side(GameFixtures.Red).Activated);

        var stood = launched.Value.DefenderStands(Commit(GameFixtures.BravoOne, GameFixtures.BravoTwo, threat: 2), new ScriptedDice(6));
        Assert.True(stood.IsAllowed, stood.Reason);
        Assert.Equal(AssaultStage.AwaitingRound, stood.Value!.Assault!.Stage);

        var fought = stood.Value.FightAssaultRound(new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 8)));
        Assert.True(fought.IsAllowed, fought.Reason);
        Assert.Equal(AssaultStage.AwaitingAftermath, fought.Value!.Assault!.Stage);
        Assert.True(fought.Value.Status(GameFixtures.Bravo).IsWipedOut);
        Assert.True(fought.Value.Status(GameFixtures.Alpha).IsWipedOut);

        var settled = fought.Value.ResolveAssaultAftermath(1, 3, new ScriptedDice());
        Assert.True(settled.IsAllowed, settled.Reason);
        Assert.Equal(AssaultStage.AwaitingFollowThrough, settled.Value!.Assault!.Stage);
        Assert.Contains(settled.Value.Log, entry => entry.Contains("nobody left on the position", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDefenderTestsFirstAndFallsBackUnderFire()
    {
        // A round in which nobody dies: chits of 1 never reach a threshold of 4. Then the defender
        // rolls 1 and breaks; the attacker is never asked, and the script has nothing left to give.
        var settled = Fought(DamageChit.Numerical(ChitColour.Red, 1))
            .ResolveAssaultAftermath(1, 3, new ScriptedDice(1)).Value!;

        Assert.Equal(AssaultStage.AwaitingFollowThrough, settled.Assault!.Stage);
        Assert.True(settled.Status(GameFixtures.Bravo).IsUnderFire);
        Assert.False(settled.Status(GameFixtures.Alpha).IsUnderFire);
        Assert.True(settled.Status(GameFixtures.Bravo).Confidence < ConfidenceLevel.Confident);
        Assert.Equal(ConfidenceLevel.Confident, settled.Status(GameFixtures.Alpha).Confidence);
    }

    [Fact]
    public void AnAttackerWhoBreaksAfterTheDefenderHeldFallsBackAndTheAssaultIsOver()
    {
        var settled = Fought(DamageChit.Numerical(ChitColour.Red, 1))
            .ResolveAssaultAftermath(1, 3, new ScriptedDice(6, 1)).Value!;

        Assert.Null(settled.Assault);
        Assert.True(settled.Status(GameFixtures.Alpha).IsUnderFire);
        Assert.False(settled.Status(GameFixtures.Bravo).IsUnderFire);
        Assert.Contains(settled.Log, entry => entry.Contains("holds the position", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMarkerAnAssaultLeftComesOffAtTheEndOfThatUnitsOwnActivation()
    {
        // Alpha's assault is thrown back, so Alpha falls back under fire - during Alpha's own
        // activation, which is the activation the marker lapses at the end of.
        var settled = Fought(DamageChit.Numerical(ChitColour.Red, 1))
            .ResolveAssaultAftermath(1, 3, new ScriptedDice(6, 1)).Value!;
        Assert.True(settled.Status(GameFixtures.Alpha).IsUnderFire);

        var closed = settled.EndActivation();
        Assert.True(closed.IsAllowed, closed.Reason);

        // The marker used to go on here and come off nowhere at all: nothing in the game cleared it,
        // and EndTurn cannot, because the clearing is tied to the unit's own activation rather than
        // to the turn. A platoon that lost an assault therefore owed a reaction test before every
        // move it made for the rest of the game, which is a trapdoor rather than a marker.
        Assert.False(closed.Value!.Status(GameFixtures.Alpha).IsUnderFire);
        Assert.Contains(
            closed.Value.Log,
            entry => entry.Contains("no longer under fire", StringComparison.Ordinal));
    }

    [Fact]
    public void AMarkerPutOnTheDefenderOutlivesTheAttackersActivation()
    {
        // The other half of the same rule, and the reason it is not simply cleared at the end of the
        // frame for everyone. Bravo broke and fell back under fire during Alpha's activation; Bravo
        // has not activated, so it still carries the marker and still owes a test when it does.
        var settled = Fought(DamageChit.Numerical(ChitColour.Red, 1))
            .ResolveAssaultAftermath(1, 3, new ScriptedDice(1)).Value!;
        Assert.True(settled.Status(GameFixtures.Bravo).IsUnderFire);

        var closed = settled.EndActivation();
        Assert.True(closed.IsAllowed, closed.Reason);

        Assert.True(closed.Value!.Status(GameFixtures.Bravo).IsUnderFire);
    }

    [Fact]
    public void TwoSidesThatBothHoldGoAgainHandToHand()
    {
        var again = Fought(DamageChit.Numerical(ChitColour.Red, 1))
            .ResolveAssaultAftermath(1, 3, new ScriptedDice(6, 6)).Value!;

        Assert.Equal(AssaultStage.AwaitingRound, again.Assault!.Stage);
        Assert.Equal(2, again.Assault.Round);

        // The second round no longer counts the defender's cover, and says so.
        var second = again.FightAssaultRound(new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 1))).Value!;
        Assert.Contains(second.Log, entry => entry.Contains("Round 2", StringComparison.Ordinal) && entry.Contains("hand to hand", StringComparison.Ordinal));
    }

    [Fact]
    public void ACutUpSideTestsAtTheHeavyThreat()
    {
        // The attacker loses one of two stands - half, which is heavy - and the defender none. Against
        // leadership 2 the defender's score is 3 (light) and the attacker's 5 (heavy): a 4 holds
        // for the defender and breaks the attacker.
        var round = Fought(
            DamageChit.Numerical(ChitColour.Red, 0),
            DamageChit.Numerical(ChitColour.Red, 0),
            DamageChit.Numerical(ChitColour.Red, 0),
            DamageChit.Numerical(ChitColour.Red, 0),
            DamageChit.Numerical(ChitColour.Red, 0),
            DamageChit.Numerical(ChitColour.Red, 0),
            DamageChit.Numerical(ChitColour.Red, 8),
            DamageChit.Numerical(ChitColour.Red, 8),
            DamageChit.Numerical(ChitColour.Red, 8),
            DamageChit.Numerical(ChitColour.Red, 0),
            DamageChit.Numerical(ChitColour.Red, 0),
            DamageChit.Numerical(ChitColour.Red, 0));

        Assert.Equal(new AssaultRoundLosses(2, 1, 2, 0), round.Assault!.LastRound);

        var settled = round.ResolveAssaultAftermath(lightCasualtyThreat: 1, heavyCasualtyThreat: 3, new ScriptedDice(4, 4)).Value!;

        Assert.Null(settled.Assault);
        Assert.True(settled.Status(GameFixtures.Alpha).IsUnderFire);
    }

    [Fact]
    public void TroopsThatWillNotGoHaveSpentTheirCombatActionAndKeptTheirNerve()
    {
        // A reaction test: a 1 against 3 fails, no assault opens, the confidence marker stays put,
        // and both committed elements have taken their combat action for the activation.
        var refused = Activated().LaunchAssault(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, GameFixtures.AlphaTwo, threat: 1), new ScriptedDice(1)).Value!;

        Assert.Null(refused.Assault);
        Assert.Equal(ConfidenceLevel.Confident, refused.Status(GameFixtures.Alpha).Confidence);
        Assert.DoesNotContain(GameFixtures.Bravo, refused.Session.Side(GameFixtures.Red).Activated);
        Assert.False(refused.Fire(
            new FireCommand(GameFixtures.Alpha, GameFixtures.AlphaOne, "Main Gun", GameFixtures.Bravo, GameFixtures.BravoOne, WeaponRangeBand.Close),
            new ScriptedDice(1, 8),
            new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 1))).IsAllowed);
    }

    [Fact]
    public void APlatoonWhoseNerveHasGoneIsRefusedOutrightWithoutADie()
    {
        var broken = Activated().WithStatus(GameFixtures.Alpha, status => status with { Confidence = ConfidenceLevel.Broken });
        var dice = new ScriptedDice(8);

        var refused = broken.LaunchAssault(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, threat: 0), dice);

        Assert.False(refused.IsAllowed);
        Assert.Contains("will not close-assault", refused.Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, dice.Remaining);
    }

    [Fact]
    public void ADefenderWhoseNerveHadAlreadyGoneBreaksWithoutATestAndThePositionFalls()
    {
        // Shaken armour routs the moment infantry climbs onto the hull. No die is thrown.
        var launched = Activated()
            .WithStatus(GameFixtures.Bravo, status => status with { Confidence = ConfidenceLevel.Shaken })
            .LaunchAssault(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, threat: 0), new ScriptedDice(6)).Value!;
        var dice = new ScriptedDice(8);

        var gone = launched.DefenderStands(Commit(GameFixtures.BravoOne, threat: 0), dice).Value!;

        Assert.Equal(ConfidenceLevel.Routed, gone.Status(GameFixtures.Bravo).Confidence);
        Assert.Equal(AssaultStage.AwaitingFollowThrough, gone.Assault!.Stage);
        Assert.Equal(1, dice.Remaining);
        Assert.Contains(gone.Log, entry => entry.Contains("without a test", StringComparison.Ordinal));
    }

    [Fact]
    public void ADefenderThatGivesWayLosesMoraleAsWellAsThePosition()
    {
        var launched = Activated().LaunchAssault(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, threat: 0), new ScriptedDice(6)).Value!;

        var gone = launched.DefenderStands(Commit(GameFixtures.BravoOne, threat: 2), new ScriptedDice(1)).Value!;

        Assert.True(gone.Status(GameFixtures.Bravo).Confidence < ConfidenceLevel.Confident);
        Assert.Equal(AssaultStage.AwaitingFollowThrough, gone.Assault!.Stage);
    }

    [Fact]
    public void FollowingThroughHandsEveryElementItsMoveAndItsCombatActionAgain()
    {
        var taken = Fought(DamageChit.Numerical(ChitColour.Red, 1))
            .ResolveAssaultAftermath(1, 3, new ScriptedDice(1)).Value!;

        var through = taken.FollowThrough(threatLevel: 1, new ScriptedDice(6)).Value!;

        Assert.Null(through.Assault);
        Assert.Empty(through.Session.CurrentFrame!.Steps);
        Assert.True(through.MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: false).IsAllowed);
        Assert.Equal(GameFixtures.Alpha, through.Session.CurrentFrame.Unit);
    }

    [Fact]
    public void FailingToFollowThroughCostsNothingButTheOpportunity()
    {
        var taken = Fought(DamageChit.Numerical(ChitColour.Red, 1))
            .ResolveAssaultAftermath(1, 3, new ScriptedDice(1)).Value!;

        var stopped = taken.FollowThrough(threatLevel: 1, new ScriptedDice(1)).Value!;

        Assert.Null(stopped.Assault);
        Assert.NotEmpty(stopped.Session.CurrentFrame!.Steps);
        Assert.Equal(taken.Status(GameFixtures.Alpha).Confidence, stopped.Status(GameFixtures.Alpha).Confidence);
    }

    [Fact]
    public void AnActivationCannotCloseInTheMiddleOfAnAssaultButMayWalkAwayFromAFollowThrough()
    {
        var launched = Activated().LaunchAssault(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, GameFixtures.AlphaTwo, threat: 0), new ScriptedDice(6)).Value!;

        var refused = launched.EndActivation();
        Assert.False(refused.IsAllowed);
        Assert.Contains("Fight it out first", refused.Reason!, StringComparison.Ordinal);

        var taken = Fought(DamageChit.Numerical(ChitColour.Red, 1))
            .ResolveAssaultAftermath(1, 3, new ScriptedDice(1)).Value!;
        var closed = taken.EndActivation();

        Assert.True(closed.IsAllowed, closed.Reason);
        Assert.Null(closed.Value!.Assault);
        Assert.Contains(closed.Value.Log, entry => entry.Contains("consolidated", StringComparison.Ordinal));
    }

    [Fact]
    public void EachStepIsRefusedUnlessItIsTheOneOwed()
    {
        var game = Activated();
        Assert.Contains("No assault", game.WhyRoundIsRefused()!, StringComparison.Ordinal);

        var launched = game.LaunchAssault(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, threat: 0), new ScriptedDice(6)).Value!;
        Assert.False(launched.FightAssaultRound(new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 1))).IsAllowed);
        Assert.False(launched.ResolveAssaultAftermath(1, 3, new ScriptedDice(6)).IsAllowed);
        Assert.False(launched.FollowThrough(0, new ScriptedDice(6)).IsAllowed);
        Assert.Contains("already fighting", launched.WhyLaunchIsRefused(GameFixtures.Bravo, Commit(GameFixtures.AlphaTwo, threat: 0))!, StringComparison.Ordinal);
    }

    [Fact]
    public void APlatoonWhoseCardDoesNotSayWhatItRollsCannotBeTested()
    {
        // The game ships no die. A platoon added without one is refused, by name, rather than rolled
        // on something this app chose.
        var game = DirtsideGame.Create("Table")
            .WithUnit(GameFixtures.Platoon(GameFixtures.Alpha, "Alpha Troop", GameFixtures.Blue, Stand(GameFixtures.AlphaOne, "Alpha One"), Stand(GameFixtures.AlphaTwo, "Alpha Two")))
            .WithUnit(GameFixtures.Platoon(GameFixtures.Bravo, "Bravo Troop", GameFixtures.Red, Stand(GameFixtures.BravoOne, "Bravo One"), Stand(GameFixtures.BravoTwo, "Bravo Two")))
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;

        var refused = game.WhyLaunchIsRefused(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, threat: 0));

        Assert.Contains("Alpha Troop's record card does not say what die it rolls", refused!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnElementWhoseCardDoesNotSayItsAssaultNumbersCannotBeCommitted()
    {
        var refused = Activated(withStandNumbers: false)
            .WhyLaunchIsRefused(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, threat: 0));

        Assert.Contains("does not say how many chits it draws", refused!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReasonALaunchIsRefusedIsTheSameWordsTheCommandWouldUse()
    {
        var game = Activated().WithStatus(GameFixtures.Alpha, status => status with { Confidence = ConfidenceLevel.Routed });
        var commitment = Commit(GameFixtures.AlphaOne, threat: 0);

        Assert.Equal(
            game.WhyLaunchIsRefused(GameFixtures.Bravo, commitment),
            game.LaunchAssault(GameFixtures.Bravo, commitment, new ScriptedDice(8)).Reason);
    }

    [Fact]
    public void AnAssaultSurvivesARestartAtEveryStage()
    {
        var launched = Activated().LaunchAssault(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, GameFixtures.AlphaTwo, threat: 1), new ScriptedDice(6)).Value!;
        AssertRoundTrips(launched);

        var stood = launched.DefenderStands(Commit(GameFixtures.BravoOne, GameFixtures.BravoTwo, threat: 2), new ScriptedDice(6)).Value!;
        AssertRoundTrips(stood);

        var fought = stood.FightAssaultRound(new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 1))).Value!;
        AssertRoundTrips(fought);

        // And the game picked back up mid-assault goes on from where it was put down.
        var restored = DirtsideGameSerialization.Restore(DirtsideGameSerialization.Save(fought));
        var settled = restored.ResolveAssaultAftermath(1, 3, new ScriptedDice(6, 6));
        Assert.True(settled.IsAllowed, settled.Reason);
        Assert.Equal(2, settled.Value!.Assault!.Round);
    }

    [Fact]
    public void ACybertankNeitherLaunchesNorReceivesAnAssault()
    {
        var game = DirtsideGame.Create("Table")
            .WithUnit(GameFixtures.Platoon(GameFixtures.Alpha, "Alpha Troop", GameFixtures.Blue, Stand(GameFixtures.AlphaOne, "Alpha One"), Stand(GameFixtures.AlphaTwo, "Alpha Two")) with { Quality = QualityDie.D8, LeadershipValue = 2 })
            .WithUnit(GameFixtures.Platoon(GameFixtures.Bravo, "Bravo Troop", GameFixtures.Red, Stand(GameFixtures.BravoOne, "Bravo One"), Stand(GameFixtures.BravoTwo, "Bravo Two")) with { IsCybertank = true })
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;

        Assert.Contains("cybertank", game.WhyLaunchIsRefused(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, threat: 0))!, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertRoundTrips(DirtsideGame game)
    {
        var restored = DirtsideGameSerialization.Restore(DirtsideGameSerialization.Save(game));
        Assert.Equal(game, restored);
        Assert.Equal(game.Assault, restored.Assault);
    }

    /// <summary>Alpha's assault on Bravo with one round fought, both sides fully committed, and the aftermath owed.</summary>
    private static DirtsideGame Fought(params DamageChit[] chits) =>
        Activated()
            .LaunchAssault(GameFixtures.Bravo, Commit(GameFixtures.AlphaOne, GameFixtures.AlphaTwo, threat: 0), new ScriptedDice(6)).Value!
            .DefenderStands(Commit(GameFixtures.BravoOne, GameFixtures.BravoTwo, threat: 0), new ScriptedDice(6)).Value!
            .FightAssaultRound(new ScriptedChitPot(chits)).Value!;

    private static AssaultCommitment Commit(ElementId only, int threat) => Commit([only], threat);

    private static AssaultCommitment Commit(ElementId first, ElementId second, int threat) => Commit([first, second], threat);

    private static AssaultCommitment Commit(ImmutableArray<ElementId> stands, int threat) =>
        new(stands, AllChits, HandToHandValidity: null, threat);

    /// <summary>An element with the two numbers an assault reads off its card.</summary>
    private static ElementDefinition Stand(ElementId id, string name, bool withStandNumbers = true) =>
        GameFixtures.Vehicle(id, name) with
        {
            AssaultChits = withStandNumbers ? 3 : null,
            KillThreshold = withStandNumbers ? 4 : null,
        };

    /// <summary>
    /// Two platoons of two stands, each with a die and a leadership on its command marker, Alpha
    /// activated and facing Bravo. Every number is invented.
    /// </summary>
    private static DirtsideGame Activated(bool withStandNumbers = true) =>
        DirtsideGame.Create("Table")
            .WithUnit(GameFixtures.Platoon(
                GameFixtures.Alpha, "Alpha Troop", GameFixtures.Blue,
                Stand(GameFixtures.AlphaOne, "Alpha One", withStandNumbers),
                Stand(GameFixtures.AlphaTwo, "Alpha Two", withStandNumbers)) with { Quality = QualityDie.D8, LeadershipValue = 2 })
            .WithUnit(GameFixtures.Platoon(
                GameFixtures.Bravo, "Bravo Troop", GameFixtures.Red,
                Stand(GameFixtures.BravoOne, "Bravo One", withStandNumbers),
                Stand(GameFixtures.BravoTwo, "Bravo Two", withStandNumbers)) with { Quality = QualityDie.D8, LeadershipValue = 2 })
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;
}
