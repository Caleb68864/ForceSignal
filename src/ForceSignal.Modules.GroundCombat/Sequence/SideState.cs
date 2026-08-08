using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>
/// One player's standing in the current turn.
/// </summary>
/// <remarks>
/// Every count a rule asks about is derived from the sets rather than stored beside them. Both of the
/// rules that are word-for-word identical between the two games - who chooses the first activation,
/// and when passing is legal - read a count of units, and a stored count that can disagree with the
/// set it counts is a bug factory. There is nothing here to keep in step.
/// </remarks>
public sealed record SideState
{
    /// <summary>Which side this is.</summary>
    public SideId Id { get; init; }

    /// <summary>Every unit this side still has on the table.</summary>
    public ImmutableHashSet<UnitId> OnTable { get; init; } = ImmutableHashSet<UnitId>.Empty;

    /// <summary>
    /// Units whose activation is spent for this turn - the face-down markers. Cleared wholesale when
    /// the next turn begins, which is the whole of the turn-end reset.
    /// </summary>
    public ImmutableHashSet<UnitId> Activated { get; init; } = ImmutableHashSet<UnitId>.Empty;

    /// <summary>
    /// Reactions that additionally cost this side its next go, one entry per go owed. Held as the
    /// reacting units rather than as a tally so that, here too, the number is derived from a set.
    /// </summary>
    public ImmutableArray<UnitId> ForfeitedPriority { get; init; } = ImmutableArray<UnitId>.Empty;

    /// <summary>Builds a side with everything unactivated.</summary>
    /// <param name="id">Which side this is.</param>
    /// <param name="units">The units it has on the table.</param>
    /// <returns>The side at the top of a turn.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="units"/> is null.</exception>
    public static SideState Of(SideId id, IEnumerable<UnitId> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        return new SideState { Id = id, OnTable = [.. units] };
    }

    /// <summary>Units still available to activate.</summary>
    [JsonIgnore]
    public ImmutableHashSet<UnitId> Unactivated => OnTable.Except(Activated);

    /// <summary>How many units are still available to activate.</summary>
    [JsonIgnore]
    public int UnactivatedCount => Unactivated.Count;

    /// <summary>How many units this side has on the table at all, activated or not.</summary>
    [JsonIgnore]
    public int UnitsOnTable => OnTable.Count;

    /// <summary>How many goes this side owes because of reactions that cost it priority.</summary>
    [JsonIgnore]
    public int ForfeitedSlots => ForfeitedPriority.IsDefault ? 0 : ForfeitedPriority.Length;

    /// <summary>Structural, because all three members would otherwise compare by reference.</summary>
    /// <param name="other">The side to compare with.</param>
    /// <returns>True when both sides stand in the same place.</returns>
    public bool Equals(SideState? other) =>
        other is not null
        && Id == other.Id
        && StructuralEquality.Set(OnTable, other.OnTable)
        && StructuralEquality.Set(Activated, other.Activated)
        && StructuralEquality.Sequence(ForfeitedPriority, other.ForfeitedPriority);

    /// <summary>Hashes in step with <see cref="Equals(SideState)"/>.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode() => HashCode.Combine(
        Id,
        StructuralEquality.SetHash(OnTable),
        StructuralEquality.SetHash(Activated),
        StructuralEquality.SequenceHash(ForfeitedPriority));
}
