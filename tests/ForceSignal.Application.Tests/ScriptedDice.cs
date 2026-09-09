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

    /// <summary>
    /// The next face. The face count is ignored on purpose: a test that scripts its rolls is saying
    /// what it wants rolled, not what size die rolled it, and clamping the script to the profile
    /// would quietly rewrite the very numbers the test was pinning.
    /// </summary>
    /// <param name="faces">Faces the caller asked for. Not consulted.</param>
    public int Next(int faces)
    {
        _ = faces;
        return _faces.Count > 0 ? _faces.Dequeue() : Fallback;
    }
}
