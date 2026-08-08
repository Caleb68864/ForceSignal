using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>
/// A point on the table, in whatever units the caller measures in.
/// </summary>
/// <param name="X">Distance along the table.</param>
/// <param name="Y">Distance across the table.</param>
/// <param name="Z">Height, for the games and situations that care.</param>
/// <remarks>
/// The ground scales differ between the two games and neither is written down here. These are the
/// caller's numbers in the caller's units; the sequence layer only ever stores them.
/// </remarks>
public readonly record struct GroundPoint(double X, double Y, double Z);

/// <summary>
/// Where and under what circumstances a pending interrupt will be resolved.
/// </summary>
/// <remarks>
/// <para>
/// This is stored rather than recomputed, and it is the subtle half of what an interrupt window has
/// to persist. StarGrunt's reaction fire resolves as though the mover were caught <em>between</em>
/// its two movement steps - out in the open half-way across ground it was sprinting over. That is a
/// position the token occupies at neither its start nor its end.
/// </para>
/// <para>
/// If the point is not written down, a session restored from disk resolves the pending reaction
/// against the mover's end position, which is the cover it was running <em>to</em>. Nothing throws
/// and nothing looks wrong; the shot simply resolves against the wrong circumstances, every time,
/// quietly. Recomputing it is not an option either, because by then the move has been committed and
/// the intermediate position is gone.
/// </para>
/// </remarks>
public sealed record InterruptGeometry
{
    /// <summary>The position the interrupt resolves against.</summary>
    public GroundPoint ResolutionPoint { get; init; }

    /// <summary>
    /// The cover, exposure and posture that applied at that point, named by the caller. Held as
    /// opaque tags because the two games describe the same idea with different vocabularies and
    /// neither vocabulary belongs in the shared layer.
    /// </summary>
    public ImmutableArray<string> Circumstances { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Builds a geometry snapshot.</summary>
    /// <param name="point">Where the interrupt resolves.</param>
    /// <param name="circumstances">Caller-named circumstances at that point.</param>
    /// <returns>The snapshot.</returns>
    public static InterruptGeometry At(GroundPoint point, params string[] circumstances) =>
        new()
        {
            ResolutionPoint = point,
            Circumstances = circumstances is null ? ImmutableArray<string>.Empty : [.. circumstances],
        };

    /// <summary>Structural, because <see cref="Circumstances"/> would otherwise compare by reference.</summary>
    /// <param name="other">The geometry to compare with.</param>
    /// <returns>True when both describe the same situation.</returns>
    public bool Equals(InterruptGeometry? other) =>
        other is not null
        && ResolutionPoint == other.ResolutionPoint
        && StructuralEquality.Sequence(Circumstances, other.Circumstances);

    /// <summary>Hashes in step with <see cref="Equals(InterruptGeometry)"/>.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(ResolutionPoint, StructuralEquality.SequenceHash(Circumstances));
}

/// <summary>
/// A policy's request to open a window, before the session has given it an identity.
/// </summary>
/// <param name="Kind">
/// The policy's name for this sort of interrupt. The shared layer compares these to enforce that no
/// reaction can retrigger its own kind, but never interprets them.
/// </param>
/// <param name="RespondingSide">The side whose units may answer.</param>
/// <param name="EligibleResponders">Who may answer, decided now and then frozen.</param>
/// <param name="ResponderCap">
/// How many may answer before the window closes regardless of who is left. One, for the games'
/// single-reactor rules; use <see cref="int.MaxValue"/> for no cap.
/// </param>
/// <param name="Geometry">Where the answers resolve.</param>
public readonly record struct InterruptWindowRequest(
    string Kind,
    SideId RespondingSide,
    ImmutableArray<UnitId> EligibleResponders,
    int ResponderCap,
    InterruptGeometry Geometry);

/// <summary>
/// A suspended frame waiting to hear whether anybody is going to interrupt it.
/// </summary>
/// <remarks>
/// Two things here are snapshots rather than live queries, and both have to be, for the same reason:
/// the window may outlive the process. <see cref="EligibleResponders"/> is fixed when the window
/// opens because the reaction resolves against the situation at the moment of the trigger, and a
/// recomputed eligibility list would drift as the interrupt itself changes the board.
/// <see cref="Geometry"/> is fixed for the reason set out on <see cref="InterruptGeometry"/>.
/// </remarks>
public sealed record InterruptWindow
{
    /// <summary>Identity, stable across a serialize and restore.</summary>
    public WindowId Id { get; init; }

    /// <summary>The policy's name for this sort of interrupt.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>The side whose units may answer.</summary>
    public SideId RespondingSide { get; init; }

    /// <summary>Who was eligible when the window opened. Frozen from that moment.</summary>
    public ImmutableArray<UnitId> EligibleResponders { get; init; } = ImmutableArray<UnitId>.Empty;

    /// <summary>Who has answered so far, whether by reacting or by declining.</summary>
    public ImmutableHashSet<UnitId> Answered { get; init; } = ImmutableHashSet<UnitId>.Empty;

    /// <summary>How many may answer before the window closes regardless.</summary>
    public int ResponderCap { get; init; } = int.MaxValue;

    /// <summary>Where and how the answers resolve.</summary>
    public InterruptGeometry Geometry { get; init; } = new();

    /// <summary>
    /// How deep the frame stack was when this window opened, which is also the depth it must return
    /// to before the window is answerable again.
    /// </summary>
    /// <remarks>
    /// This is how a nested window and its reaction frame stay in step without a back-pointer in each
    /// direction: a window is the one currently being answered exactly when the stack is back at the
    /// depth it suspended.
    /// </remarks>
    public int SuspendedAtDepth { get; init; }

    /// <summary>True when nobody is left to answer, or the cap has been reached.</summary>
    [JsonIgnore]
    public bool IsClosed =>
        Answered.Count >= ResponderCap
        || EligibleResponders.IsDefaultOrEmpty
        || EligibleResponders.All(Answered.Contains);

    /// <summary>True when this unit may still answer.</summary>
    /// <param name="unit">The would-be responder.</param>
    /// <returns>True when the unit was eligible and has not answered yet.</returns>
    public bool MayAnswer(UnitId unit) =>
        !EligibleResponders.IsDefaultOrEmpty
        && EligibleResponders.Contains(unit)
        && !Answered.Contains(unit);

    /// <summary>Structural, because two of the members would otherwise compare by reference.</summary>
    /// <param name="other">The window to compare with.</param>
    /// <returns>True when both windows say the same thing.</returns>
    public bool Equals(InterruptWindow? other) =>
        other is not null
        && Id == other.Id
        && Kind == other.Kind
        && RespondingSide == other.RespondingSide
        && ResponderCap == other.ResponderCap
        && SuspendedAtDepth == other.SuspendedAtDepth
        && Geometry == other.Geometry
        && StructuralEquality.Sequence(EligibleResponders, other.EligibleResponders)
        && StructuralEquality.Set(Answered, other.Answered);

    /// <summary>Hashes in step with <see cref="Equals(InterruptWindow)"/>.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode() => HashCode.Combine(
        Id,
        Kind,
        RespondingSide,
        ResponderCap,
        SuspendedAtDepth,
        Geometry,
        StructuralEquality.SequenceHash(EligibleResponders),
        StructuralEquality.SetHash(Answered));
}
