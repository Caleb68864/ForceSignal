using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.StarGrunt.Combat;

/// <summary>How much the ground is protecting a target.</summary>
/// <remarks>
/// The values are identities and nothing else. They used to be the rungs each level was worth -
/// <c>(int)Cover</c> was the shift - which put a rules number in an enum where no guard looked. What
/// each level is worth is on <see cref="StarGruntRulesProfile"/> now.
/// </remarks>
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
/// <remarks>
/// What it is worth is not here. This carried a <c>Shifts</c> property that added the cover enum's
/// value to one for being dug in, which was the rulebook's posture table in one line of arithmetic.
/// </remarks>
public readonly record struct TargetPosture(CoverLevel Cover, bool InPosition = false);

/// <summary>
/// The range die the target will roll, or the reason there is no shot worth taking.
/// </summary>
/// <param name="CanFireEffectively">
/// False when the shot is beyond effective range, or when the profile does not carry an entry the
/// shot reads - which <paramref name="IsMissingFromProfile"/> tells apart.
/// </param>
/// <param name="RangeDie">
/// The die the target rolls, or null when the shot is impossible and there is no die.
/// <para>
/// This was not nullable, so the two refusals below had to name a die to fill the slot and both
/// named a D12. Nothing reads it on that path - <c>FireCombat</c> returns before it is touched - but
/// a rung of the quality ladder sitting in a field is a number this app chose whether anybody looks
/// at it or not, and the next reader has no way to tell a placeholder from a decision. Null says
/// what is true: past effective range there is no die to throw.
/// </para>
/// </param>
/// <param name="BandsOut">How many range bands separate the two, rounded up, or zero when unknown.</param>
/// <param name="Reason">Why the shot is impossible, or null when it is not.</param>
/// <param name="PostureShift">
/// The rungs the target's cover and posture came to, off the profile. Carried because the same rungs
/// make the target's armour harder to get through, and reading them twice is how the two would come
/// to disagree.
/// </param>
/// <param name="IsMissingFromProfile">
/// True when the reason is an entry the players have not made rather than a rule about the shot.
/// Told apart because a table reading a refusal needs to know whether to measure again or to go and
/// fill in their profile - and because a shot that is merely out of range is taken and wasted, while
/// one the game cannot settle must not cost the unit anything.
/// </param>
public readonly record struct RangeSolution(
    bool CanFireEffectively,
    QualityDie? RangeDie,
    int BandsOut,
    string? Reason,
    int PostureShift = 0,
    bool IsMissingFromProfile = false)
{
    /// <summary>A shot the rules allow and that achieves nothing, carrying no die.</summary>
    /// <param name="bandsOut">How far out the target was.</param>
    /// <param name="reason">Why there is no effective shot.</param>
    /// <returns>The solution.</returns>
    public static RangeSolution NoEffectiveShot(int bandsOut, string reason) =>
        new(false, null, bandsOut, reason);

    /// <summary>A shot that cannot be worked out because the profile lacks an entry it reads.</summary>
    /// <param name="bandsOut">How far out the target was, or zero when that is what is missing.</param>
    /// <param name="entry">The entry, phrased as the thing being looked up.</param>
    /// <returns>The refusal.</returns>
    public static RangeSolution NotOnTheProfile(int bandsOut, string entry) =>
        new(false, null, bandsOut, Missing(entry), IsMissingFromProfile: true);

    /// <summary>The refusal a missing entry produces, in the words a table needs to fix it.</summary>
    /// <remarks>
    /// It names the entry rather than saying the profile is incomplete, for the reason Dirtside's
    /// refusal does: a refusal a player cannot act on is only a slower way of stopping.
    /// </remarks>
    private static string Missing(string entry) =>
        $"This game's range table has no entry for {entry}. Enter it in the game's rules profile - "
        + "this app ships none of its own.";
}

