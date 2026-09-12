using System.Collections.Immutable;
using ForceSignal.Modules.Dirtside.Sequence;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Game;

/// <summary>
/// A whole Dirtside game as a value: who is on the table, what has happened to them, and where the
/// turn has got to.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the caller <see cref="IDirtsideBoard"/> was written for.</b> That interface exists so
/// the activation rules can read the world without owning it - "the caller keeps it because the
/// caller is the one drawing chits and moving models" - and until now there was no caller. Dirtside
/// had a complete engine and no way to reach it: one status endpoint replying "in-development", and
/// a hundred and sixty tests covering code no player could run. The game implements the board
/// directly, so the policy reads the same roster the commands write.
/// </para>
/// <para>
/// <b>A platoon activates; its elements decide.</b> That is the shape everything here follows, and
/// it is why this is not StarGrunt with different words. There is no action budget on the unit. Each
/// element takes one move and one combat action in whichever order it likes, or neither, and the
/// activation is finished when every element still on the table has said which. A command that
/// touches an element therefore names it, and a platoon-level command that did not would have no
/// rule to apply.
/// </para>
/// <para>
/// <b>A value, not a mutable session</b>, for the reasons the sequence layer already recorded: a
/// suspended activation has to survive being serialized, and equality is then an assertion rather
/// than a bespoke comparer. The board interface it implements is documented as being read live,
/// which it still is - every command returns a new game, and the policy reads whichever one it was
/// handed.
/// </para>
/// <para>
/// Equality is structural and written by hand, because <see cref="ImmutableDictionary{TKey,TValue}"/>
/// compares by reference.
/// </para>
/// </remarks>
public sealed partial record DirtsideGame : IDirtsideBoard
{
    /// <summary>What the players are calling this game.</summary>
    public required string Name { get; init; }

    /// <summary>Every platoon on the table, by id.</summary>
    public ImmutableDictionary<UnitId, PlatoonDefinition> Units { get; init; } =
        ImmutableDictionary<UnitId, PlatoonDefinition>.Empty;

    /// <summary>What has happened to each of them.</summary>
    public ImmutableDictionary<UnitId, PlatoonStatus> Statuses { get; init; } =
        ImmutableDictionary<UnitId, PlatoonStatus>.Empty;

    /// <summary>Where the turn has got to.</summary>
    public GroundCombatSession Session { get; init; } = new();

    /// <summary>
    /// The close assault being fought, or null when there is none.
    /// </summary>
    /// <remarks>
    /// On the game rather than in the frame, because an assault is several commands long - launch,
    /// stand, a round, its aftermath, perhaps another round - and a game put down between two of
    /// them has to come back between the same two. The frame records that the committed elements
    /// spent their combat action; this records what they spent it on.
    /// </remarks>
    public DirtsideAssault? Assault { get; init; }

    /// <summary>What has happened, in the order it happened, for the table to read back.</summary>
    public ImmutableArray<string> Log { get; init; } = [];

    /// <summary>An empty game nobody has been added to yet.</summary>
    /// <param name="name">What to call it.</param>
    /// <returns>The game.</returns>
    public static DirtsideGame Create(string name) => new() { Name = name };

    /// <summary>Puts a platoon on the table at full strength.</summary>
    /// <param name="platoon">The platoon to add.</param>
    /// <returns>The game with it on.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="platoon"/> is null.</exception>
    /// <exception cref="ArgumentException">A platoon with that id is already on the table.</exception>
    public DirtsideGame WithUnit(PlatoonDefinition platoon)
    {
        ArgumentNullException.ThrowIfNull(platoon);
        if (Units.ContainsKey(platoon.Id))
        {
            throw new ArgumentException($"'{platoon.Id}' is already on the table.", nameof(platoon));
        }

        return this with
        {
            Units = Units.Add(platoon.Id, platoon),
            Statuses = Statuses.Add(platoon.Id, PlatoonStatus.ForFullStrength(platoon)),
            // The session is rebuilt rather than edited: it takes its sides from the roster, and two
            // places holding the list of who is playing is one too many.
            Session = SessionForRoster(Units.Add(platoon.Id, platoon)),
        };
    }

    /// <summary>Replaces a platoon's status.</summary>
    /// <param name="unit">The platoon.</param>
    /// <param name="edit">What to change about it.</param>
    /// <returns>The game with the change applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="edit"/> is null.</exception>
    public DirtsideGame WithStatus(UnitId unit, Func<PlatoonStatus, PlatoonStatus> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return this with { Statuses = Statuses.SetItem(unit, edit(Status(unit))) };
    }

    /// <summary>Adds a line to the log.</summary>
    /// <param name="entry">What happened.</param>
    /// <returns>The game with it recorded.</returns>
    public DirtsideGame WithLog(string entry) => this with { Log = Log.Add(entry) };

    /// <summary>True when this platoon is on the table.</summary>
    /// <param name="unit">The platoon to look for.</param>
    /// <returns>Whether it is there.</returns>
    public bool HasUnit(UnitId unit) => Units.ContainsKey(unit);

