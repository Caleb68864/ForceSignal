using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Morale;

namespace ForceSignal.Modules.StarGrunt.Sequence;

/// <summary>
/// Everything about one unit that the activation rules read but the sequence layer does not own.
/// </summary>
/// <remarks>
/// None of this lives in the session. The session is the shape of the turn; this is the state of the
/// world, and the caller keeps it because the caller is the one rolling dice and moving models.
/// </remarks>
public sealed record StarGruntUnitState
{
    /// <summary>Where the unit sits in the chain of command.</summary>
    public CommandLevel Level { get; init; } = CommandLevel.Squad;

    /// <summary>How many suppression markers it is carrying.</summary>
    public int SuppressionMarkers { get; init; }

    /// <summary>How much fight it has left.</summary>
    public ConfidenceLevel Confidence { get; init; } = ConfidenceLevel.Confident;

    /// <summary>True when the unit has come apart and owes a reorganise before anything else.</summary>
    public bool IsDisorganised { get; init; }

    /// <summary>True when the unit has something to hide behind.</summary>
    public bool IsInCover { get; init; }

    /// <summary>
    /// True when the move the player is about to order would take the unit out of cover or on to a
    /// located enemy.
    /// </summary>
    /// <remarks>
    /// A declaration, not a calculation. Whether a given move counts as leaving cover or advancing is
    /// an eyeball judgement at a real table, and the rest of this project has already settled on
    /// asking rather than computing occlusion.
    /// </remarks>
    public bool NextMoveLeavesCover { get; init; }

    /// <summary>
    /// True when the reaction test that move needs has been rolled and passed.
    /// </summary>
    /// <remarks>
    /// The policy never rolls. It refuses the move, names the test, and waits for the caller to come
    /// back having passed it. Failing costs the action rather than a confidence level, so a failed
    /// test is simply the caller spending the action on something else.
    /// </remarks>
    public bool ReactionTestCleared { get; init; }

    /// <summary>
    /// How many subordinates this commander has already sprung this turn.
    /// </summary>
    /// <remarks>
    /// This one is here reluctantly, and it is the sharpest thing StarGrunt asks for that the shared
    /// layer cannot answer - see the remarks on <see cref="StarGruntActivationPolicy"/>. A frame is
    /// discarded when it closes, so a limit that spans a whole turn has nowhere in the session to be
    /// read back from.
    /// </remarks>
    public int TransfersMadeThisTurn { get; init; }
}

/// <summary>
/// Where a dash can be caught, and by whom.
/// </summary>
/// <param name="Midpoint">
/// The point the reaction resolves against: the mover part-way through, which is a place it stands at
/// neither end of the dash.
/// </param>
/// <param name="Watchers">Enemy units that can see that point. Whether they can is the players' call.</param>
/// <param name="Circumstances">Cover and exposure at the mid-point, in the caller's own words.</param>
public sealed record ReactionOpening(
    GroundPoint Midpoint,
    ImmutableArray<UnitId> Watchers,
    ImmutableArray<string> Circumstances);

/// <summary>
/// The caller's model of the table, as the activation rules need to see it.
/// </summary>
/// <remarks>
/// Read live rather than snapshotted. A unit that reorganises part-way through its activation is no
/// longer disorganised for its second action, and the policy has to see that without being rebuilt.
/// </remarks>
public interface IStarGruntBoard
{
    /// <summary>The state of one unit.</summary>
    /// <param name="unit">The unit to look up.</param>
    /// <returns>Its state.</returns>
    StarGruntUnitState State(UnitId unit);

    /// <summary>Which command levels are represented on the table at all.</summary>
    IReadOnlySet<CommandLevel> CommandLevelsOnTable { get; }

    /// <summary>
    /// Where this unit's declared dash can be caught, or null when nothing can see it.
    /// </summary>
    /// <param name="mover">The unit about to dash.</param>
    /// <returns>The opening, or null when the dash is unobserved.</returns>
    ReactionOpening? DashOpening(UnitId mover);
}

/// <summary>
/// A straightforward board the caller can keep and update as the game goes on.
/// </summary>
/// <remarks>
/// Deliberately mutable, unlike everything in the sequence layer. The session is a value because a
/// suspended activation has to survive a restore; the board is the caller's live world model, and
/// pretending otherwise would mean rebuilding the policy after every casualty.
/// </remarks>
public sealed class StarGruntBoard : IStarGruntBoard
{
    private readonly Dictionary<UnitId, StarGruntUnitState> states = [];
    private readonly Dictionary<UnitId, ReactionOpening> openings = [];

    /// <summary>Which command levels are represented, taken from the units that have been placed.</summary>
    public IReadOnlySet<CommandLevel> CommandLevelsOnTable =>
        states.Values.Select(state => state.Level).ToHashSet();

    /// <summary>The state of one unit, defaulting to a fresh squad nobody has told us about.</summary>
    /// <param name="unit">The unit to look up.</param>
    /// <returns>Its state.</returns>
    public StarGruntUnitState State(UnitId unit) =>
        states.TryGetValue(unit, out var state) ? state : new StarGruntUnitState();

    /// <summary>Records or replaces a unit's state.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="state">Its new state.</param>
    /// <returns>This board, so setup reads as a chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public StarGruntBoard Set(UnitId unit, StarGruntUnitState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        states[unit] = state;
        return this;
    }

    /// <summary>Edits a unit's state in place.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="edit">What to change.</param>
    /// <returns>This board.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="edit"/> is null.</exception>
    public StarGruntBoard Update(UnitId unit, Func<StarGruntUnitState, StarGruntUnitState> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Set(unit, edit(State(unit)));
    }

    /// <summary>Says that this unit's dash can be caught, and where.</summary>
    /// <param name="mover">The unit about to dash.</param>
    /// <param name="opening">Where and by whom.</param>
    /// <returns>This board.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="opening"/> is null.</exception>
    public StarGruntBoard SetDashOpening(UnitId mover, ReactionOpening opening)
    {
        ArgumentNullException.ThrowIfNull(opening);
        openings[mover] = opening;
        return this;
    }

    /// <summary>Where this unit's dash can be caught, or null.</summary>
    /// <param name="mover">The unit about to dash.</param>
    /// <returns>The opening, or null.</returns>
    public ReactionOpening? DashOpening(UnitId mover) =>
        openings.TryGetValue(mover, out var opening) ? opening : null;
}
