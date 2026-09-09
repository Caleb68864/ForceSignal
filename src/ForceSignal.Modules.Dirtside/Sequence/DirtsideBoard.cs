using System.Collections.Immutable;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Sequence;

/// <summary>
/// Everything about one unit that the activation rules read but the sequence layer does not own.
/// </summary>
/// <remarks>
/// None of this lives in the session. The session is the shape of the turn; this is the state of the
/// world, and the caller keeps it because the caller is the one drawing chits and moving models.
/// </remarks>
public sealed record DirtsideUnitState
{
    /// <summary>Which column of the confidence effects table this unit reads.</summary>
    public DirtsideUnitKind Kind { get; init; } = DirtsideUnitKind.Armour;

    /// <summary>
    /// True for a vehicle run by an onboard intelligence, which is immune to confidence and reaction
    /// entirely.
    /// </summary>
    /// <remarks>
    /// A whole class of rule switched off by one flag, and it is worth saying why that is not a
    /// shortcut. A cybertank does not have unshakeable morale, it has none: it carries no confidence
    /// marker at all, so there is no level for a test to move and nothing for a threat level to be
    /// added to. Modelling it as a unit that always passes would leave it able to fail, one bad
    /// refactor later.
    /// </remarks>
    public bool IsCybertank { get; init; }

    /// <summary>Where its confidence marker stands. Meaningless for a cybertank, which carries none.</summary>
    public ConfidenceLevel Confidence { get; init; } = ConfidenceLevel.Confident;

    /// <summary>True when it is carrying an Under Fire marker.</summary>
    public bool IsUnderFire { get; init; }

    /// <summary>True when the unit has come apart and may only move to close its ranks.</summary>
    public bool IsDisorganised { get; init; }

    /// <summary>
    /// True when the move the player is about to order would take the unit toward the enemy or out
    /// of cover.
    /// </summary>
    /// <remarks>
    /// A declaration, not a calculation. Whether a move counts as advancing is an eyeball judgement
    /// at a real table, and this project has already settled on asking rather than computing.
    /// </remarks>
    public bool NextMoveAdvancesOnTheEnemy { get; init; }

    /// <summary>
    /// True when the reaction test the next move needs has been rolled and passed.
    /// </summary>
    /// <remarks>
    /// The policy never rolls. It refuses the move, names the test, and waits for the caller to come
    /// back having passed it. Failing costs the action rather than a confidence level, so a failed
    /// test is simply the caller doing something else with the activation it announced.
    /// </remarks>
    public bool ReactionTestCleared { get; init; }
}

/// <summary>
/// Where an interrupt can be answered from, and by whom.
/// </summary>
/// <param name="ResolutionPoint">The point the interrupt resolves against.</param>
/// <param name="Watchers">
/// Units that could answer, in the caller's judgement. Whether a weapon reaches and a line of sight
/// exists is settled by tape and eyeball at a real table, so it is settled by the caller here.
/// </param>
/// <param name="Circumstances">Cover and exposure at that point, in the caller's own words.</param>
public sealed record InterruptOpening(
    GroundPoint ResolutionPoint,
    ImmutableArray<UnitId> Watchers,
    ImmutableArray<string> Circumstances);

/// <summary>
/// The caller's model of the table, as Dirtside's activation rules need to see it.
/// </summary>
/// <remarks>
/// Read live rather than snapshotted, so that a unit which closes its ranks part-way through its
/// activation is no longer disorganised for the elements that have not moved yet.
/// </remarks>
public interface IDirtsideBoard
{
    /// <summary>The state of one unit.</summary>
    /// <param name="unit">The unit to look up.</param>
    /// <returns>Its state.</returns>
    DirtsideUnitState State(UnitId unit);

    /// <summary>
    /// The elements still in a unit.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <returns>Every element that could still choose to do something.</returns>
    /// <remarks>
    /// Live, and therefore excluding whatever has been destroyed. An activation is finished when
    /// every element in here has chosen, so a list that still held the dead would leave a platoon
    /// unable to end its turn.
    /// </remarks>
    IReadOnlyCollection<ElementId> Elements(UnitId unit);

    /// <summary>
    /// Whether this element's weapon is on a fixed mount rather than a traverse.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <param name="element">The element.</param>
    /// <param name="weapon">The caller's own name for the weapon system.</param>
    /// <returns>True when the weapon can only be aimed by pointing the whole vehicle.</returns>
    bool IsFixedMount(UnitId unit, ElementId element, string weapon);

    /// <summary>
    /// Where a moving element can be caught by opportunity fire, or null when nothing can reach it.
    /// </summary>
    /// <param name="mover">The unit moving.</param>
    /// <param name="element">The element moving.</param>
    /// <returns>The opening, or null.</returns>
    InterruptOpening? OpportunityFireOpening(UnitId mover, ElementId element);

    /// <summary>
    /// Where the shot this element just took can be intercepted, or null when it cannot be.
    /// </summary>
    /// <param name="firer">The unit firing.</param>
    /// <param name="element">The element firing.</param>
    /// <param name="weapon">The caller's own name for the weapon system.</param>
    /// <returns>The opening, or null.</returns>
    /// <remarks>
    /// Null for anything that is not interceptable, which is why the weapon is passed rather than
    /// asked about separately: only the caller's own record card knows which of its systems throws
    /// something an area-defence gun can shoot down.
    /// </remarks>
    InterruptOpening? InterceptionOpening(UnitId firer, ElementId element, string weapon);
}

