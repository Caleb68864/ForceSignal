using ForceSignal.Modules.StarGrunt.Game;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// An allocator that sends each hit to a written-down figure, so a test can say exactly who was
/// hit rather than hoping. Runs off the end by hitting the last figure.
/// </summary>
/// <param name="figures">The figure indices to hand out, in order.</param>
internal sealed class ScriptedAllocator(params int[] figures) : IFigureAllocator
{
    private readonly Queue<int> _figures = new(figures);

    /// <inheritdoc />
    public int Pick(int figureCount) =>
        figureCount <= 0 ? 0 : Math.Clamp(_figures.Count > 0 ? _figures.Dequeue() : figureCount - 1, 0, figureCount - 1);
}
