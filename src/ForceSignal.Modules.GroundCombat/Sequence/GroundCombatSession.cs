using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>Where a turn has got to.</summary>
public enum TurnPhase
{
    /// <summary>Forces are on the table but no turn has begun.</summary>
    NotStarted = 0,

    /// <summary>
    /// Waiting for the side entitled to choose to say whether it takes the first activation or gives
    /// it away. Decided fresh at the top of every turn, not once per game.
    /// </summary>
    ChoosingFirstActivator = 1,

    /// <summary>The alternating chain of activations.</summary>
    Activating = 2,

    /// <summary>Both sides are done. Nothing more happens until the next turn begins.</summary>
    TurnEnded = 3,
}

/// <summary>
/// The whole state of an alternating-activation game turn, as a value.
/// </summary>
/// <remarks>
/// <para>
/// This is a value rather than a mutable session object because a suspended activation has to
/// survive being serialized and restored. A mutable class has no natural serialization boundary -
/// you end up hand-writing a companion DTO, and the DTO drifts. As an immutable record of immutable
/// collections there is nothing to drift from, restore fidelity is an equality assertion, and undo,
/// replay and the after-action log come free because the frames <em>are</em> the log.
/// </para>
/// <para>
/// Equality is structural throughout, overridden by hand. See <see cref="StructuralEquality"/> for
/// why that choice was made rather than exposing a second comparison alongside the default one.
/// </para>
/// <para>
/// Nothing in here is a rule. Every number a game needs - how many actions, how far, how many may
/// react - comes from the caller or from its <see cref="IActivationPolicy"/>.
/// </para>
/// </remarks>
public sealed record GroundCombatSession
{
    /// <summary>Which turn this is, counting from one once the first turn has begun.</summary>
    public int TurnNumber { get; init; }

    /// <summary>Where the turn has got to.</summary>
    public TurnPhase Phase { get; init; } = TurnPhase.NotStarted;

    /// <summary>The two players. Exactly two, because both shared guards compare you with your opponent.</summary>
    public ImmutableArray<SideState> Sides { get; init; } = ImmutableArray<SideState>.Empty;

    /// <summary>Who took the first activation this turn, once that has been settled.</summary>
    public SideId? FirstActivator { get; init; }

    /// <summary>Whose go it is.</summary>
    public SideId? ActiveSide { get; init; }

    /// <summary>The frame stack, bottom first. The last entry is what is happening right now.</summary>
    public ImmutableArray<ActivationFrame> FrameStack { get; init; } = ImmutableArray<ActivationFrame>.Empty;

    /// <summary>Open interrupt windows, oldest first. The last entry is the innermost.</summary>
    public ImmutableArray<InterruptWindow> WindowStack { get; init; } = ImmutableArray<InterruptWindow>.Empty;

    /// <summary>
    /// The unbroken run of passes at the end of the current chain. Two different sides in here means
    /// neither wants to go on and the turn may end.
    /// </summary>
    public ImmutableArray<SideId> ConsecutivePasses { get; init; } = ImmutableArray<SideId>.Empty;

    /// <summary>
    /// The counter frame and window identities are minted from.
    /// </summary>
    /// <remarks>
    /// A counter rather than a GUID so that a restored session is equal to the one that was saved,
    /// and so that replaying the same transitions from the same start produces the same session.
    /// </remarks>
    public int NextIdentity { get; init; } = 1;

    /// <summary>Sets up a game between two sides, before the first turn begins.</summary>
    /// <param name="first">One side.</param>
    /// <param name="second">The other.</param>
    /// <returns>A session ready for <see cref="GroundCombatSequence.BeginTurn"/>.</returns>
    /// <exception cref="ArgumentNullException">Either side is null.</exception>
    /// <exception cref="ArgumentException">Both sides carry the same identifier.</exception>
    public static GroundCombatSession Start(SideState first, SideState second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Id == second.Id)
        {
            throw new ArgumentException("The two sides must have different identifiers.", nameof(second));
        }

