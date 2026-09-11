using System.Collections.Immutable;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.StarGrunt.Combat;

namespace ForceSignal.Application.Ground;

/// <summary>
/// Reads a StarGrunt range table off the wire, and reports one back.
/// </summary>
/// <remarks>
/// <para>
/// The range page is the players', and this is the only door it comes through. Nothing in this
/// assembly supplies one and there is no fallback: a game with no table refuses its first shot and
/// says which entry it wants, the same stance <see cref="DirtsideRulesProfileMapping"/> takes.
/// </para>
/// <para>
/// Rows rather than a fixed field per band, and nullable numbers rather than zero-as-blank. A row
/// nobody entered is absent, which is the whole mechanism that lets a shot name what it is missing;
/// and zero is a real answer for a cover shift, so it cannot also mean "not entered".
/// </para>
/// </remarks>
internal static class StarGruntRulesProfileMapping
{
    /// <summary>
    /// Rows one table may hold. Far above anything a real table has, and here only so the dictionary
    /// this builds cannot be sized by a stranger on an unauthenticated create route.
    /// </summary>
    public const int MaxRowsPerTable = 200;

    /// <summary>
    /// The most rungs one cover or posture entry may be worth.
    /// </summary>
    /// <remarks>
    /// Not a rule: a shift as long as the ladder already runs off it from any rung, so nothing above
    /// that means anything different, and a table is free to enter one. The ceiling is here so the
    /// rung arithmetic cannot overflow on a number nobody could have read off a page.
    /// </remarks>
    public const int MaxShift = 100;

    /// <summary>Reads a profile off a create request, or answers a blank one.</summary>
    /// <param name="dto">What the players sent, or null when they sent nothing.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="InvalidOperationException">An entry is not a die, a band, or a number a page could hold.</exception>
    public static StarGruntRulesProfile FromRequest(StarGruntRulesProfileDto? dto) =>
        dto is null ? StarGruntRulesProfile.Empty : FromDto(dto);

    /// <summary>Reads a profile off the wire, or off a stored row, which is the same shape.</summary>
    /// <param name="dto">The entries.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="InvalidOperationException">An entry is not a die, a band, or a number a page could hold.</exception>
    public static StarGruntRulesProfile FromDto(StarGruntRulesProfileDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new StarGruntRulesProfile(
            BandWidths(dto.BandWidths),
            RangeDice(dto.RangeDice),
            AtLeastOne(dto.EffectiveBands, "number of bands small arms reach"),
            Shift(dto.SoftCoverShift, "soft cover"),
            Shift(dto.HardCoverShift, "hard cover"),
            Shift(dto.InPositionShift, "a settled position"));
    }

    /// <summary>Reports a profile back the way it was entered.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The entries, each table in its own order rather than a dictionary's.</returns>
    public static StarGruntRulesProfileDto ToDto(StarGruntRulesProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new StarGruntRulesProfileDto(
            // Worst troops to best, and nearest band to furthest: the order a table reads its page in,
            // rather than whatever order a dictionary happens to enumerate.
            [.. profile.BandInches
                .OrderBy(row => row.Key)
                .Select(row => new StarGruntBandWidthDto((int)row.Key, row.Value))],
            [.. profile.RangeDice
                .OrderBy(row => row.Key)
                .Select(row => new StarGruntRangeDieDto(row.Key, (int)row.Value))],
            profile.EffectiveBands,
            profile.SoftCoverShift,
            profile.HardCoverShift,
            profile.InPositionShift);
    }

    /// <summary>
    /// Reads the band-width rows. A repeated quality is the last row typed, not a refusal: entering a
    /// row twice is a correction at a table, and refusing the whole profile over it would be this app
    /// being clever about a typo.
    /// </summary>
    private static ImmutableDictionary<QualityDie, int> BandWidths(IReadOnlyList<StarGruntBandWidthDto>? rows)
    {
        var table = ImmutableDictionary.CreateBuilder<QualityDie, int>();
        foreach (var row in Rows(rows, "band width"))
        {
            var quality = Die(row.QualityDie, "band-width row's quality");
            table[quality] = row.Inches > 0
                ? row.Inches
                : throw new InvalidOperationException(
                    $"A range band for {quality} troops cannot be {row.Inches} inches wide.");
        }

        return table.ToImmutable();
    }

    /// <summary>Reads the range-die rows, on the same terms as the band widths.</summary>
    private static ImmutableDictionary<int, QualityDie> RangeDice(IReadOnlyList<StarGruntRangeDieDto>? rows)
    {
        var table = ImmutableDictionary.CreateBuilder<int, QualityDie>();
        foreach (var row in Rows(rows, "range die"))
        {
            if (row.BandsOut < 1)
            {
                throw new InvalidOperationException(
                    $"A range-die row for {row.BandsOut} bands out is not a band; they count from 1.");
            }

            table[row.BandsOut] = Die(row.Die, $"range die for {row.BandsOut} bands out");
        }

        return table.ToImmutable();
    }

    /// <summary>One table's rows, refusing a table too long to be one or a row that says nothing.</summary>
    private static IEnumerable<TRow> Rows<TRow>(IReadOnlyList<TRow>? rows, string what)
        where TRow : class
    {
        if (rows is null || rows.Count == 0)
        {
            return [];
        }

        if (rows.Count > MaxRowsPerTable)
        {
            throw new InvalidOperationException(
                $"That {what} table has {rows.Count} rows, which is more than this server will hold.");
        }

        return rows.Select(row => row ?? throw new InvalidOperationException(
            $"A {what} table cannot hold a row that says nothing."));
    }

    /// <summary>Turns a face count into a rung of the ladder, refusing anything that is not one.</summary>
    private static QualityDie Die(int faces, string what) =>
        Enum.IsDefined(typeof(QualityDie), faces)
            ? (QualityDie)faces
            : throw new InvalidOperationException(
                $"A {what} of {faces} is not on the quality ladder ({string.Join(", ", StarGruntWire.Ladder)}).");

    /// <summary>A count that may be left unentered but may not be less than one.</summary>
    /// <remarks>
    /// Refused rather than clamped: a reach of no bands is not a number anybody read off a page, and
    /// clamping would turn a typo into a rule.
    /// </remarks>
    private static int? AtLeastOne(int? value, string what) =>
        value is null or >= 1 ? value : throw new InvalidOperationException($"The {what} cannot be {value}.");

    /// <summary>
    /// A shift that may be left unentered, may be zero, and may not be negative or absurd.
    /// </summary>
    private static int? Shift(int? value, string what) =>
        value is null or (>= 0 and <= MaxShift)
            ? value
            : throw new InvalidOperationException(
                $"The rungs {what} is worth cannot be {value}; enter a number from 0 to {MaxShift}.");
}
