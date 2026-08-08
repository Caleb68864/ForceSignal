using System.Collections.Immutable;

namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>
/// One thing a unit did inside a frame.
/// </summary>
/// <remarks>
/// <para>
/// An activation is deliberately <b>not</b> a budget of two actions. That is StarGrunt's shape and
/// Dirtside cannot express it: Dirtside activates a platoon and then each element inside it picks
/// its own move/act order independently, so there is no single pool being drawn down. A frame
/// therefore accumulates an ordered list of these, and the shared layer never counts them - it asks
/// the game's policy whether the frame is finished.
/// </para>
/// <para>
/// <see cref="Consumes"/> is the whole of the resource model. Nothing about what has been spent is
/// stored anywhere; it is read back off the steps. That is what makes StarGrunt's per-activation
/// weapon-fire limit structurally impossible to get wrong - the limit resets when a new frame starts
/// because there is nowhere for it to have been remembered. It is also one fewer field that could go
/// stale across a serialize and restore.
/// </para>
/// </remarks>
public sealed record ActivationStep
{
    /// <summary>What kind of step this was. Opaque to the shared layer; the policy names these.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Which element inside the unit took the step, when the game distinguishes them. Null means the
    /// step was taken by the unit as a whole, which is StarGrunt's usual case.
    /// </summary>
    public ElementId? Subject { get; init; }

    /// <summary>
    /// Names of the things this step used up, scoped to the frame it was taken in - a weapon system,
    /// a mount, a sensor. The caller invents these names; the shared layer only compares them.
    /// </summary>
    public ImmutableArray<string> Consumes { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Builds a step.</summary>
    /// <param name="kind">The policy's name for the step.</param>
    /// <param name="subject">The element that took it, or null for the whole unit.</param>
    /// <param name="consumes">Resources the step uses up within its frame.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is blank.</exception>
    public static ActivationStep Of(string kind, ElementId? subject = null, params string[] consumes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return new ActivationStep
        {
            Kind = kind,
            Subject = subject,
            Consumes = consumes is null ? ImmutableArray<string>.Empty : [.. consumes],
        };
    }

    /// <summary>Structural, not the reference equality an <see cref="ImmutableArray{T}"/> member would give.</summary>
    /// <param name="other">The step to compare with.</param>
    /// <returns>True when both steps say the same thing.</returns>
    public bool Equals(ActivationStep? other) =>
        other is not null
        && Kind == other.Kind
        && Subject == other.Subject
        && StructuralEquality.Sequence(Consumes, other.Consumes);

    /// <summary>Hashes in step with <see cref="Equals(ActivationStep)"/>.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Kind, Subject, StructuralEquality.SequenceHash(Consumes));
}
