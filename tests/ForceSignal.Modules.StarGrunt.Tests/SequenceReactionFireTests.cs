using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Reaction fire: the game's one out-of-sequence action, and the one place a shot resolves somewhere
/// the target never stands.
/// </summary>
public class SequenceReactionFireTests
{
    private static readonly GroundPoint HalfWayAcross = new(12.5, 7.0, 0.0);

    private readonly StarGruntBoard board = SequenceFixtures.Board();
    private readonly StarGruntActivationPolicy policy;

    public SequenceReactionFireTests()
    {
        policy = new StarGruntActivationPolicy(board);
        board.SetDashOpening(
            SequenceFixtures.SquadA,
            new ReactionOpening(
                HalfWayAcross,
                [SequenceFixtures.Watcher, SequenceFixtures.Reserve],
                ["open"]));
    }

    [Fact]
    public void ADeclaredDashOpensTheWindowAtItsMidPoint()
    {
        var dashing = Dashing();

        var window = dashing.AwaitingAnswer;
        Assert.NotNull(window);
        Assert.Equal(StarGruntActivationPolicy.ReactionFireWindow, window.Kind);
        Assert.Equal(SequenceFixtures.Red, window.RespondingSide);
        Assert.Equal(HalfWayAcross, window.Geometry.ResolutionPoint);
    }

    /// <summary>
    /// The information-hiding decision. Opening the window after a first move step and taking it back
    /// if no second move came would tell the opponent that a dash was considered, which is not
    /// something he gets to know.
    /// </summary>
    [Fact]
    public void ASingleMoveOpensNothingAtAll()
    {
        var moved = GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), StarGruntSteps.Move(), policy);

        Assert.Empty(moved.WindowStack);
        Assert.Null(moved.AwaitingAnswer);
    }

    [Fact]
    public void OnlyOneOpposingUnitMayReactToOneMover()
    {
        var dashing = Dashing();

        Assert.Equal(1, dashing.AwaitingAnswer!.ResponderCap);
        Assert.Equal(2, dashing.AwaitingAnswer.EligibleResponders.Length);

        var answered = GroundCombatSequence.DeclineReaction(dashing, SequenceFixtures.Watcher);

        // The cap closes the window on the first answer, so the second watcher never gets a say.
        Assert.Empty(answered.WindowStack);
    }

    [Fact]
    public void AUnitThatHasAlreadyActivatedCannotReact()
    {
        // Blue spends a squad, Red spends its watcher, and only then does Blue's other squad dash.
        var blueSpent = GroundCombatSequence.EndFrame(
            SequenceFixtures.Activating(SequenceFixtures.SquadB), policy);
        var watcherDone = GroundCombatSequence.EndFrame(
            GroundCombatSequence.BeginActivation(blueSpent, SequenceFixtures.Red, SequenceFixtures.Watcher),
            policy);

        var dashing = GroundCombatSequence.TakeStep(
            GroundCombatSequence.BeginActivation(watcherDone, SequenceFixtures.Blue, SequenceFixtures.SquadA),
            StarGruntSteps.Dash(),
            policy);

        Assert.Equal(SequenceFixtures.Reserve, Assert.Single(dashing.AwaitingAnswer!.EligibleResponders));
    }

    [Fact]
    public void ADashNobodyCanSeeOpensNoWindow()
    {
        var unseen = new StarGruntBoard().Set(SequenceFixtures.SquadA, new StarGruntUnitState());
        var blind = new StarGruntActivationPolicy(unseen);

        var dashing = GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), StarGruntSteps.Dash(), blind);

        Assert.Empty(dashing.WindowStack);
    }

    [Fact]
    public void ReactingCostsTheFirerItsActivationAndItsNextGo()
    {
        var reacting = StarGruntTurn.ReactWithFire(Dashing(), SequenceFixtures.Watcher);
        var shotDone = GroundCombatSequence.EndFrame(
            GroundCombatSequence.TakeStep(reacting, StarGruntSteps.Fire("saw"), policy), policy);

        Assert.Contains(SequenceFixtures.Watcher, shotDone.Side(SequenceFixtures.Red).Activated);
        Assert.Equal(1, shotDone.Side(SequenceFixtures.Red).ForfeitedSlots);

        // Play goes back to the mover, not over to the firer.
        Assert.Equal(SequenceFixtures.SquadA, shotDone.CurrentFrame!.Unit);
        Assert.Equal(SequenceFixtures.Blue, shotDone.ActiveSide);
    }

    [Fact]
    public void TheReactionResolvesOnItsOwnActionBudget()
    {
        var reacting = StarGruntTurn.ReactWithFire(Dashing(), SequenceFixtures.Watcher);

        Assert.Equal(2, StarGruntActivationPolicy.ActionsRemaining(reacting.CurrentFrame!));
        Assert.True(GroundCombatSequence.CanTakeStep(reacting, StarGruntSteps.Fire("saw"), policy).IsAllowed);
    }

    [Fact]
    public void TheMidPointSurvivesBeingPutDownAndPickedUpAgain()
    {
        var restored = SessionSerialization.Restore(SessionSerialization.Save(Dashing()));

        Assert.Equal(Dashing(), restored);
        Assert.Equal(HalfWayAcross, restored.AwaitingAnswer!.Geometry.ResolutionPoint);
        Assert.Equal("open", Assert.Single(restored.AwaitingAnswer.Geometry.Circumstances));
    }

    private GroundCombatSession Dashing() =>
        GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA), StarGruntSteps.Dash(), policy);
}
