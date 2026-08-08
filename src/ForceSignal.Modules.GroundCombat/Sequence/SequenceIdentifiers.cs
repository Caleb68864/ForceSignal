namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>One of the two players in a game.</summary>
/// <param name="Value">Whatever the caller uses to name a side. Opaque here.</param>
/// <remarks>
/// The shared layer never interprets these strings. It only ever compares them, so a caller may use
/// a force name, a player id, or a colour without the sequence engine caring which.
/// </remarks>
public readonly record struct SideId(string Value)
{
    /// <summary>The caller's own name for the side.</summary>
    /// <returns>The wrapped value.</returns>
    public override string ToString() => Value;
}

/// <summary>
/// The thing that gets activated: a StarGrunt squad or a Dirtside platoon. Both games alternate at
/// this granularity, which is why the shared layer has one identifier for it rather than two.
/// </summary>
/// <param name="Value">The caller's name for the unit.</param>
public readonly record struct UnitId(string Value)
{
    /// <summary>The caller's own name for the unit.</summary>
    /// <returns>The wrapped value.</returns>
    public override string ToString() => Value;
}

/// <summary>
/// Something inside a unit that a step may be aimed at - a Dirtside element, a StarGrunt detached
/// team or figure group.
/// </summary>
/// <param name="Value">The caller's name for the element.</param>
/// <remarks>
/// This exists because Dirtside's activation is not a budget the unit spends: the platoon activates
/// and then each element inside it independently picks its own move/act order. A step therefore has
/// to be able to say <em>which</em> element took it, and the shared layer has to carry that without
/// understanding it.
/// </remarks>
public readonly record struct ElementId(string Value)
{
    /// <summary>The caller's own name for the element.</summary>
    /// <returns>The wrapped value.</returns>
    public override string ToString() => Value;
}

/// <summary>Identifies one frame on the stack for the life of a session.</summary>
/// <param name="Value">A number minted in order by the session.</param>
/// <remarks>
/// Minted from a counter carried on the session rather than from a GUID, so that a serialized
/// session and its restore are byte-comparable and a replay of the same transitions produces the
/// same identifiers.
/// </remarks>
public readonly record struct FrameId(int Value);

/// <summary>Identifies one interrupt window for the life of a session.</summary>
/// <param name="Value">A number minted in order by the session.</param>
public readonly record struct WindowId(int Value);
