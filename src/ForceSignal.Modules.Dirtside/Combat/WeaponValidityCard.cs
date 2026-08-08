using ForceSignal.Modules.Dirtside.Chits;

namespace ForceSignal.Modules.Dirtside.Combat;

/// <summary>
/// One weapon's chit validity across all three range bands, as it is written on the record card.
/// </summary>
/// <remarks>
/// <para>
/// Validity varies by band, and that is a trap rather than a convenience. A damaged vehicle's shot
/// resolves one band worse, and for a weapon whose valid colours narrow with range that shift
/// changes <em>which colours count</em>, not merely how good the firer's die is. Anything that reads
/// validity therefore has to read it at the band the shot is actually resolved at, and the surest
/// way to make that happen is to make the band the key rather than something the caller remembers to
/// re-check.
/// </para>
/// <para>
/// Three rows and no more. This is a card the player fills in, not a table shipped with the app -
/// the rules themselves say to record it on the vehicle, and the entries differ per weapon, per
/// armour type and per target, which is exactly the sort of thing that belongs on the user's side of
/// the line.
/// </para>
/// </remarks>
/// <param name="Close">What counts inside the close band.</param>
/// <param name="Medium">What counts at medium.</param>
/// <param name="Long">What counts out at long.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The three rows are named for the three range bands, exactly as they are printed "
        + "on the card a player is copying from. Renaming the row would put the code and the card "
        + "out of step, which is the same trade already taken for WeaponRangeBand.")]
public sealed record WeaponValidityCard(ChitValidity Close, ChitValidity Medium, ChitValidity Long)
{
    /// <summary>
    /// A card whose three rows are the same, for weapons that do not band at all.
    /// </summary>
    /// <param name="validity">The single row.</param>
    /// <returns>The card.</returns>
    /// <remarks>
    /// Some weapons have one flat effective range rather than three bands, and some gate on the
    /// target's armour type rather than on distance. Both want the same row three times, and writing
    /// it once is less error-prone than writing it three times.
    /// </remarks>
    public static WeaponValidityCard Flat(ChitValidity validity) => new(validity, validity, validity);

    /// <summary>A card for a weapon that cannot harm this target at any range.</summary>
    public static WeaponValidityCard Ineffective { get; } = Flat(ChitValidity.Ineffective);

    /// <summary>The row for one band.</summary>
    /// <param name="band">The band the shot is <em>resolved</em> at, which is not always the band it was measured at.</param>
    /// <returns>What the drawn chits are allowed to count.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The band is not one of the three.</exception>
    public ChitValidity At(WeaponRangeBand band) => band switch
    {
        WeaponRangeBand.Close => Close,
        WeaponRangeBand.Medium => Medium,
        WeaponRangeBand.Long => Long,
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, "Not one of the three range bands."),
    };
}
