using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Morale;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// What a unit's condition stops it doing: scattered, pinned, or short of nerve.
/// </summary>
public class SequenceGatingTests
{
    private readonly StarGruntBoard board = SequenceFixtures.Board();
    private readonly StarGruntActivationPolicy policy;

    public SequenceGatingTests() => policy = new StarGruntActivationPolicy(board);

    [Fact]
    public void ADisorganisedUnitMayDoNothingButReorganise()
    {
        Scatter();
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);

        Assert.False(GroundCombatSequence.CanTakeStep(opened, StarGruntSteps.Move(), policy).IsAllowed);
        Assert.False(GroundCombatSequence.CanTakeStep(opened, StarGruntSteps.Fire("saw"), policy).IsAllowed);
        Assert.True(
            GroundCombatSequence.CanTakeStep(
                opened, StarGruntSteps.Simple(StarGruntAction.Reorganise), policy).IsAllowed);
    }

    /// <summary>
    /// The board is read live, so once the caller has resolved the reorganise and cleared the flag the
    /// second action is free without the policy having to remember anything.
    /// </summary>
    [Fact]
    public void OnceItIsBackInOrderTheSecondActionIsFree()
    {
        Scatter();
        var reorganised = GroundCombatSequence.TakeStep(
            SequenceFixtures.Activating(SequenceFixtures.SquadA),
            StarGruntSteps.Simple(StarGruntAction.Reorganise),
            policy);

        board.Update(SequenceFixtures.SquadA, state => state with { IsDisorganised = false });

        Assert.True(GroundCombatSequence.CanTakeStep(reorganised, StarGruntSteps.Fire("saw"), policy).IsAllowed);
    }

    [Theory]
    [InlineData(StarGruntAction.Move)]
    [InlineData(StarGruntAction.Dash)]
    [InlineData(StarGruntAction.CloseAssault)]
    [InlineData(StarGruntAction.GoInPosition)]
    public void ASuppressedUnitWillNotMoveOrAssault(StarGruntAction action)
    {
        Pin(markers: 1);
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);

        Assert.False(
            GroundCombatSequence.CanTakeStep(opened, StarGruntSteps.Simple(action), policy).IsAllowed);
    }

    [Fact]
    public void ASuppressedUnitWillNotFire()
    {
        Pin(markers: 1);
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);

        Assert.False(GroundCombatSequence.CanTakeStep(opened, StarGruntSteps.Fire("saw"), policy).IsAllowed);
    }

    [Theory]
    [InlineData(StarGruntAction.Observe)]
    [InlineData(StarGruntAction.Communicate)]
    [InlineData(StarGruntAction.RemoveSuppression)]
    public void ItsLeaderCanStillLookTalkAndTryToGetHeadsBackUp(StarGruntAction action)
    {
        Pin(markers: 2);
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);

        Assert.True(
            GroundCombatSequence.CanTakeStep(opened, StarGruntSteps.Simple(action), policy).IsAllowed);
    }

    [Fact]
    public void ReorganisingWhilePinnedNeedsSomethingToHideBehind()
    {
        Pin(markers: 1);
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);
        var reorganise = StarGruntSteps.Simple(StarGruntAction.Reorganise);

        Assert.False(GroundCombatSequence.CanTakeStep(opened, reorganise, policy).IsAllowed);

        board.Update(SequenceFixtures.SquadA, state => state with { IsInCover = true });

        Assert.True(GroundCombatSequence.CanTakeStep(opened, reorganise, policy).IsAllowed);
    }

    /// <summary>
    /// The rule that a unit retrying a suppression removal with its second action can do nothing else
    /// that turn needs no code: the two attempts are the two actions.
    /// </summary>
    [Fact]
    public void RetryingTheRemovalUsesUpTheWholeActivationByItself()
    {
        Pin(markers: 2);
        var retried = GroundCombatSequence.TakeStep(
            GroundCombatSequence.TakeStep(
                SequenceFixtures.Activating(SequenceFixtures.SquadA),
                StarGruntSteps.Simple(StarGruntAction.RemoveSuppression),
                policy),
            StarGruntSteps.Simple(StarGruntAction.RemoveSuppression),
            policy);

        Assert.Equal(0, StarGruntActivationPolicy.ActionsRemaining(retried.CurrentFrame!));
    }

    [Fact]
    public void AShakenUnitNeedsItsNerveTestedBeforeItLeavesCover()
    {
        Waver(ConfidenceLevel.Shaken, leavingCover: true);
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);

        var check = GroundCombatSequence.CanTakeStep(opened, StarGruntSteps.Move(), policy);
        Assert.False(check.IsAllowed);
        Assert.Contains("reaction test", check.Reason, StringComparison.Ordinal);

        board.Update(SequenceFixtures.SquadA, state => state with { ReactionTestCleared = true });

        Assert.True(GroundCombatSequence.CanTakeStep(opened, StarGruntSteps.Move(), policy).IsAllowed);
    }

    [Fact]
    public void StayingPutInCoverNeedsNoTest()
    {
        Waver(ConfidenceLevel.Shaken, leavingCover: false);
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);

        Assert.True(GroundCombatSequence.CanTakeStep(opened, StarGruntSteps.Move(), policy).IsAllowed);
        Assert.True(GroundCombatSequence.CanTakeStep(opened, StarGruntSteps.Fire("saw"), policy).IsAllowed);
    }

    [Theory]
    [InlineData(ConfidenceLevel.Confident)]
    [InlineData(ConfidenceLevel.Steady)]
    public void SteadyTroopsGoWhereTheyAreToldWithoutBeingAsked(ConfidenceLevel level)
    {
        Waver(level, leavingCover: true);
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);

        Assert.True(GroundCombatSequence.CanTakeStep(opened, StarGruntSteps.Dash(), policy).IsAllowed);
    }

    [Fact]
    public void ARoutedUnitFiresAtNothing()
    {
        Waver(ConfidenceLevel.Routed, leavingCover: false);
        var opened = SequenceFixtures.Activating(SequenceFixtures.SquadA);

        var check = GroundCombatSequence.CanTakeStep(opened, StarGruntSteps.Fire("saw"), policy);

        Assert.False(check.IsAllowed);
        Assert.Contains("fires at nothing", check.Reason, StringComparison.Ordinal);
    }

    private void Scatter() =>
        board.Update(SequenceFixtures.SquadA, state => state with { IsDisorganised = true });

    private void Pin(int markers) =>
        board.Update(SequenceFixtures.SquadA, state => state with { SuppressionMarkers = markers });

    private void Waver(ConfidenceLevel level, bool leavingCover) =>
        board.Update(
            SequenceFixtures.SquadA,
            state => state with { Confidence = level, NextMoveLeavesCover = leavingCover });
}
