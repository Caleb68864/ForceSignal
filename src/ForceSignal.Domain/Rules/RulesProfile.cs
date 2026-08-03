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
    /// <summary>The light cinematic profile: level-three screens, 12mu fighter moves, 9mu needles.</summary>
    public static RulesProfile LightCinematic { get; } = new(RulesLayer.LightCinematic, 3, 12, 9);

    /// <summary>
    /// The Fleet Book profile: screens stop at level two, groups fly 24mu, and the enhanced needle
    /// reaches 12mu. Its extra needle damage and reroll rules are not modelled yet.
    /// </summary>
    public static RulesProfile FleetBook { get; } = new(RulesLayer.FleetBook, 2, 24, 12);

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
