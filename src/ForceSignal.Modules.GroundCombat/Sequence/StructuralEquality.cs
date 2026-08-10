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
/// <para>
/// <b>Why it is public.</b> It began internal, when the only records holding collections were the
/// session's own. A game built around a session has the same problem for the same reason - it holds
/// a roster and a set of statuses and wants the same restore-fidelity assertion - so the choice was
/// between publishing this or letting each game write its own copy. A shared layer is exactly where
/// a helper both games need belongs, and a second copy of an equality is a second chance to get one
/// wrong.
/// </para>
/// </remarks>
public static class StructuralEquality
{
    /// <summary>Compares two maps key by key, order insignificant.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="left">One map.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when both hold the same keys against equal values.</returns>
    public static bool Map<TKey, TValue>(ImmutableDictionary<TKey, TValue>? left, ImmutableDictionary<TKey, TValue>? right)
        where TKey : notnull
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !EqualityComparer<TValue>.Default.Equals(value, other))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Hashes a map without depending on enumeration order, which a map does not fix.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="items">The map to hash.</param>
    /// <returns>A hash that agrees with <see cref="Map{TKey,TValue}"/>.</returns>
    public static int MapHash<TKey, TValue>(ImmutableDictionary<TKey, TValue>? items)
        where TKey : notnull
    {
        if (items is null)
        {
            return 0;
        }

        var hash = items.Count;
        foreach (var (key, value) in items)
        {
            hash ^= HashCode.Combine(key, value);
        }

        return hash;
    }

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
