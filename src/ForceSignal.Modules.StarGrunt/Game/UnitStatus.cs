using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Game;

/// <summary>
/// What has happened to a unit so far.
/// </summary>
/// <remarks>
/// <para>
/// This is the game's own record. It is deliberately <em>not</em> a second copy of
/// <see cref="StarGruntUnitState"/>: that record says what the activation rules need to read, and is
/// produced from this one by <see cref="ToUnitState"/>. One definition of that shape, in the layer
/// that consumes it.
/// </para>
/// <para>
/// A record rather than a mutable class, because the game it belongs to is a value - see
/// <see cref="StarGruntGame"/> for why.
/// </para>
/// </remarks>
public sealed record UnitStatus
{
    /// <summary>Figures still standing.</summary>
    public int FiguresAlive { get; init; }

    /// <summary>
    /// Figures carrying a wound. Two wounds on one figure in a single resolution is a death, so
    /// these are the ones that survived a single hit rather than a running total of hurt.
    /// </summary>
    public int FiguresWounded { get; init; }

    /// <summary>Suppression markers on the unit, from none to three.</summary>
    public int SuppressionMarkers { get; init; }

    /// <summary>How much fight it has left.</summary>
    public ConfidenceLevel Confidence { get; init; } = ConfidenceLevel.Confident;

    /// <summary>True when it has come apart and owes a reorganise before anything else.</summary>
    public bool IsDisorganised { get; init; }

    /// <summary>True when it has something to hide behind. The players' call, not a calculation.</summary>
    public bool IsInCover { get; init; }

    /// <summary>True when the move about to be ordered would leave cover or close on a located enemy.</summary>
    public bool NextMoveLeavesCover { get; init; }

    /// <summary>True when the reaction test that move needs has been rolled and passed.</summary>
    public bool ReactionTestCleared { get; init; }

    /// <summary>How many subordinates this commander has sprung this turn.</summary>
    public int TransfersMadeThisTurn { get; init; }

    /// <summary>True when there is nobody left standing.</summary>
    public bool IsWipedOut => FiguresAlive <= 0;

    /// <summary>A unit as it comes to the table, at the strength its figures give it.</summary>
    /// <param name="unit">The unit being placed.</param>
    /// <returns>Its opening status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unit"/> is null.</exception>
    public static UnitStatus ForFullStrength(UnitDefinition unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return new UnitStatus { FiguresAlive = unit.FullStrength };
    }

    /// <summary>The state the activation rules read, for a unit at this command level.</summary>
    /// <param name="level">Where the unit sits in the chain of command.</param>
    /// <returns>The projection.</returns>
    public StarGruntUnitState ToUnitState(CommandLevel level) => new()
    {
        Level = level,
        SuppressionMarkers = SuppressionMarkers,
        Confidence = Confidence,
        IsDisorganised = IsDisorganised,
        IsInCover = IsInCover,
        NextMoveLeavesCover = NextMoveLeavesCover,
        ReactionTestCleared = ReactionTestCleared,
        TransfersMadeThisTurn = TransfersMadeThisTurn,
    };
}