/// <summary>
/// Turns a distance on the table into the single die the target rolls.
/// </summary>
/// <remarks>
/// <para>
/// Range in StarGrunt is not a to-hit penalty; it inflates the die the target throws, so a distant
/// target is harder to hit and - because the same die type divides the fire total - soaks up far
/// fewer hits from the volley that does connect.
/// </para>
/// <para>
/// Every number that reads off the rulebook comes from <see cref="StarGruntRulesProfile"/>: how wide
/// a band is for the firer's troops, which die a target that many bands out rolls, how far small arms
/// reach, and what cover and posture are worth. What stays here is the procedure: count the bands,
/// look up the die, move it up by the cover, and treat a die pushed off the top of the ladder as no
/// effective shot - which is also where the shorter reach into cover comes from, because it falls
/// out of the shift rather than being a rule of its own.
/// </para>
/// </remarks>
public static class RangeBands
{
    /// <summary>
    /// Works out the target's range die from the distance between the two.
    /// </summary>
    /// <param name="profile">The range table this game's players entered off their own rulebook.</param>
    /// <param name="distanceInches">Distance between firer and target, in inches.</param>
    /// <param name="firerQuality">The firing unit's quality die, whose band width the profile gives.</param>
    /// <param name="posture">The target's cover and posture.</param>
    /// <param name="isCloseRangeWeapon">
    /// True for a weapon effective only inside one band, such as a shotgun. Cover still shifts its
    /// range die; what changes is that it simply does not reach past the first band.
    /// </param>
    /// <returns>The range die, or the reason there is no effective shot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The distance is negative.</exception>
    /// <remarks>
    /// Only what this shot reads is asked for, in the order the shot reads it, so a table that has
    /// entered one band width, the rows it shoots at and the cover it fights in is never refused for
    /// the rest. A shot that is already out of reach does not go on to ask what its target's cover is
    /// worth.
    /// </remarks>
    public static RangeSolution Resolve(
        StarGruntRulesProfile profile,
        decimal distanceInches,
        QualityDie firerQuality,
        TargetPosture posture,
        bool isCloseRangeWeapon = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfNegative(distanceInches);

        if (profile.BandWidth(firerQuality) is not { } band)
        {
            return RangeSolution.NotOnTheProfile(0, $"how many inches a range band is for {firerQuality} troops");
        }

        // Anything inside the first full band is one band out, including a target at arm's length,
        // and a band's far edge belongs to it rather than to the next.
        var bands = Math.Ceiling(distanceInches / band);
        var bandsOut = bands >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)bands);

        if (isCloseRangeWeapon && bandsOut > 1)
        {
            return RangeSolution.NoEffectiveShot(bandsOut, $"That weapon is only effective inside {band} inches.");
        }

        if (profile.EffectiveBands is { } reach && bandsOut > reach)
        {
            return RangeSolution.NoEffectiveShot(
                bandsOut,
                $"That is past effective range: {bandsOut} bands out, and this game's range table gives small arms {reach}.");
        }

        if (profile.RangeDie(bandsOut) is not { } rangeDie)
        {
            // Said in full, because there are two honest answers and the refusal cannot tell which
            // one the table's rulebook gives: a die for that band, or a reach that stops short of it.
            return RangeSolution.NotOnTheProfile(
                bandsOut,
                $"the range die a target {bandsOut} band{(bandsOut == 1 ? string.Empty : "s")} out rolls"
                + (profile.EffectiveBands is null ? " (or, if your rules give no effective fire that far, how many bands small arms reach)" : string.Empty));
        }

        if (profile.CoverShift(posture.Cover) is not { } coverShift)
        {
            return RangeSolution.NotOnTheProfile(
                bandsOut, $"how many rungs {(posture.Cover == CoverLevel.Hard ? "hard" : "soft")} cover moves a die");
        }

        var postureShift = coverShift;
        if (posture.InPosition)
        {
            if (profile.InPositionShift is not { } dugIn)
            {
                return RangeSolution.NotOnTheProfile(bandsOut, "how many rungs a target settled into its position moves a die");
            }

            postureShift += dugIn;
        }

        var shifted = QualityDice.Shift(rangeDie, postureShift);
        if (shifted.Overflow > 0)
        {
            // The die would have to be better than the ladder goes, and unlike an ordinary shift that
            // is not capped - it means there is no effective fire to be had. This is also where the
            // reduced reach into cover comes from: it falls out of the shift rather than being a
            // separate rule.
            return RangeSolution.NoEffectiveShot(
                bandsOut, $"That is past effective range against a target in {Describe(posture)}.");
        }

        return new RangeSolution(true, shifted.Die, bandsOut, null, postureShift);
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
