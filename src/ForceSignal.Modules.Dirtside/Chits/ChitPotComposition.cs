using System.Collections.ObjectModel;

namespace ForceSignal.Modules.Dirtside.Chits;

/// <summary>
/// Everything that is in the pot, as a flat multiset of chits.
/// </summary>
/// <remarks>
/// <para>
/// This is configuration, not a rule. Two reasons, and both matter. The published distribution is
/// not fully verified, so baking it in would ship a number nobody can check as though it were
/// settled. And the composition is the single most sensitive input in the whole damage model - every
/// probability in the game moves when it moves - so it belongs where a player can look at it and
/// replace it rather than buried in a static initialiser they have to read the source to find.
/// </para>
/// <para>
/// Flat rather than "counts per kind" because a draw is a sequence of individual chits, and holding
/// it flat means the pot cannot accidentally draw the same physical chit twice.
/// </para>
/// </remarks>
public sealed class ChitPotComposition
{
    /// <summary>
    /// Values 0 to 3 are handed out round-robin in this order when a colour's count does not divide
    /// evenly by four. The order is 1, 2, 0, 3 so that the leftovers pair off around the middle and
    /// the colour's mean stays as close to the centre as the count allows - the colours are supposed
    /// to be identical in severity, and a careless remainder would quietly break that.
    /// </summary>
    private static readonly int[] RemainderOrder = [1, 2, 0, 3];

    private readonly Dictionary<DamageChit, int> _counts;

    private ChitPotComposition(IReadOnlyList<DamageChit> chits)
    {
        Chits = chits;
        _counts = chits.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// A documented starting point, not an authority, and on its way out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Kept for one release only.</b> The composition is now the players': it arrives on the
    /// create request, is carried with the game and survives a restart. This exists so that a game
    /// stored before that change still opens, and so that a table part-way through one is not thrown
    /// off it. Everything above the module that falls back to it says out loud that it has - the
    /// readiness warning names the guess, and every snapshot carries the flag - because a guess that
    /// is not labelled is indistinguishable from an answer. It is removed next release.
    /// </para>
    /// <para>
    /// One hundred numerical chits, half of them red and the other half split between yellow and
    /// green, each colour carrying values zero to three spread as evenly as its count permits. That
    /// much follows the counter sheet.
    /// </para>
    /// <para>
    /// The special counts do not. The published sheet says only that there are Boom, Mobility and
    /// Systems Down chits, and fewer of the firer's Systems Down than the target's. The numbers here
    /// honour that ordering and nothing more; a caller who has counted their own sheet should
    /// replace this outright rather than trust it.
    /// </para>
    /// </remarks>
    public static ChitPotComposition Default { get; } = BuildDefault();

    /// <summary>Every chit in the pot, one entry per physical counter.</summary>
    public IReadOnlyList<DamageChit> Chits { get; }

    /// <summary>How many chits are in the pot altogether.</summary>
    public int Count => Chits.Count;

    /// <summary>How many of one particular chit the pot holds.</summary>
    /// <param name="chit">The chit to count.</param>
    /// <returns>The number of copies in the pot, possibly zero.</returns>
    public int CountOf(DamageChit chit) => _counts.GetValueOrDefault(chit);

    /// <summary>Builds a composition from an explicit list of chits.</summary>
    /// <param name="chits">One entry per physical counter; repeats are the point.</param>
    /// <returns>The composition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chits"/> is null.</exception>
    /// <exception cref="ArgumentException">The pot is empty.</exception>
    public static ChitPotComposition Of(IEnumerable<DamageChit> chits)
    {
        ArgumentNullException.ThrowIfNull(chits);
        var list = new ReadOnlyCollection<DamageChit>([.. chits]);
        if (list.Count == 0)
        {
            throw new ArgumentException("A pot with nothing in it cannot resolve damage.", nameof(chits));
        }

        return new ChitPotComposition(list);
    }

    /// <summary>Builds a composition from a count per distinct chit.</summary>
    /// <param name="counts">How many of each chit the pot holds.</param>
    /// <returns>The composition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="counts"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count is negative.</exception>
    public static ChitPotComposition FromCounts(IReadOnlyDictionary<DamageChit, int> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var chits = new List<DamageChit>();
        foreach (var (chit, count) in counts)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(counts));
            chits.AddRange(Enumerable.Repeat(chit, count));
        }

        return Of(chits);
    }

    /// <summary>
    /// A copy of this composition with the numerical chits kept and the specials replaced.
    /// </summary>
    /// <param name="specials">How many of each special the pot holds.</param>
    /// <returns>The new composition.</returns>
    /// <remarks>
    /// The specials are the part of the default that is guesswork, so they are the part a caller is
    /// most likely to want to correct on its own without re-entering a hundred numbered chits.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="specials"/> is null.</exception>
    public ChitPotComposition WithSpecials(IReadOnlyDictionary<ChitSpecial, int> specials)
    {
        ArgumentNullException.ThrowIfNull(specials);

        var chits = Chits.Where(c => !c.IsSpecial).ToList();
        foreach (var (special, count) in specials)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(specials));
            chits.AddRange(Enumerable.Repeat(DamageChit.Of(special), count));
        }

        return Of(chits);
    }

    private static ChitPotComposition BuildDefault()
    {
        var chits = new List<DamageChit>();
        chits.AddRange(NumericalRun(ChitColour.Red, 50));
        chits.AddRange(NumericalRun(ChitColour.Yellow, 25));
        chits.AddRange(NumericalRun(ChitColour.Green, 25));

        chits.AddRange(Enumerable.Repeat(DamageChit.Of(ChitSpecial.Mobility), 6));
        chits.AddRange(Enumerable.Repeat(DamageChit.Of(ChitSpecial.SystemsDownTarget), 6));
        chits.AddRange(Enumerable.Repeat(DamageChit.Of(ChitSpecial.SystemsDownFirer), 3));
        chits.AddRange(Enumerable.Repeat(DamageChit.Of(ChitSpecial.Boom), 3));

        return Of(chits);
    }

    /// <summary>Spreads one colour's chits over the values zero to three as evenly as the count allows.</summary>
    private static IEnumerable<DamageChit> NumericalRun(ChitColour colour, int count)
    {
        var perValue = new int[4];
        Array.Fill(perValue, count / 4);
        for (var i = 0; i < count % 4; i++)
        {
            perValue[RemainderOrder[i]]++;
        }

        for (var value = 0; value < perValue.Length; value++)
        {
            for (var i = 0; i < perValue[value]; i++)
            {
                yield return DamageChit.Numerical(colour, value);
            }
        }
    }
}