    /// <summary>A platoon's definition.</summary>
    /// <param name="unit">The platoon to look up.</param>
    /// <returns>What it is.</returns>
    /// <exception cref="ArgumentException">No such platoon is on the table.</exception>
    public PlatoonDefinition Unit(UnitId unit) =>
        Units.TryGetValue(unit, out var found)
            ? found
            : throw new ArgumentException($"There is no platoon called '{unit}' on the table.", nameof(unit));

    /// <summary>A platoon's status.</summary>
    /// <param name="unit">The platoon to look up.</param>
    /// <returns>What has happened to it.</returns>
    /// <exception cref="ArgumentException">No such platoon is on the table.</exception>
    public PlatoonStatus Status(UnitId unit) =>
        Statuses.TryGetValue(unit, out var found)
            ? found
            : throw new ArgumentException($"There is no platoon called '{unit}' on the table.", nameof(unit));

    /// <summary>The sides in play, in the order they were first seen.</summary>
    public ImmutableArray<SideId> Sides => [.. Units.Values.Select(unit => unit.Side).Distinct()];

    /// <inheritdoc />
    public DirtsideUnitState State(UnitId unit) =>
        Statuses.TryGetValue(unit, out var status) && Units.TryGetValue(unit, out var platoon)
            ? status.ToUnitState(platoon.Kind, platoon.IsCybertank)
            : new DirtsideUnitState();

    /// <inheritdoc />
    /// <remarks>
    /// Live, and therefore without the destroyed: an activation is finished when every element in
    /// here has chosen, so leaving the dead in would strand a platoon unable to end its turn. A
    /// system that is down is still listed, because it is still there and still has to say it is
    /// doing nothing.
    /// </remarks>
    public IReadOnlyCollection<ElementId> Elements(UnitId unit)
    {
        if (!Units.TryGetValue(unit, out var platoon) || platoon.Elements.IsDefaultOrEmpty)
        {
            return [];
        }

        var status = Status(unit);
        return [.. platoon.Elements.Where(element => !status.Element(element.Id).IsDestroyed).Select(element => element.Id)];
    }

    /// <inheritdoc />
    public bool IsFixedMount(UnitId unit, ElementId element, string weapon) =>
        Units.TryGetValue(unit, out var platoon)
        && platoon.Element(element)?.Weapon(weapon) is { IsFixedMount: true };

    /// <inheritdoc />
    /// <remarks>
    /// Null for now, and that is a slice boundary rather than a rule. Opportunity fire needs a way to
    /// say who can see the mover, which is tape and eyeball at a real table; until the app can ask,
    /// nothing is watching and no window opens. The policy already handles a null opening.
    /// </remarks>
    public InterruptOpening? OpportunityFireOpening(UnitId mover, ElementId element) => null;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Null for now, but no longer for the reason <see cref="OpportunityFireOpening"/> gives. Both
    /// halves this needs are here: the weapon card records which systems throw something
    /// interceptable, and <see cref="InterceptWithAreaDefence"/> knows which elements have their
    /// sensors on and whether their table has entered a reach.
    /// </para>
    /// <para>
    /// <b>What is missing is the rules, not the plumbing, and this is where that bites.</b> An open
    /// window refuses every step and refuses to let the frame close until it is answered - and
    /// <see cref="InterceptWithAreaDefence"/> cannot answer one, because nobody has written down
    /// what an interception rolls or what a success does to the shot; it refuses and says so. A
    /// route exists and reaches that refusal, which is what makes the feature findable rather than
    /// absent, but a refusal is not an answer. Returning an opening here would therefore still
    /// deadlock the game it was meant to enrich - a platoon unable to act, unable to finish, and
    /// unable to say it does not want to intercept.
    /// </para>
    /// <para>
    /// So this stops returning null on the day the procedure arrives, and not before. Adding a
    /// decline route first would produce windows that can only ever be declined, which is the shape
    /// already rejected here. Until then the rule lives on the game, where a caller can ask whether
    /// an element may intercept and be told - the half that costs a combat action - and can ask to
    /// intercept and be told exactly which four sentences are missing.
    /// </para>
    /// </remarks>
    public InterruptOpening? InterceptionOpening(UnitId firer, ElementId element, string weapon) => null;

    /// <summary>Builds the session's sides from the roster, so the two cannot disagree.</summary>
    private static GroundCombatSession SessionForRoster(ImmutableDictionary<UnitId, PlatoonDefinition> units)
    {
        var sides = units.Values
            .GroupBy(unit => unit.Side)
            .Select(side => SideState.Of(side.Key, [.. side.Select(unit => unit.Id)]))
            .ToArray();

        // The session insists on exactly two sides, because both shared guards compare you with your
        // opponent. A game still being built has fewer, and that is not yet a turn to start.
        return sides.Length == 2 ? GroundCombatSession.Start(sides[0], sides[1]) : new GroundCombatSession();
    }

    /// <inheritdoc />
    public bool Equals(DirtsideGame? other) =>
        other is not null
        && Name == other.Name
        && StructuralEquality.Map(Units, other.Units)
        && StructuralEquality.Map(Statuses, other.Statuses)
        && Session == other.Session
        && Assault == other.Assault
        && StructuralEquality.Sequence(Log, other.Log);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        Name,
        StructuralEquality.MapHash(Units),
        StructuralEquality.MapHash(Statuses),
        Session,
        Assault,
        StructuralEquality.SequenceHash(Log));
}
