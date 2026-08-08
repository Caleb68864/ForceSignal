using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.StarGrunt.Combat;

/// <summary>How much the ground is protecting a target.</summary>
public enum CoverLevel
{
    /// <summary>Out in the open with nothing to hide behind.</summary>
    None = 0,

    /// <summary>Something that breaks up a target without stopping a round.</summary>
    Soft = 1,

    /// <summary>Something a round has to get through.</summary>
    Hard = 2,
}

/// <summary>
/// What a target has going for it. Both parts shift dice the same way, which is why they are read
/// together rather than separately.
/// </summary>
/// <param name="Cover">How much cover the target is in.</param>
/// <param name="InPosition">
/// True when the target has had time to settle into its ground rather than merely being near it.
/// </param>
public readonly record struct TargetPosture(CoverLevel Cover, bool InPosition = false)
{
    /// <summary>
    /// Rungs this posture shifts a die up. It applies to the range die and the armour die alike -
    /// cover makes a target both harder to hit and harder to hurt.
    /// </summary>
    public int Shifts => (int)Cover + (InPosition ? 1 : 0);
}

/// <summary>
/// The range die the target will roll, or the reason there is no shot worth taking.
/// </summary>
/// <param name="CanFireEffectively">False when the shot is beyond effective range.</param>
/// <param name="RangeDie">The die the target rolls. Meaningless when the shot is impossible.</param>
/// <param name="BandsOut">How many range bands separate the two, rounded up.</param>
/// <param name="Reason">Why the shot is impossible, or null when it is not.</param>
public readonly record struct RangeSolution(
    bool CanFireEffectively,
    QualityDie RangeDie,
    int BandsOut,
    string? Reason);

/// <summary>
/// Turns a distance on the table into the single die the target rolls.
/// </summary>
/// <remarks>
/// Range in StarGrunt is not a to-hit penalty; it inflates the die the target throws, so a distant
/// target is harder to hit and - because the same die type divides the fire total - soaks up far
/// fewer hits from the volley that does connect. A unit's band is its own quality die measured in
/// inches, so better troops reach further as well as shooting better.
/// </remarks>
public static class RangeBands
{
    /// <summary>
    /// How wide one range band is, in inches, for small arms and infantry support weapons fired by
    /// troops of the given quality.
    /// </summary>
    /// <param name="firerQuality">The firing unit's quality die.</param>
    /// <returns>The band width in inches.</returns>
    public static int BandInches(QualityDie firerQuality) => QualityDice.Faces(firerQuality);

    /// <summary>
    /// Works out the target's range die from the distance between the two.
    /// </summary>
    /// <param name="distanceInches">Distance between firer and target, in inches.</param>
    /// <param name="firerQuality">The firing unit's quality die, which sets the band width.</param>
    /// <param name="posture">The target's cover and posture.</param>
    /// <param name="isCloseRangeWeapon">
    /// True for a weapon effective only inside one band, such as a shotgun. Cover still shifts its
    /// range die; what changes is that it simply does not reach past the first band.
    /// </param>
    /// <returns>The range die, or the reason there is no effective shot.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The distance is negative.</exception>
    public static RangeSolution Resolve(
        decimal distanceInches,
        QualityDie firerQuality,
        TargetPosture posture,
        bool isCloseRangeWeapon = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(distanceInches);

        var band = BandInches(firerQuality);
        // Anything inside the first full band is one band out, including a target at arm's length.
        var bandsOut = Math.Max(1, (int)Math.Ceiling(distanceInches / band));

        if (isCloseRangeWeapon && bandsOut > 1)
        {
            return new RangeSolution(false, QualityDie.D12, bandsOut,
                $"That weapon is only effective inside {band} inches.");
        }

        // Each band out is one rung up the ladder from D4. Cover and posture stack on top.
        var rung = bandsOut - 1 + posture.Shifts;
        if (rung >= QualityDice.Ladder.Count)
        {
            // The die would have to be better than the ladder goes, and unlike an ordinary shift
            // that is not capped - it means there is no effective fire to be had. This is also
            // where the reduced reach into cover comes from: it falls out of the shift rather than
            // being a separate rule.
            return new RangeSolution(false, QualityDie.D12, bandsOut,
                $"That is past effective range against a target in {Describe(posture)}.");
        }

        return new RangeSolution(true, QualityDice.Ladder[rung], bandsOut, null);
    }

    /// <summary>
    /// The furthest a unit of this quality can fire effectively at a target in this posture.
    /// </summary>
    /// <param name="firerQuality">The firing unit's quality die.</param>
    /// <param name="posture">The target's cover and posture.</param>
    /// <returns>Maximum effective range in inches, or zero when there is none at all.</returns>
    public static int MaxEffectiveRangeInches(QualityDie firerQuality, TargetPosture posture)
    {
        var bands = QualityDice.Ladder.Count - posture.Shifts;
        return bands <= 0 ? 0 : bands * BandInches(firerQuality);
    }

    private static string Describe(TargetPosture posture) => (posture.Cover, posture.InPosition) switch
    {
        (CoverLevel.None, false) => "the open",
        (CoverLevel.None, true) => "the open and dug in",
        (CoverLevel.Soft, false) => "soft cover",
        (CoverLevel.Soft, true) => "soft cover and dug in",
        (CoverLevel.Hard, false) => "hard cover",
        _ => "hard cover and dug in",
    };
}
