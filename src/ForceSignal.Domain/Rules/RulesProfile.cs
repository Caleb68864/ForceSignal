namespace ForceSignal.Domain.Rules;

/// <summary>
/// Which layer of the rules a match is being played under. The layers are not variants of one game:
/// each replaces parts of the one before, and mixing them breaks the balance they were costed against.
/// </summary>
public enum RulesLayer
{
    /// <summary>
    /// The light cinematic set and the second edition it is a subset of. ForceSignal's default.
    /// </summary>
    LightCinematic,

    /// <summary>
    /// The Fleet Book design system, which replaces parts of the second edition: no level-three
    /// screens, longer fighter moves, and a longer-reaching needle beam.
    /// </summary>
    FleetBook
}

/// <summary>
/// How a hull's damage track is divided into rows, which decides how many threshold checks a ship
/// faces on its way to being destroyed.
/// </summary>
public enum ThresholdRowMode
{
    /// <summary>
    /// Four rows for every hull, whatever its size. Both layers ForceSignal ships use this.
    /// </summary>
    FourRows,

    /// <summary>
    /// Rows by size band: an escort two, a cruiser three, a capital four. This belongs to the
    /// second edition, which ForceSignal has no profile for - see <see cref="RulesProfile"/>.
    /// </summary>
    ByShipClass
}

/// <summary>
/// The numbers that differ between rules layers, gathered in one place so a match can be played under
/// one layer without the differences being scattered as hard-coded constants.
/// </summary>
/// <param name="Layer">The layer this profile describes.</param>
/// <param name="MaxScreenLevel">Highest screen level a ship may carry.</param>
/// <param name="FighterMoveAllowance">How far a fighter group flies in a turn, in mu.</param>
/// <param name="NeedleBeamRange">How far a needle beam reaches, in mu.</param>
public sealed record RulesProfile(
    RulesLayer Layer,
    int MaxScreenLevel,
    int FighterMoveAllowance,
    int NeedleBeamRange)
{
    /// <summary>
    /// How the hull's damage track is divided into rows.
    /// </summary>
    /// <remarks>
    /// Both shipped layers divide every hull into four rows, so this setting does not currently
    /// vary - and that is correct, not an oversight. Four rows is the Fleet Book rule; rows by
    /// class band (escort two, cruiser three, capital four) is the *second edition* rule, and
    /// ForceSignal has no second-edition profile to hang it on. The light cinematic profile covers
    /// both the light set and the second edition it subsets, and deliberately keeps four rows so a
    /// small hull stays under threshold pressure instead of dying with its systems intact. The
    /// setting exists so that adding a second-edition layer is a matter of choosing
    /// <see cref="ThresholdRowMode.ByShipClass"/> here rather than reopening the damage code.
    /// </remarks>
    public ThresholdRowMode ThresholdRows { get; init; } = ThresholdRowMode.FourRows;

    /// <summary>
    /// True when a needle beam also puts a point of damage into the hull - on a 5 as well as on a
    /// 6 - and that point ignores armour as well as screens.
    /// </summary>
    public bool EnhancedNeedleBeams { get; init; }

    /// <summary>
    /// True when a ship launches one group per operational fighter bay and recovers half its bays,
    /// rather than the two-groups-for-a-carrier-and-one-for-anything-else cap.
    /// </summary>
    public bool CarrierRatesFollowBays { get; init; }

    /// <summary>
    /// True when a recovered fighter group rolls for turnaround before it may be launched again.
    /// </summary>
    public bool CarrierTurnaroundRoll { get; init; }

    /// <summary>The light cinematic profile: level-three screens, 12mu fighter moves, 9mu needles.</summary>
    public static RulesProfile LightCinematic { get; } = new(RulesLayer.LightCinematic, 3, 12, 9);

    /// <summary>
    /// The Fleet Book profile: screens stop at level two, groups fly 24mu, the enhanced needle
    /// reaches 12mu and draws blood, and flight operations run at one group per bay.
    /// </summary>
    public static RulesProfile FleetBook { get; } = new(RulesLayer.FleetBook, 2, 24, 12)
    {
        EnhancedNeedleBeams = true,
        CarrierRatesFollowBays = true,
        CarrierTurnaroundRoll = true,
    };

    /// <summary>The profile for a layer.</summary>
    public static RulesProfile For(RulesLayer layer) =>
        layer == RulesLayer.FleetBook ? FleetBook : LightCinematic;

    /// <summary>
    /// Reads a stored or requested layer name, falling back to the light cinematic default rather than
    /// failing: an unknown name in an old snapshot should not stop a match being restored.
    /// </summary>
    public static RulesProfile Parse(string? name) =>
        name?.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant() switch
        {
            "fleetbook" or "fleetbook1" or "fb1" or "fb" => FleetBook,
            _ => LightCinematic,
        };
}
