using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.GroundCombat.Tests;

/// <summary>
/// Putting a game down mid-reaction and picking it up again.
/// </summary>
/// <remarks>
/// The equality these tests lean on is structural and hand-written. Without that, every assertion
/// here would compare <see cref="System.Collections.Immutable.ImmutableArray{T}"/> members by
/// reference and pass or fail for reasons that have nothing to do with the session's contents.
/// <see cref="EqualityLooksAtContentsRatherThanReferences"/> is the test that says so out loud.
/// </remarks>
public class SequenceSerializationTests
{
    private const string ReactionFire = "reaction-fire";

    private static readonly string[] MidMoveCircumstances = ["open", "flank-exposed"];

    private static readonly UnitId Mover = new("blue-1");
    private static readonly UnitId Watcher = new("red-1");
    private static readonly UnitId Second = new("red-2");

    /// <summary>
    /// Half-way across the open ground, which is where the reaction resolves - and a place the token
    /// stands at neither end of its move.
    /// </summary>
    private static readonly InterruptGeometry CaughtBetweenMoves =
        InterruptGeometry.At(new GroundPoint(11.25, 6.5, 0.0), "open", "flank-exposed");

    /// <summary>Where the mover actually ends up, which is behind something.</summary>
    private static readonly InterruptGeometry EndOfTheDash =
        InterruptGeometry.At(new GroundPoint(19.0, 6.5, 0.0), "hard-cover");

    private static readonly ScriptedActivationPolicy Reacting = new()
    {
        TriggerRule = (_, _, step) => step.Kind == "move"
            ? new InterruptWindowRequest(
                ReactionFire, SequenceFixtures.Red, [Watcher, Second], ResponderCap: 1, CaughtBetweenMoves)
            : null,
    };

    [Fact]
    public void ASessionSuspendedMidReactionSurvivesARoundTrip()
    {
        var suspended = Suspended();

        var restored = SessionSerialization.Restore(SessionSerialization.Save(suspended));

        Assert.Equal(suspended, restored);
    }

    /// <summary>
    /// The subtle one. If the resolution point is not written down, a restored session resolves the
    /// pending shot against the mover's end position - which is in cover - and nothing anywhere
    /// complains. The shot is simply wrong, quietly, every time.
    /// </summary>
    [Fact]
    public void TheInterruptGeometrySurvivesAndIsNotTheMoversEndPosition()
    {
        var restored = SessionSerialization.Restore(SessionSerialization.Save(Suspended()));

        var window = restored.AwaitingAnswer;
        Assert.NotNull(window);
        Assert.Equal(CaughtBetweenMoves, window.Geometry);
        Assert.Equal(11.25, window.Geometry.ResolutionPoint.X);
        Assert.Equal(MidMoveCircumstances, window.Geometry.Circumstances);
        Assert.NotEqual(EndOfTheDash, window.Geometry);
    }

    [Fact]
    public void TheEligibleResponderSnapshotSurvivesInOrder()
    {
        var restored = SessionSerialization.Restore(SessionSerialization.Save(Suspended()));

        Assert.Equal(new[] { Watcher, Second }, restored.AwaitingAnswer!.EligibleResponders);
        Assert.Equal(1, restored.AwaitingAnswer.ResponderCap);
    }

    [Fact]
    public void ARestoredSessionCarriesOnFromWhereItStopped()
    {
        var restored = SessionSerialization.Restore(SessionSerialization.Save(Suspended()));

        var reacted = GroundCombatSequence.DeclareReaction(restored, Watcher, ReactionCost.ConsumesActivation);
        var resumed = GroundCombatSequence.EndFrame(reacted, Reacting);

        Assert.Equal(Mover, resumed.CurrentFrame!.Unit);
        Assert.Contains(Watcher, resumed.Side(SequenceFixtures.Red).Activated);
        Assert.Empty(resumed.WindowStack);
    }

    [Fact]
    public void ASuspendedFrameStackSurvivesAtDepth()
    {
        var nested = GroundCombatSequence.DeclareReaction(
            Suspended(), Watcher, ReactionCost.ConsumesActivation | ReactionCost.ForfeitsNextPrioritySlot);
        var mid = GroundCombatSequence.TakeStep(nested, ActivationStep.Of("fire", null, "main-gun"), Reacting);

        var restored = SessionSerialization.Restore(SessionSerialization.Save(mid));

        Assert.Equal(mid, restored);
        Assert.Equal(2, restored.Depth);
        Assert.Equal(ReactionCost.ConsumesActivation | ReactionCost.ForfeitsNextPrioritySlot, restored.CurrentFrame!.Cost);

        // Spending is read back off the steps rather than out of the file, so it cannot come back stale.
        Assert.Contains("main-gun", restored.CurrentFrame.ResourcesSpent);
    }

    /// <summary>
    /// Guards the round-trip tests against being tautologies. Two sessions built independently, with
    /// no collection instance in common, must still compare equal; and a single changed number deep
    /// inside the geometry must still make them unequal.
    /// </summary>
    [Fact]
    public void EqualityLooksAtContentsRatherThanReferences()
    {
        var one = Suspended();
        var two = Suspended();

        Assert.NotSame(one, two);
        Assert.Equal(one, two);
        Assert.Equal(one.GetHashCode(), two.GetHashCode());

        var moved = two with
        {
            WindowStack = two.WindowStack.SetItem(
                0,
                two.WindowStack[0] with { Geometry = EndOfTheDash }),
        };

        Assert.NotEqual(one, moved);
    }

    [Fact]
    public void RestoringNonsenseIsRejected()
    {
        Assert.Throws<ArgumentException>(() => SessionSerialization.Restore("  "));
        Assert.Throws<ArgumentException>(() => SessionSerialization.Restore("null"));
    }

    /// <summary>Blue's mover part-way across the open, with reaction fire declared but not answered.</summary>
    private static GroundCombatSession Suspended()
    {
        var forces = GroundCombatSession.Start(
            SideState.Of(SequenceFixtures.Blue, [Mover, new UnitId("blue-2")]),
            SideState.Of(SequenceFixtures.Red, [Watcher, Second]));

        return GroundCombatSequence.TakeStep(
            GroundCombatSequence.BeginActivation(
                SequenceFixtures.TurnUnderWay(forces, SequenceFixtures.Blue), SequenceFixtures.Blue, Mover),
            ActivationStep.Of("move"),
            Reacting);
    }
}
