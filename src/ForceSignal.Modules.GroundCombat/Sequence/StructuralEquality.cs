using System.Collections.Immutable;

namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>
/// Element-wise comparison and hashing for the immutable collections the session is built from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> <see cref="ImmutableArray{T}"/> and <see cref="ImmutableHashSet{T}"/>
/// compare by reference. A record whose members are those types therefore gets a compiler-generated
/// <c>Equals</c> that is reference equality in disguise, so the obvious restore-fidelity assertion -
/// serialize a session, read it back, assert the two are equal - would be guaranteed to fail rather
/// than guaranteed to pass. Left unnoticed the other way round (say, if the collections were shared
/// between the two instances) it would be a tautology instead. Either way it would not be a test.
/// </para>
/// <para>
/// <b>The choice made here</b> is to override <c>Equals</c> and <c>GetHashCode</c> on every record in
/// this namespace that holds a collection, rather than to expose a separate <c>StructuralEquals</c>
/// alongside the default one. Two equalities on one type is the worse option: <c>==</c>, dictionary
/// lookup, <c>Contains</c>, <c>Distinct</c> and every assertion helper would keep using the reference
/// one, and each of those is a place to be silently wrong. One correct equality costs a little code
/// here and nothing anywhere else.
/// </para>
/// </remarks>
internal static class StructuralEquality
{
    /// <summary>Compares two arrays element by element, order significant.</summary>
    public static bool Sequence<T>(ImmutableArray<T> left, ImmutableArray<T> right) =>
        left.IsDefault
            ? right.IsDefault
            : !right.IsDefault && System.Linq.Enumerable.SequenceEqual(left, right);

    /// <summary>Hashes an array in order.</summary>
    public static int SequenceHash<T>(ImmutableArray<T> items)
    {
        if (items.IsDefault)
        {
            return 0;
        }

        var hash = default(HashCode);
        foreach (var item in items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <summary>Compares two sets by membership.</summary>
    public static bool Set<T>(ImmutableHashSet<T>? left, ImmutableHashSet<T>? right) =>
        ReferenceEquals(left, right) || (left is not null && right is not null && left.SetEquals(right));

    /// <summary>Hashes a set without depending on enumeration order, which a set does not fix.</summary>
    public static int SetHash<T>(ImmutableHashSet<T>? items)
    {
        if (items is null)
        {
            return 0;
        }

        var hash = items.Count;
        foreach (var item in items)
        {
            hash ^= item?.GetHashCode() ?? 0;
        }

        return hash;
    }
}
