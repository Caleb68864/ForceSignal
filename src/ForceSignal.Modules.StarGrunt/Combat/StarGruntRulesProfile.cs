using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.StarGrunt.Combat;

/// <summary>
/// Every number a StarGrunt shot reads off the rulebook's range page, supplied by the players rather
/// than shipped with this app.
/// </summary>
/// <remarks>
/// <para>
/// The same argument as <c>RulesProfile</c> and <c>DirtsideRulesProfile</c>, and the same shape: the
/// engine owns the <em>procedures</em> - distance is counted in bands, the target throws one range die,
/// cover and a settled posture move that die up, a die pushed off the top of the ladder is no
/// effective shot, and the same rungs make the target's armour harder to get through - and the players
/// own every <em>number</em> those procedures read, off their own rulebook.
/// </para>
/// <para>
/// This module used to carry that page as arithmetic on the ladder, which is why no earlier guard
/// saw it: a band was the firer's own quality die read as inches (<c>QualityDice.Faces</c>), the
/// target's die walked up the ladder one rung per band starting at the bottom, soft cover was worth
/// one rung and hard cover two because those were the enum's values, being dug in was worth one more,
/// and the reach was the length of the ladder. Every one of those is a reading off somebody's page.
/// They are all here now, together - the page is one table, and moving part of it would have left a
/// half-entered table, which this project has already established is worse than an empty one.
/// </para>
/// <para>
/// <b>A missing entry is a refusal, never a substitution.</b> Every lookup here answers null when the
/// players have not entered it, and <see cref="RangeBands.Resolve"/> turns that null into a refusal
/// that names the entry. There is deliberately no <see cref="Empty"/>-but-typical profile and no way
/// to conjure one from a name.
/// </para>
/// <para>
/// <b>Only what a shot reads is needed.</b> A table whose troops are all one quality never enters a
/// band width for the others; a firefight across open ground never asks what hard cover is worth; a
/// game that never shoots past three bands never needs the fourth row. The over-strict failure has
/// been paid for in this project already, once on a legitimate seven-field profile and once on a CI
/// job.
/// </para>
/// </remarks>
/// <param name="BandInches">
/// How wide one range band is, in inches, for troops of each quality. Keyed by the firer's quality
/// die because that is what the band belongs to, not because the width is that die's face count.
/// </param>
/// <param name="RangeDice">
/// The die a target rolls when it is this many bands out, before cover. Keyed from one, the first
/// band.
/// </param>
/// <param name="EffectiveBands">
/// How many bands out small arms still have an effective shot at a target in the open, or null when
/// the players have not said. Past it a shot is taken and achieves nothing; with it unentered, a shot
/// at a band the table has no row for is refused and names both.
/// </param>
/// <param name="SoftCoverShift">Rungs soft cover moves a die up, or null when not entered.</param>
/// <param name="HardCoverShift">Rungs hard cover moves a die up, or null when not entered.</param>
/// <param name="InPositionShift">
/// Rungs a target settled into its ground moves a die up, on top of any cover, or null when not
/// entered.
/// </param>
public sealed record StarGruntRulesProfile(
    ImmutableDictionary<QualityDie, int> BandInches,
    ImmutableDictionary<int, QualityDie> RangeDice,
    int? EffectiveBands = null,
    int? SoftCoverShift = null,
    int? HardCoverShift = null,
    int? InPositionShift = null)
{
    /// <summary>
    /// A profile with nothing entered. What a game created without one plays on, and what every
    /// stored game written before profiles existed reads back as.
    /// </summary>
    /// <remarks>
    /// Blank, not typical. A game on this profile can be set up, activated and argued over, and its
    /// first shot is refused with the name of the entry it is missing. Retiring such a game would take
    /// the table's evening; inventing a die for it would take the thing this policy protects.
    /// </remarks>
    public static StarGruntRulesProfile Empty { get; } = new(
        ImmutableDictionary<QualityDie, int>.Empty,
        ImmutableDictionary<int, QualityDie>.Empty);

    /// <summary>True when nothing whatever has been entered.</summary>
    public bool IsBlank =>
        BandInches.IsEmpty
        && RangeDice.IsEmpty
        && EffectiveBands is null
        && SoftCoverShift is null
        && HardCoverShift is null
        && InPositionShift is null;

    /// <summary>How wide a band is for troops of this quality, or null when the profile does not say.</summary>
    /// <param name="quality">The firing unit's quality die.</param>
    /// <returns>The width in inches, or null.</returns>
    public int? BandWidth(QualityDie quality) =>
        BandInches.TryGetValue(quality, out var inches) ? inches : null;

    /// <summary>The die a target this many bands out rolls before cover, or null when not entered.</summary>
    /// <param name="bandsOut">Bands between firer and target, counting from one.</param>
    /// <returns>The die, or null.</returns>
    public QualityDie? RangeDie(int bandsOut) =>
        RangeDice.TryGetValue(bandsOut, out var die) ? die : null;

    /// <summary>
    /// Rungs this cover moves a die, or null when the profile does not say.
    /// </summary>
    /// <param name="cover">The target's cover.</param>
    /// <returns>
    /// The rungs, or null. Open ground is always zero: nothing is being looked up, so there is
    /// nothing that could be missing, and that is procedure rather than a number.
    /// </returns>
    public int? CoverShift(CoverLevel cover) => cover switch
    {
        CoverLevel.None => 0,
        CoverLevel.Soft => SoftCoverShift,
        CoverLevel.Hard => HardCoverShift,
        _ => null,
    };

    /// <summary>Compares two profiles by their contents, tables included.</summary>
    /// <param name="other">The profile to compare against.</param>
    /// <remarks>
    /// <see cref="ImmutableDictionary{TKey,TValue}"/> compares by reference, so the generated
    /// equality would call two profiles built from the same numbers unequal. Dirtside's profile had
    /// to write this out by hand for the same reason.
    /// </remarks>
    public bool Equals(StarGruntRulesProfile? other) =>
        other is not null
        && EffectiveBands == other.EffectiveBands
        && SoftCoverShift == other.SoftCoverShift
        && HardCoverShift == other.HardCoverShift
        && InPositionShift == other.InPositionShift
        && Same(BandInches, other.BandInches)
        && Same(RangeDice, other.RangeDice);

    /// <summary>Hashes a profile by its contents.</summary>
    public override int GetHashCode() => HashCode.Combine(
        EffectiveBands,
        SoftCoverShift,
        HardCoverShift,
        InPositionShift,
        BandInches.Count,
        RangeDice.Count);

    private static bool Same<TKey, TValue>(
        ImmutableDictionary<TKey, TValue> left,
        ImmutableDictionary<TKey, TValue> right)
        where TKey : notnull =>
        left.Count == right.Count
        && left.All(entry => right.TryGetValue(entry.Key, out var value)
            && EqualityComparer<TValue>.Default.Equals(value, entry.Value));
}
