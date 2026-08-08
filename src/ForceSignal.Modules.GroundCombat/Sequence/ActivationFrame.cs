using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>Why a frame is on the stack.</summary>
public enum FrameKind
{
    /// <summary>A unit taking its own turn, chosen by the side whose go it is.</summary>
    Activation = 0,

    /// <summary>
    /// A unit answering an interrupt window that suspended somebody else's frame. Opportunity fire,
    /// area-defence interception and reaction fire are all this kind.
    /// </summary>
    Reaction = 1,

    /// <summary>
    /// A whole extra activation handed to a subordinate by a commander who spent an action on it.
    /// Only StarGrunt has this, and it costs the shared layer nothing: the stack was already needed.
    /// </summary>
    Granted = 2,
}

/// <summary>
/// What answering an interrupt costs the responder.
/// </summary>
/// <remarks>
/// This is a property of the declaration rather than of the window or of the game, because Dirtside
/// alone needs two different answers to it: opportunity fire spends the firing unit's whole
/// activation for the turn, while an area-defence system with live sensors intercepts without losing
/// anything - it is a standing reaction, not an activation traded away. StarGrunt's reaction fire
/// wants both bits set, since it also counts as that player's next go.
/// </remarks>
[Flags]
public enum ReactionCost
{
    /// <summary>A standing reaction. The responder is no worse off for having answered.</summary>
    None = 0,

    /// <summary>The responder's activation for this turn is spent, whether or not all of it fired.</summary>
    ConsumesActivation = 1,

    /// <summary>
    /// The responder's side loses its next go in the alternation as well, so play returns to the
    /// interrupted player rather than passing over.
    /// </summary>
    ForfeitsNextPrioritySlot = 2,
}

/// <summary>
/// One entry on the frame stack: a unit part-way through doing something.
/// </summary>
/// <remarks>
/// <para>
/// The stack is justified by Dirtside on its own, which is the test of whether it belongs down here
/// in the shared layer. A mover is interrupted by opportunity fire; that fire launches a missile;
/// area-defence interception interrupts the interrupt. Three frames, no StarGrunt anywhere near it.
/// A single current frame could not hold that.
/// </para>
/// </remarks>
public sealed record ActivationFrame
{
    /// <summary>Identity, stable across a serialize and restore.</summary>
    public FrameId Id { get; init; }

    /// <summary>Why the frame is here.</summary>
    public FrameKind Kind { get; init; }

    /// <summary>The side that owns the acting unit.</summary>
    public SideId Side { get; init; }

    /// <summary>The unit acting in this frame.</summary>
    public UnitId Unit { get; init; }

    /// <summary>What has been done so far, oldest first.</summary>
    public ImmutableArray<ActivationStep> Steps { get; init; } = ImmutableArray<ActivationStep>.Empty;

    /// <summary>
    /// The window this frame was pushed to answer, for a <see cref="FrameKind.Reaction"/>. Null
    /// otherwise.
    /// </summary>
    public WindowId? AnsweringWindow { get; init; }

    /// <summary>What answering will cost when this frame ends. Meaningless unless it is a reaction.</summary>
    public ReactionCost Cost { get; init; } = ReactionCost.None;

    /// <summary>
    /// Everything used up in this frame, read back off the steps rather than tracked alongside them.
    /// </summary>
    /// <remarks>
    /// Because this is derived and the frame is what it is derived from, a limit expressed against it
    /// resets exactly when a new frame is pushed. That is the official errata behaviour for a
    /// weapon's fire limit - per activation, not per game turn - and it falls out rather than being
    /// arranged.
    /// </remarks>
    [JsonIgnore]
    public ImmutableHashSet<string> ResourcesSpent =>
        Steps.IsDefaultOrEmpty
            ? ImmutableHashSet<string>.Empty
            : Steps
                .SelectMany(step => step.Consumes.IsDefault ? ImmutableArray<string>.Empty : step.Consumes)
                .ToImmutableHashSet(StringComparer.Ordinal);

    /// <summary>True when the named resource has already been used up in this frame.</summary>
    /// <param name="resource">The resource name to look for.</param>
    /// <returns>True when a step in this frame already consumed it.</returns>
    public bool HasSpent(string resource) => ResourcesSpent.Contains(resource);

    /// <summary>Structural, because <see cref="Steps"/> would otherwise compare by reference.</summary>
    /// <param name="other">The frame to compare with.</param>
    /// <returns>True when both frames say the same thing.</returns>
    public bool Equals(ActivationFrame? other) =>
        other is not null
        && Id == other.Id
        && Kind == other.Kind
        && Side == other.Side
        && Unit == other.Unit
        && AnsweringWindow == other.AnsweringWindow
        && Cost == other.Cost
        && StructuralEquality.Sequence(Steps, other.Steps);

    /// <summary>Hashes in step with <see cref="Equals(ActivationFrame)"/>.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode() => HashCode.Combine(
        Id, Kind, Side, Unit, AnsweringWindow, Cost, StructuralEquality.SequenceHash(Steps));
}