/// <summary>
/// A straightforward board the caller can keep and update as the game goes on.
/// </summary>
/// <remarks>
/// Deliberately mutable, unlike everything in the sequence layer. The session is a value because a
/// suspended activation has to survive a restore; the board is the caller's live world model, and
/// pretending otherwise would mean rebuilding the policy after every chit drawn.
/// </remarks>
public sealed class DirtsideBoard : IDirtsideBoard
{
    private readonly Dictionary<UnitId, DirtsideUnitState> states = [];
    private readonly Dictionary<UnitId, List<ElementId>> elements = [];
    private readonly HashSet<(UnitId Unit, ElementId Element, string Weapon)> fixedMounts = [];
    private readonly Dictionary<(UnitId Unit, ElementId Element), InterruptOpening> moveOpenings = [];
    private readonly Dictionary<(UnitId Unit, ElementId Element, string Weapon), InterruptOpening> shotOpenings = [];

    /// <summary>The state of one unit, defaulting to a fresh one nobody has told us about.</summary>
    /// <param name="unit">The unit to look up.</param>
    /// <returns>Its state.</returns>
    public DirtsideUnitState State(UnitId unit) =>
        states.TryGetValue(unit, out var state) ? state : new DirtsideUnitState();

    /// <summary>The elements still in a unit.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns>Its elements, or none when the unit is unknown.</returns>
    public IReadOnlyCollection<ElementId> Elements(UnitId unit) =>
        elements.TryGetValue(unit, out var list) ? list : [];

    /// <summary>Records or replaces a unit's state.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="state">Its new state.</param>
    /// <returns>This board, so setup reads as a chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public DirtsideBoard Set(UnitId unit, DirtsideUnitState state)
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
    public DirtsideBoard Update(UnitId unit, Func<DirtsideUnitState, DirtsideUnitState> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Set(unit, edit(State(unit)));
    }

    /// <summary>Says which elements a unit is made of.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="unitElements">Its elements.</param>
    /// <returns>This board.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitElements"/> is null.</exception>
    public DirtsideBoard SetElements(UnitId unit, IEnumerable<ElementId> unitElements)
    {
        ArgumentNullException.ThrowIfNull(unitElements);
        elements[unit] = [.. unitElements];
        return this;
    }

    /// <summary>Says that an element's weapon is on a fixed mount.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="element">The element.</param>
    /// <param name="weapon">The caller's name for the weapon system.</param>
    /// <returns>This board.</returns>
    public DirtsideBoard SetFixedMount(UnitId unit, ElementId element, string weapon)
    {
        fixedMounts.Add((unit, element, weapon));
        return this;
    }

    /// <summary>Whether an element's weapon is fixed.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="element">The element.</param>
    /// <param name="weapon">The caller's name for the weapon system.</param>
    /// <returns>True when it can only be aimed by pointing the vehicle.</returns>
    public bool IsFixedMount(UnitId unit, ElementId element, string weapon) =>
        fixedMounts.Contains((unit, element, weapon));

    /// <summary>Says that this element's move can be caught, and where.</summary>
    /// <param name="mover">The unit moving.</param>
    /// <param name="element">The element moving.</param>
    /// <param name="opening">Where and by whom.</param>
    /// <returns>This board.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="opening"/> is null.</exception>
    public DirtsideBoard SetOpportunityFireOpening(
        UnitId mover,
        ElementId element,
        InterruptOpening opening)
    {
        ArgumentNullException.ThrowIfNull(opening);
        moveOpenings[(mover, element)] = opening;
        return this;
    }

    /// <summary>Where this element's move can be caught, or null.</summary>
    /// <param name="mover">The unit moving.</param>
    /// <param name="element">The element moving.</param>
    /// <returns>The opening, or null.</returns>
    public InterruptOpening? OpportunityFireOpening(UnitId mover, ElementId element) =>
        moveOpenings.TryGetValue((mover, element), out var opening) ? opening : null;

    /// <summary>Says that this element's shot can be intercepted, and by whom.</summary>
    /// <param name="firer">The unit firing.</param>
    /// <param name="element">The element firing.</param>
    /// <param name="weapon">The caller's name for the weapon system.</param>
    /// <param name="opening">Where and by whom.</param>
    /// <returns>This board.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="opening"/> is null.</exception>
    public DirtsideBoard SetInterceptionOpening(
        UnitId firer,
        ElementId element,
        string weapon,
        InterruptOpening opening)
    {
        ArgumentNullException.ThrowIfNull(opening);
        shotOpenings[(firer, element, weapon)] = opening;
        return this;
    }

    /// <summary>Where this element's shot can be intercepted, or null.</summary>
    /// <param name="firer">The unit firing.</param>
    /// <param name="element">The element firing.</param>
    /// <param name="weapon">The caller's name for the weapon system.</param>
    /// <returns>The opening, or null.</returns>
    public InterruptOpening? InterceptionOpening(UnitId firer, ElementId element, string weapon) =>
        shotOpenings.TryGetValue((firer, element, weapon), out var opening) ? opening : null;
}