        return new GroundCombatSession { Sides = [first, second] };
    }

    /// <summary>What is happening right now, or null when nothing is.</summary>
    [JsonIgnore]
    public ActivationFrame? CurrentFrame =>
        FrameStack.IsDefaultOrEmpty ? null : FrameStack[^1];

    /// <summary>The innermost open window, or null when none is open.</summary>
    [JsonIgnore]
    public InterruptWindow? InnermostWindow =>
        WindowStack.IsDefaultOrEmpty ? null : WindowStack[^1];

    /// <summary>
    /// The window that is waiting for an answer right now, or null.
    /// </summary>
    /// <remarks>
    /// A window is answerable only while the stack is back at the depth it suspended. Once somebody
    /// declares a reaction the stack is one deeper, and that reaction's own frame - not the window -
    /// is what the session is doing.
    /// </remarks>
    [JsonIgnore]
    public InterruptWindow? AwaitingAnswer =>
        InnermostWindow is { } window && window.SuspendedAtDepth == Depth ? window : null;

    /// <summary>How deep the frame stack is. Also the nesting depth the ceiling is checked against.</summary>
    [JsonIgnore]
    public int Depth => FrameStack.IsDefault ? 0 : FrameStack.Length;

    /// <summary>True when no frame is open and a new activation could begin.</summary>
    [JsonIgnore]
    public bool IsIdle => Depth == 0;

    /// <summary>Looks up a side.</summary>
    /// <param name="id">The side to find.</param>
    /// <returns>Its state.</returns>
    /// <exception cref="ArgumentException">No such side is playing.</exception>
    public SideState Side(SideId id) =>
        Sides.FirstOrDefault(side => side.Id == id)
        ?? throw new ArgumentException($"No side called '{id}' is in this game.", nameof(id));

    /// <summary>The other player.</summary>
    /// <param name="id">The side to look away from.</param>
    /// <returns>The opposing side's state.</returns>
    /// <exception cref="ArgumentException">No such side is playing.</exception>
    public SideState Opponent(SideId id)
    {
        _ = Side(id);
        return Sides.First(side => side.Id != id);
    }

    /// <summary>True when a window of this kind is already open somewhere below.</summary>
    /// <param name="kind">The policy's name for the interrupt.</param>
    /// <returns>True when that kind is already open.</returns>
    /// <remarks>
    /// This is the structural half of what bounds nesting: no reaction may retrigger its own kind, so
    /// opportunity fire inside opportunity fire is not a matter of depth-counting but of the kind
    /// already being on the stack.
    /// </remarks>
    public bool IsWindowKindOpen(string kind) =>
        !WindowStack.IsDefaultOrEmpty
        && WindowStack.Any(window => string.Equals(window.Kind, kind, StringComparison.Ordinal));

    /// <summary>Replaces one side's state, leaving the other alone.</summary>
    internal GroundCombatSession WithSide(SideState replacement) =>
        this with { Sides = [.. Sides.Select(side => side.Id == replacement.Id ? replacement : side)] };

    /// <summary>Structural, because every collection member would otherwise compare by reference.</summary>
    /// <param name="other">The session to compare with.</param>
    /// <returns>True when both sessions describe the same position.</returns>
    public bool Equals(GroundCombatSession? other) =>
        other is not null
        && TurnNumber == other.TurnNumber
        && Phase == other.Phase
        && FirstActivator == other.FirstActivator
        && ActiveSide == other.ActiveSide
        && NextIdentity == other.NextIdentity
        && StructuralEquality.Sequence(Sides, other.Sides)
        && StructuralEquality.Sequence(FrameStack, other.FrameStack)
        && StructuralEquality.Sequence(WindowStack, other.WindowStack)
        && StructuralEquality.Sequence(ConsecutivePasses, other.ConsecutivePasses);

    /// <summary>Hashes in step with <see cref="Equals(GroundCombatSession)"/>.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode() => HashCode.Combine(
        TurnNumber,
        Phase,
        FirstActivator,
        ActiveSide,
        NextIdentity,
        StructuralEquality.SequenceHash(Sides),
        StructuralEquality.SequenceHash(FrameStack),
        HashCode.Combine(
            StructuralEquality.SequenceHash(WindowStack),
            StructuralEquality.SequenceHash(ConsecutivePasses)));
}
