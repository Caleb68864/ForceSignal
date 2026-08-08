using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.GroundCombat.Tests;

/// <summary>
/// A stand-in for a game's rules, driven entirely by whatever the test hands it.
/// </summary>
/// <remarks>
/// Neither game's real policy exists yet, and the shared layer is supposed to work without knowing
/// anything about either. Testing it against a scripted double rather than against a half-written
/// Dirtside policy is the only way to be sure no game knowledge has leaked downwards.
/// </remarks>
internal sealed class ScriptedActivationPolicy : IActivationPolicy
{
    public Func<GroundCombatSession, ActivationFrame, ActivationStep, SequenceCheck> StepRule { get; set; } =
        (_, _, _) => SequenceCheck.Allowed;

    public Func<GroundCombatSession, ActivationFrame, bool> CompletionRule { get; set; } =
        (_, _) => true;

    public Func<GroundCombatSession, ActivationFrame, ActivationStep, InterruptWindowRequest?> TriggerRule { get; set; } =
        (_, _, _) => null;

    public SequenceCheck IsStepLegal(GroundCombatSession session, ActivationFrame frame, ActivationStep step) =>
        StepRule(session, frame, step);

    public bool IsFrameComplete(GroundCombatSession session, ActivationFrame frame) =>
        CompletionRule(session, frame);

    public InterruptWindowRequest? WindowOpenedBy(
        GroundCombatSession session,
        ActivationFrame frame,
        ActivationStep step) => TriggerRule(session, frame, step);

    /// <summary>
    /// A step rule that refuses to spend the same thing twice inside one frame.
    /// </summary>
    /// <remarks>
    /// This is how a per-activation limit is meant to be written: against the frame's derived
    /// <see cref="ActivationFrame.ResourcesSpent"/>, with nothing kept on the side. A limit written
    /// this way resets when a new frame is pushed and cannot be reset by anything else.
    /// </remarks>
    public static SequenceCheck OncePerFrame(
        GroundCombatSession session,
        ActivationFrame frame,
        ActivationStep step)
    {
        _ = session;
        var clash = step.Consumes.FirstOrDefault(frame.HasSpent);
        return clash is null
            ? SequenceCheck.Allowed
            : SequenceCheck.Refused($"{clash} has already been used in this frame.");
    }
}

/// <summary>Shorthand for building the small positions these tests need.</summary>
internal static class SequenceFixtures
{
    public static readonly SideId Blue = new("blue");
    public static readonly SideId Red = new("red");

    public static GroundCombatSession Game(int blueUnits, int redUnits) =>
        GroundCombatSession.Start(
            SideState.Of(Blue, Units("blue", blueUnits)),
            SideState.Of(Red, Units("red", redUnits)));

    public static ImmutableArray<UnitId> Units(string prefix, int count) =>
        [.. Enumerable.Range(1, count).Select(index => new UnitId($"{prefix}-{index}"))];

    /// <summary>A turn started and the alternation handed to the named side.</summary>
    public static GroundCombatSession TurnUnderWay(GroundCombatSession session, SideId first)
    {
        var begun = GroundCombatSequence.BeginTurn(session);
        var chooser = SequenceGuards.FirstActivationChooser(begun) ?? first;
        return GroundCombatSequence.ChooseFirstActivator(begun, chooser, takeIt: chooser == first);
    }

    /// <summary>Rewrites one side, for setting up a position a legal sequence would take a while to reach.</summary>
    public static GroundCombatSession WithSideForTest(
        this GroundCombatSession session,
        SideId id,
        Func<SideState, SideState> edit) =>
        session with
        {
            Sides = [.. session.Sides.Select(side => side.Id == id ? edit(side) : side)],
        };

    public static InterruptWindowRequest Window(
        string kind,
        SideId responders,
        InterruptGeometry geometry,
        params UnitId[] eligible) =>
        new(kind, responders, [.. eligible], ResponderCap: 1, geometry);
}
