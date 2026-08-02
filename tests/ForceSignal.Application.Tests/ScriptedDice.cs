namespace ForceSignal.Application.Tests;

/// <summary>
/// A die source a test can script. Faces are handed out in order and anything past the end of the
/// script falls back to a fixed face, so a test only has to pin the rolls it actually cares about -
/// the firing initiative die-off draws from the same source and would otherwise shift every roll
/// that follows it.
/// </summary>
internal sealed class ScriptedDice
{
    private Queue<int> _faces = new();

    /// <summary>Face returned once the script runs out.</summary>
    public int Fallback { get; init; } = 4;

    /// <summary>Replaces the script with these faces, in order.</summary>
    public void Script(params int[] faces) => _faces = new Queue<int>(faces);

    /// <summary>The next face.</summary>
    public int Next() => _faces.Count > 0 ? _faces.Dequeue() : Fallback;
}
