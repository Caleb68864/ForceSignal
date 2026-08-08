using System.Text.Json;

namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>
/// Writing a suspended session out and reading it back.
/// </summary>
/// <remarks>
/// <para>
/// This is the reason the session is a value in the first place. A game can be put down mid-reaction
/// - the mover paused, the responders named, the shot not yet resolved - and has to come back
/// identical. Because the session is a record of immutable collections with no behaviour hanging off
/// it, there is no hand-written companion object to drift, and the serialized form is simply the
/// record's own shape.
/// </para>
/// <para>
/// Everything derived is marked to stay out of the file. A restored session recomputes what has been
/// spent from the steps rather than reading it back, so a stale value is not something the format
/// even has room to express.
/// </para>
/// </remarks>
public static class SessionSerialization
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
    };

    /// <summary>Writes a session out.</summary>
    /// <param name="session">The session to save.</param>
    /// <returns>The serialized session.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static string Save(GroundCombatSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return JsonSerializer.Serialize(session, Options);
    }

    /// <summary>Reads a session back.</summary>
    /// <param name="saved">A string produced by <see cref="Save"/>.</param>
    /// <returns>The restored session, equal to the one that was saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="saved"/> is blank or does not hold a session.</exception>
    public static GroundCombatSession Restore(string saved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saved);
        return JsonSerializer.Deserialize<GroundCombatSession>(saved, Options)
            ?? throw new ArgumentException("That is not a saved session.", nameof(saved));
    }
}
