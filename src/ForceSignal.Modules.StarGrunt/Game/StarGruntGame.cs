using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Game;

/// <summary>
/// A whole StarGrunt game as a value: who is on the table, what has happened to them, and where the
/// turn has got to.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the caller <see cref="IStarGruntBoard"/> was written for.</b> That interface exists so
/// the activation rules can read the world without owning it - "the caller keeps it because the
/// caller is the one rolling dice and moving models" - and until now there was no caller. The game
/// implements it directly, so the policy reads the same roster the commands write.
/// </para>
/// <para>
/// <b>A value, not a mutable session.</b> The same reasoning the sequence layer already recorded: a
/// suspended activation has to survive being serialized, and a mutable object has no natural
/// serialization boundary, so you end up hand-writing a DTO that drifts. As a record of immutable
/// collections there is nothing to drift from, restore fidelity is an equality assertion rather than
/// a bespoke comparer, and undo and replay come free.
/// </para>
/// <para>
/// It also keeps the whole game inside a class library with no dependencies, which is what would let
/// a client that is not a web browser host it in process one day.
/// </para>
/// <para>
/// Equality is structural and written by hand, because <see cref="ImmutableDictionary{TKey,TValue}"/>
/// compares by reference and a record built on one would otherwise get reference equality wearing a
/// record's clothes. See <see cref="StructuralEquality"/>.
/// </para>
/// </remarks>
public sealed record StarGruntGame : IStarGruntBoard
{
    /// <summary>What the players are calling this game.</summary>
    public required string Name { get; init; }

    /// <summary>Everyone on the table, by id.</summary>
    public ImmutableDictionary<UnitId, UnitDefinition> Units { get; init; } =
        ImmutableDictionary<UnitId, UnitDefinition>.Empty;

    /// <summary>What has happened to each of them.</summary>
    public ImmutableDictionary<UnitId, UnitStatus> Statuses { get; init; } =
        ImmutableDictionary<UnitId, UnitStatus>.Empty;

    /// <summary>Where the turn has got to.</summary>
    public GroundCombatSession Session { get; init; } = new();

    /// <summary>What has happened, in the order it happened, for the table to read back.</summary>
    public ImmutableArray<string> Log { get; init; } = [];

    /// <summary>An empty game nobody has been added to yet.</summary>
    /// <param name="name">What to call it.</param>
    /// <returns>The game.</returns>
    public static StarGruntGame Create(string name) => new() { Name = name };

    /// <summary>Puts a unit on the table at full strength.</summary>
    /// <param name="unit">The unit to add.</param>
    /// <returns>The game with it on.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unit"/> is null.</exception>
    /// <exception cref="ArgumentException">A unit with that id is already on the table.</exception>
    public StarGruntGame WithUnit(UnitDefinition unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (Units.ContainsKey(unit.Id))
        {
            throw new ArgumentException($"'{unit.Id}' is already on the table.", nameof(unit));
        }

        return this with
        {
            Units = Units.Add(unit.Id, unit),
            Statuses = Statuses.Add(unit.Id, UnitStatus.ForFullStrength(unit)),
            // The session is rebuilt rather than edited: it takes its sides from the roster, and two
            // places holding the list of who is playing is one too many.
            Session = SessionForRoster(Units.Add(unit.Id, unit)),
        };
    }

    /// <summary>Replaces a unit's status.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="edit">What to change about it.</param>
    /// <returns>The game with the change applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="edit"/> is null.</exception>
    /// <exception cref="ArgumentException">No such unit is on the table.</exception>
    public StarGruntGame WithStatus(UnitId unit, Func<UnitStatus, UnitStatus> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return this with { Statuses = Statuses.SetItem(unit, edit(Status(unit))) };
    }

    /// <summary>Adds a line to the log.</summary>
    /// <param name="entry">What happened.</param>
    /// <returns>The game with it recorded.</returns>
    public StarGruntGame WithLog(string entry) => this with { Log = Log.Add(entry) };

    /// <summary>True when this unit is on the table.</summary>
    /// <param name="unit">The unit to look for.</param>
    /// <returns>Whether it is there.</returns>
    public bool HasUnit(UnitId unit) => Units.ContainsKey(unit);

    /// <summary>A unit's definition.</summary>
    /// <param name="unit">The unit to look up.</param>
    /// <returns>What it is.</returns>
    /// <exception cref="ArgumentException">No such unit is on the table.</exception>
    public UnitDefinition Unit(UnitId unit) =>
        Units.TryGetValue(unit, out var found)
            ? found
            : throw new ArgumentException($"There is no unit called '{unit}' on the table.", nameof(unit));

    /// <summary>A unit's status.</summary>
    /// <param name="unit">The unit to look up.</param>
    /// <returns>What has happened to it.</returns>
    /// <exception cref="ArgumentException">No such unit is on the table.</exception>
    public UnitStatus Status(UnitId unit) =>
        Statuses.TryGetValue(unit, out var found)
            ? found
            : throw new ArgumentException($"There is no unit called '{unit}' on the table.", nameof(unit));

    /// <summary>The sides in play, in the order they were first seen.</summary>
    public ImmutableArray<SideId> Sides => [.. Units.Values.Select(unit => unit.Side).Distinct()];

    /// <inheritdoc />
    public StarGruntUnitState State(UnitId unit) =>
        Statuses.TryGetValue(unit, out var status)
            ? status.ToUnitState(Units[unit].Level)
            : new StarGruntUnitState();

    /// <inheritdoc />
    public IReadOnlySet<CommandLevel> CommandLevelsOnTable =>
        Units.Values.Select(unit => unit.Level).ToHashSet();

    /// <inheritdoc />
    /// <remarks>
    /// Always null for now. A dash nobody can see is unobserved, which is a legal answer, and
    /// reaction fire against a dash needs a way to say who is watching that this slice does not have.
    /// </remarks>
    public ReactionOpening? DashOpening(UnitId mover) => null;

    /// <summary>Builds the session's sides from the roster, so the two cannot disagree.</summary>
    private static GroundCombatSession SessionForRoster(ImmutableDictionary<UnitId, UnitDefinition> units)
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
    public bool Equals(StarGruntGame? other) =>
        other is not null
        && Name == other.Name
        && StructuralEquality.Map(Units, other.Units)
        && StructuralEquality.Map(Statuses, other.Statuses)
        && Session == other.Session
        && StructuralEquality.Sequence(Log, other.Log);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        Name,
        StructuralEquality.MapHash(Units),
        StructuralEquality.MapHash(Statuses),
        Session,
        StructuralEquality.SequenceHash(Log));
}
