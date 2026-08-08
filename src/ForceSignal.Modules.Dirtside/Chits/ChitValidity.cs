namespace ForceSignal.Modules.Dirtside.Chits;

/// <summary>
/// What a weapon's chits are worth against this target at this range.
/// </summary>
/// <remarks>
/// Exactly one weapon family in the game scales chit values rather than gating colours, so this is
/// deliberately a small closed set and not an arbitrary multiplier.
/// </remarks>
public enum ChitValueScale
{
    /// <summary>Every chit counts twice.</summary>
    Doubled = 0,

    /// <summary>Every chit counts what it says.</summary>
    FaceValue = 1,

    /// <summary>Every chit counts half.</summary>
    Halved = 2,
}

/// <summary>
/// One row off the target's record card: what this weapon, at this range, against this armour, is
/// allowed to count.
/// </summary>
/// <remarks>
/// <para>
/// This is a field for the user to fill in, not a table shipped with the app. The rules themselves
/// tell players to write colour validity on the vehicle's record card, so a lookup keyed by weapon
/// type and range would be transcribing the rulebook into the source - which the content policy
/// forbids, and which would also be the wrong design, since a house rule or an errata sheet would
/// then need a code change instead of an edit to a card.
/// </para>
/// <para>
/// The four fields are independent on purpose. <see cref="IsIneffective"/> in particular is not
/// "<see cref="ValidColours"/> is empty": specials are not colour-gated, so a weapon that simply
/// cannot hurt this target would still immobilise or destroy it if the two were collapsed. They are
/// different statements and the engine treats them differently.
/// </para>
/// </remarks>
/// <param name="ValidColours">Which colours count. Everything else is drawn and thrown away.</param>
/// <param name="ValueScale">Whether the values count double, straight, or half.</param>
/// <param name="SpecialsCount">
/// Whether the special chits do anything. They always do against a vehicle and never against
/// infantry, so this is the target's answer rather than the weapon's.
/// </param>
/// <param name="IsIneffective">
/// True when this weapon cannot harm this target at all. Short-circuits before a chit leaves the
/// pot.
/// </param>
public sealed record ChitValidity(
    ChitColours ValidColours,
    ChitValueScale ValueScale = ChitValueScale.FaceValue,
    bool SpecialsCount = true,
    bool IsIneffective = false)
{
    /// <summary>
    /// How a halved value rounds.
    /// </summary>
    /// <remarks>
    /// This is a choice, not a rule. The rules say the chits count half and never say what half of
    /// an odd number is, so something had to be picked and named rather than left implicit in an
    /// integer division. Rounding towards zero is the pick: it keeps a halved draw strictly no
    /// better than a face-value one, and it leaves a chit worth 1 unable to hurt anything at long
    /// range, which is the direction the rule is plainly aiming in. Change this constant and the
    /// whole long-range profile of that weapon family moves with it.
    /// </remarks>
    public const MidpointRounding HalvedValueRounding = MidpointRounding.ToZero;

    /// <summary>A weapon that cannot touch this target at all, by any chit.</summary>
    public static ChitValidity Ineffective { get; } =
        new(ChitColours.None, SpecialsCount: false, IsIneffective: true);

    /// <summary>Whether a drawn chit counts for anything at all.</summary>
    /// <param name="chit">The chit that came out of the pot.</param>
    /// <returns>
    /// True when the chit contributes; false when it was drawn, consumed its slot, and did nothing.
    /// </returns>
    /// <remarks>
    /// A zero-valued chit is valid. It adds nothing, but it is not the same thing as an invalid
    /// chit, and the difference is worth keeping because it is the difference between a weapon that
    /// rolled badly and a weapon that is out of its depth.
    /// </remarks>
    public bool Counts(DamageChit chit) => chit.Special is not null
        ? SpecialsCount
        : chit.Colour is { } colour && ValidColours.Contains(colour);

    /// <summary>What a chit is worth once the scale is applied.</summary>
    /// <param name="chit">The chit that came out of the pot.</param>
    /// <returns>The value to add to the running total; zero for anything that does not count.</returns>
    public int ValueOf(DamageChit chit)
    {
        if (chit.IsSpecial || !Counts(chit))
        {
            return 0;
        }

        // Scaled per chit rather than on the total, because "all chits count half" is a statement
        // about chits. It matters: two chits of 1 halved per chit come to 0, halved on the total
        // come to 1.
        return ValueScale switch
        {
            ChitValueScale.Doubled => chit.Value * 2,
            ChitValueScale.Halved => (int)Math.Round(chit.Value / 2.0, HalvedValueRounding),
            _ => chit.Value,
        };
    }
}
