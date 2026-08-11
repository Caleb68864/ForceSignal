using ForceSignal.Modules.StarGrunt.Game;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Sends every hit to a rifleman rather than the squad leader.
/// </summary>
/// <remarks>
/// Allocation is random in the rules, and a leader going down hands the squad a second suppression
/// marker - so a test that leaves allocation to chance is a test that passes most days and fails
/// the rest. Tests that are not about who was hit say so with this.
/// </remarks>
internal sealed class ScriptedFigureAllocator : IFigureAllocator
{
    /// <inheritdoc />
    public int Pick(int figureCount) => figureCount <= 1 ? 0 : Math.Min(4, figureCount - 1);
}
