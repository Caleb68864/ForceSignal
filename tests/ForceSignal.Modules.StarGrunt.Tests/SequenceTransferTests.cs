using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Transferred activations, and the weapon limit they are the reason to get right.
/// </summary>
public class SequenceTransferTests
{
    private readonly StarGruntBoard board = SequenceFixtures.Board();
    private readonly StarGruntActivationPolicy policy;

    public SequenceTransferTests() => policy = new StarGruntActivationPolicy(board);

    [Fact]
    public void AWeaponFiresOnceInAnActivationAndAnotherWeaponIsUntouched()
    {
        var fired = GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), StarGruntSteps.Fire("saw"), policy);

        Assert.False(GroundCombatSequence.CanTakeStep(fired, StarGruntSteps.Fire("saw"), policy).IsAllowed);
        Assert.True(GroundCombatSequence.CanTakeStep(fired, StarGruntSteps.Fire("rifles"), policy).IsAllowed);
    }

    [Fact]
    public void AFireActionHasToNameItsWeapon()
    {
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);
        var anonymous = ActivationStep.Of(StarGruntSteps.Name(StarGruntAction.Fire));

        var check = GroundCombatSequence.CanTakeStep(opened, anonymous, policy);

        Assert.False(check.IsAllowed);
        Assert.Contains("name the weapon", check.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The errata, proved end to end: the limit is per activation, so a squad that already fired can
    /// fire the same weapon again on an activation its commander hands it - in the same game turn.
    /// </summary>
    [Fact]
    public void ATransferredActivationLetsTheSameWeaponFireAgainInTheSameTurn()
    {
        var squadDone = GroundCombatSequence.EndFrame(
            GroundCombatSequence.TakeStep(
                SequenceFixtures.Activating(SequenceFixtures.SquadA), StarGruntSteps.Fire("saw"), policy),
            policy);
        var backToBlue = GroundCombatSequence.EndFrame(
            GroundCombatSequence.BeginActivation(squadDone, SequenceFixtures.Red, SequenceFixtures.Watcher),
            policy);

        var commanding = GroundCombatSequence.BeginActivation(
            backToBlue, SequenceFixtures.Blue, SequenceFixtures.Commander);
        var sprung = StarGruntTurn.Transfer(
            commanding, policy, SequenceFixtures.SquadA, communicationSucceeded: true);

        Assert.Equal(1, sprung.TurnNumber);
        Assert.Equal(SequenceFixtures.SquadA, sprung.CurrentFrame!.Unit);
        Assert.Empty(sprung.CurrentFrame.ResourcesSpent);

        var firedAgain = GroundCombatSequence.TakeStep(sprung, StarGruntSteps.Fire("saw"), policy);

        Assert.Contains("weapon:saw", firedAgain.CurrentFrame!.ResourcesSpent);
    }

    /// <summary>
    /// It nests rather than queues - the subordinate acts at once, before the enemy gets a say, and
    /// the commander's other action is still waiting underneath when it is done.
    /// </summary>
    [Fact]
    public void ATransferNestsAndTheCommanderKeepsHisSecondAction()
    {
        var sprung = StarGruntTurn.Transfer(
            SequenceFixtures.Activating(SequenceFixtures.Commander),
            policy,
            SequenceFixtures.SquadA,
            communicationSucceeded: true);

        Assert.Equal(2, sprung.Depth);
        Assert.Equal(FrameKind.Granted, sprung.CurrentFrame!.Kind);
        Assert.Equal(SequenceFixtures.Blue, sprung.ActiveSide);

        var back = GroundCombatSequence.EndFrame(sprung, policy);

        Assert.Equal(SequenceFixtures.Commander, back.CurrentFrame!.Unit);
        Assert.Equal(1, StarGruntActivationPolicy.ActionsRemaining(back.CurrentFrame));
        Assert.Contains(SequenceFixtures.SquadA, back.Side(SequenceFixtures.Blue).Activated);
    }

    [Fact]
    public void ACommanderMaySpringTwoSubordinatesAndNoMore()
    {
        var first = GroundCombatSequence.EndFrame(
            StarGruntTurn.Transfer(
                SequenceFixtures.Activating(SequenceFixtures.Commander), policy, SequenceFixtures.SquadA, true),
            policy);
        var second = GroundCombatSequence.EndFrame(
            StarGruntTurn.Transfer(first, policy, SequenceFixtures.SquadB, true),
            policy);

        Assert.Equal(2, StarGruntActivationPolicy.TransfersMade(second.CurrentFrame!));

        // Two actions gone as well as two transfers, so either gate alone would have stopped a third.
        Assert.False(StarGruntTurn.CanTransfer(second, policy, SequenceFixtures.SquadA, true).IsAllowed);
    }

    [Fact]
    public void TheSameSubordinateCannotBeSprungTwice()
    {
        var once = GroundCombatSequence.EndFrame(
            StarGruntTurn.Transfer(
                SequenceFixtures.Activating(SequenceFixtures.Commander), policy, SequenceFixtures.SquadA, true),
            policy);

        var check = GroundCombatSequence.CanTakeStep(
            once, StarGruntSteps.Transfer(SequenceFixtures.SquadA), policy);

        Assert.False(check.IsAllowed);
    }

    /// <summary>
    /// The per-turn cap the session cannot answer on its own. A commander given a second activation
    /// gets a fresh frame, so only the board remembers what he did on the first.
    /// </summary>
    [Fact]
    public void ThePerTurnCapIsReadFromTheBoardRatherThanFromTheFrame()
    {
        board.Update(SequenceFixtures.Commander, state => state with { TransfersMadeThisTurn = 2 });

        var check = StarGruntTurn.CanTransfer(
            SequenceFixtures.Activating(SequenceFixtures.Commander), policy, SequenceFixtures.SquadA, true);

        Assert.False(check.IsAllowed);
        Assert.Contains("already sprung", check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivationsOnlyTravelDownTheChain()
    {
        var check = StarGruntTurn.CanTransfer(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), policy, SequenceFixtures.Commander, true);

        Assert.False(check.IsAllowed);
        Assert.Contains("down the chain", check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoSquadsCannotHandEachOtherActivationsBackAndForth()
    {
        var check = StarGruntTurn.CanTransfer(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), policy, SequenceFixtures.SquadB, true);

        Assert.False(check.IsAllowed);
    }

    [Fact]
    public void AMessageThatDoesNotGetThroughSpringsNobody()
    {
        var check = StarGruntTurn.CanTransfer(
            SequenceFixtures.Activating(SequenceFixtures.Commander), policy, SequenceFixtures.SquadA, false);

        Assert.False(check.IsAllowed);
        Assert.Contains("did not get through", check.Reason, StringComparison.Ordinal);
    }

    /// <summary>Springing a unit that has already gone is the entire point of a transfer.</summary>
    [Fact]
    public void AlreadySpentSubordinatesAreExactlyWhoTransfersAreFor()
    {
        var squadDone = GroundCombatSequence.EndFrame(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), policy);
        var backToBlue = GroundCombatSequence.EndFrame(
            GroundCombatSequence.BeginActivation(squadDone, SequenceFixtures.Red, SequenceFixtures.Watcher),
            policy);
        var commanding = GroundCombatSequence.BeginActivation(
            backToBlue, SequenceFixtures.Blue, SequenceFixtures.Commander);

        Assert.Contains(SequenceFixtures.SquadA, commanding.Side(SequenceFixtures.Blue).Activated);
        Assert.True(StarGruntTurn.CanTransfer(commanding, policy, SequenceFixtures.SquadA, true).IsAllowed);
    }

    [Fact]
    public void OnlyLevelsActuallyOnTheTableCountAsBypassed()
    {
        var withPlatoon = new HashSet<CommandLevel> { CommandLevel.Squad, CommandLevel.Platoon, CommandLevel.Company };
        var without = new HashSet<CommandLevel> { CommandLevel.Squad, CommandLevel.Company };

        Assert.Equal(1, CommandChain.LevelsBypassed(CommandLevel.Company, CommandLevel.Squad, withPlatoon));
        Assert.Equal(0, CommandChain.LevelsBypassed(CommandLevel.Company, CommandLevel.Squad, without));
        Assert.Equal(0, CommandChain.LevelsBypassed(CommandLevel.Platoon, CommandLevel.Squad, withPlatoon));
    }

    [Fact]
    public void EachBypassedLevelCostsTheSenderWhatTheCallersRulesSay()
    {
        // Two rungs a level, which is invented: what a skipped level costs is the players', and this
        // pins only that it is read and multiplied, not what it is.
        Assert.Equal(QualityDie.D12, CommandChain.CommunicationDie(QualityDie.D12, 0, rungsPerLevel: 2));
        Assert.Equal(QualityDie.D8, CommandChain.CommunicationDie(QualityDie.D12, 1, rungsPerLevel: 2));
        Assert.Equal(QualityDie.D4, CommandChain.CommunicationDie(QualityDie.D12, 2, rungsPerLevel: 2));

        // A message can get very unlikely but never falls off the bottom into being impossible.
        Assert.Equal(QualityDie.D4, CommandChain.CommunicationDie(QualityDie.D12, 9, rungsPerLevel: 2));
    }

    [Fact]
    public void RulesThatChargeNothingForABypassAreReadAsWritten()
    {
        // The control: a cost of nothing is a real answer and leaves the sender's die alone, where
        // the old walk would have taken a rung per level regardless.
        Assert.Equal(QualityDie.D8, CommandChain.CommunicationDie(QualityDie.D8, 3, rungsPerLevel: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommandChain.CommunicationDie(QualityDie.D8, 1, rungsPerLevel: -1));
    }

    [Fact]
    public void ThePolicyReportsTheBypassForTheCallerToShiftBy()
    {
        Assert.Equal(0, policy.LevelsBypassed(SequenceFixtures.Commander, SequenceFixtures.SquadA));
    }
}
