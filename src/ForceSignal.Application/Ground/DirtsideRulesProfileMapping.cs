using System.Collections.Immutable;
using System.Globalization;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Application.Ground;

/// <summary>
/// Reads a Dirtside rules profile off the wire, and reports one back.
/// </summary>
/// <remarks>
/// <para>
/// The die tables are the players', and this is the only door they come through. Nothing in this
/// assembly supplies one, and - unlike the chit pot beside it - there is no fallback at all. A game
/// with no profile refuses its first shot and says which row it wants. That asymmetry is deliberate:
/// the pot's fallback exists because a pot was once this app's to invent and is being taken back over
/// one release, whereas these tables never shipped as anything a player could have thought was
/// theirs, so there is nothing to wean anybody off.
/// </para>
/// <para>
/// Rows, keyed by a word off the record card, rather than a fixed field per level. A row nobody
/// entered is absent rather than zero, which is the whole mechanism that lets a shot name what it is
/// missing.
/// </para>
/// </remarks>
internal static class DirtsideRulesProfileMapping
{
    /// <summary>
    /// Rows one table may hold. Far above the five a real table has, and here only so the
    /// dictionary this builds cannot be sized by a stranger on an unauthenticated create route.
    /// </summary>
    public const int MaxRowsPerTable = 200;

    /// <summary>The signatures a card can carry, which is the range the engine already enforces.</summary>
    private const int MinSignature = 1;

    /// <summary>The largest signature a card can carry.</summary>
    private const int MaxSignature = 5;

    /// <summary>Reads a profile off a create request, or answers a blank one.</summary>
    /// <param name="dto">The rows the players sent, or null when they sent none.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="InvalidOperationException">A row names something that is not a die or a key.</exception>
    public static DirtsideRulesProfile FromRequest(DirtsideRulesProfileDto? dto) =>
        dto is null ? DirtsideRulesProfile.Empty : FromDto(dto);

    /// <summary>Reads a profile off the wire, or off a stored row, which is the same shape.</summary>
    /// <param name="dto">The rows.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="InvalidOperationException">A row names something that is not a die or a key.</exception>
    public static DirtsideRulesProfile FromDto(DirtsideRulesProfileDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new DirtsideRulesProfile(
            Rows(dto.FireControl, "fire control level", FireControl),
            Rows(dto.Posture, "posture", Posture),
            Rows(dto.Signature, "signature", Signature),
            Die(dto.SystemsDownRecoveryDie),
            AtLeastZero(dto.SystemsDownRecoveryRoll, "systems-down recovery roll"),
            AtLeastZero(dto.SystemsDownRecoveryRollWithBackup, "systems-down recovery roll with backup systems"));
    }

    /// <summary>Reports a profile back the way it was entered.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The rows, each table in its own order rather than a dictionary's.</returns>
    public static DirtsideRulesProfileDto ToDto(DirtsideRulesProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new DirtsideRulesProfileDto(
            // Ordered the way the wire's own vocabulary lists them, not by enum value and not by
            // whatever order the dictionary happens to enumerate in. A table reading its profile
            // back should see it in the order it reads its card.
            [.. DirtsideWire.FireControls
                .Select(name => (Key: name, Die: Lookup(profile.FireControlDice, name)))
                .Where(row => row.Die is not null)
                .Select(row => new DirtsideDieRowDto(row.Key, row.Die!.ToString()!))],
            [.. DirtsideWire.Postures
                .Select(name => (Key: name, Die: Lookup(profile.PostureDice, name)))
                .Where(row => row.Die is not null)
                .Select(row => new DirtsideDieRowDto(row.Key, row.Die!.ToString()!))],
            [.. Enumerable.Range(MinSignature, MaxSignature - MinSignature + 1)
                .Select(signature => (Key: signature, Die: profile.SignatureDie(signature)))
                .Where(row => row.Die is not null)
                .Select(row => new DirtsideDieRowDto(
                    row.Key.ToString(CultureInfo.InvariantCulture), row.Die!.ToString()!))],
            profile.SystemsDownRecoveryDie?.ToString(),
            profile.SystemsDownRecoveryRoll,
            profile.SystemsDownRecoveryRollWithBackup);
    }

    /// <summary>The die a named key carries, or null when the table has no such row.</summary>
    private static QualityDie? Lookup<TKey>(ImmutableDictionary<TKey, QualityDie> table, string name)
        where TKey : struct, Enum =>
        Enum.TryParse<TKey>(name, ignoreCase: true, out var key) && table.TryGetValue(key, out var die)
            ? die
            : null;

    /// <summary>Reads one table's rows into a dictionary, refusing anything it does not recognise.</summary>
    /// <remarks>
    /// A repeated key is the last row typed, not a refusal: entering a row twice is a correction at a
    /// table, and refusing the whole profile over it would be this app being clever about a typo.
    /// </remarks>
    private static ImmutableDictionary<TKey, QualityDie> Rows<TKey>(
        IReadOnlyList<DirtsideDieRowDto>? rows,
        string what,
        Func<string, TKey> key)
        where TKey : notnull
    {
        if (rows is null || rows.Count == 0)
        {
            return ImmutableDictionary<TKey, QualityDie>.Empty;
        }

        if (rows.Count > MaxRowsPerTable)
        {
            throw new InvalidOperationException(
                $"That {what} table has {rows.Count} rows, which is more than this server will hold.");
        }

        var table = ImmutableDictionary.CreateBuilder<TKey, QualityDie>();
        foreach (var row in rows)
        {
            if (row is null)
            {
                throw new InvalidOperationException($"A {what} table cannot hold a row that says nothing.");
            }

            table[key(row.Key)] = Die(row.Die)
                ?? throw new InvalidOperationException(
                    $"The {what} row for '{row.Key}' does not say which die it rolls.");
        }

        return table.ToImmutable();
    }

    private static FireControlLevel FireControl(string? name) =>
        Enum.TryParse<FireControlLevel>(name, ignoreCase: true, out var level) && Enum.IsDefined(level)
            ? level
            : throw new InvalidOperationException(
                $"'{name}' is not a fire control level ({string.Join(", ", DirtsideWire.FireControls)}).");

    private static DefensivePosture Posture(string? name) =>
        Enum.TryParse<DefensivePosture>(name, ignoreCase: true, out var posture)
        && Enum.IsDefined(posture)
        && posture != DefensivePosture.None
            ? posture
            : throw new InvalidOperationException(
                $"'{name}' is not a posture a die table has a row for ({string.Join(", ", DirtsideWire.Postures)}).");

    private static int Signature(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signature)
        && signature is >= MinSignature and <= MaxSignature
            ? signature
            : throw new InvalidOperationException(
                $"'{value}' is not a signature; they run {MinSignature} to {MaxSignature}.");

    private static QualityDie? Die(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
        : Enum.TryParse<QualityDie>(name, ignoreCase: true, out var die) && Enum.IsDefined(die) ? die
        : throw new InvalidOperationException(
            $"'{name}' is not a quality die ({string.Join(", ", DirtsideWire.QualityDice)}).");

    /// <summary>
    /// A number that may be left unentered but may not be nonsense.
    /// </summary>
    /// <remarks>
    /// Zero is "not entered" and is how a profile without this section reads back; a negative is a
    /// number nobody could have read off a card, so it is refused rather than clamped. Clamping would
    /// turn a typo into a rule.
    /// </remarks>
    private static int AtLeastZero(int value, string what) =>
        value >= 0 ? value : throw new InvalidOperationException($"The {what} cannot be {value}.");
}
