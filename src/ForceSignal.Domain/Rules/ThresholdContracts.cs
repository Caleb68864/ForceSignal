namespace ForceSignal.Domain.Rules;

/// <summary>
/// The size band a hull sits in. Full Thrust groups warships into escorts, cruisers and capital
/// ships, and some rules layers size the damage track from that band rather than from the hull.
/// </summary>
public enum ShipClassBand
{
    /// <summary>Couriers, scouts, corvettes, frigates and destroyers.</summary>
    Escort,

    /// <summary>Light, escort and heavy cruisers.</summary>
    Cruiser,

    /// <summary>Battlecruisers and up, and the carriers built on those hulls.</summary>
    Capital
}

/// <summary>Working out which size band a hull belongs to.</summary>
public static class ShipClassBands
{
    /// <summary>
    /// The band a ship's map icon puts it in, or null when the icon says nothing about size.
    /// </summary>
    /// <remarks>
    /// This is an inference, not a lookup, and it is worth being plain about what it gets wrong.
    /// A ship's class in ForceSignal is free text - a player types "Warsaw class Destroyer" or
    /// "Bonaventure" or nothing at all - so the only normalized size signal on a ship is the icon
    /// key chosen for the map. That key is a picture, and a picture is a rough guide:
    /// <list type="bullet">
    /// <item>An unrecognised class falls back to the cruiser icon, so an unnamed hull silently
    /// reads as a cruiser rather than as "unknown".</item>
    /// <item>An "Escort Cruiser" is a cruiser in the rules but matches "escort" first, and a
    /// "Battlecruiser" is a capital ship but matches nothing and falls back to cruiser.</item>
    /// <item>Every carrier shares one icon, from an escort carrier to a fleet carrier.</item>
    /// <item>A station has no size band at all, and a fighter group is not a hull.</item>
    /// </list>
    /// Hull boxes would be a more honest signal, except that the two design systems convert MASS
    /// to hull boxes differently, so a box count does not map back to a class across layers. Any
    /// layer that sizes the track by class should therefore let the player set the band outright
    /// and treat this only as the opening guess.
    /// </remarks>
    public static ShipClassBand? FromIconKey(string? iconKey) => iconKey switch
    {
        "escort" or "frigate" or "destroyer" => ShipClassBand.Escort,
        "cruiser" => ShipClassBand.Cruiser,
        "dreadnought" or "carrier" => ShipClassBand.Capital,
        _ => null,
    };
}

/// <summary>A kind of system icon that a threshold check can knock out.</summary>
public enum ShipSystemKind
{
    /// <summary>The ship's drives. The first loss halves thrust; the second kills it outright.</summary>
    Drive,

    /// <summary>A fire control system. Losing every one stops the ship firing.</summary>
    FireControl,

    /// <summary>One level of defensive screens. Each level is its own generator.</summary>
    Screen,

    /// <summary>One weapon mount.</summary>
    Weapon,

    /// <summary>
    /// One fighter bay. Losing it costs the carrier a group's worth of capacity, and any group
    /// still aboard goes with it.
    /// </summary>
    FighterBay,

    /// <summary>
    /// A damage control party. Losing one costs the ship a repair crew for good: a dead party is not
    /// something another party can fix.
    /// </summary>
    DamageControlParty
}

/// <summary>One surviving system icon that rolls when a hull row is completed.</summary>
/// <param name="Kind">What sort of system this is.</param>
/// <param name="Name">Name to show in the battle log.</param>
/// <param name="WeaponId">The mount this row refers to, for weapons only.</param>
public sealed record ShipSystem(ShipSystemKind Kind, string Name, Guid? WeaponId = null);

/// <summary>
/// A threshold check to resolve: the deepest hull row completed, how many extra rows the same
/// attack tore through, and every system still standing.
/// </summary>
/// <param name="Threshold">Deepest row completed, 1 through 3.</param>
/// <param name="ExtraThresholds">Further rows completed by the same attack beyond the first.</param>
/// <param name="Systems">Systems that are still working and therefore roll.</param>
public sealed record ThresholdCheck(int Threshold, int ExtraThresholds, IReadOnlyList<ShipSystem> Systems);

/// <summary>One system's die roll during a threshold check.</summary>
public sealed record ThresholdRoll(ShipSystem System, int Die, bool IsLost);

/// <summary>The outcome of a threshold check.</summary>
/// <param name="Threshold">Deepest row completed.</param>
/// <param name="LostOn">A system is knocked out on this number or lower.</param>
/// <param name="Rolls">Every system's roll, in order, so the table can audit the check.</param>
public sealed record ThresholdCheckResult(int Threshold, int LostOn, IReadOnlyList<ThresholdRoll> Rolls)
{
    /// <summary>Systems knocked out by this check.</summary>
    public IEnumerable<ShipSystem> Lost => Rolls.Where(roll => roll.IsLost).Select(roll => roll.System);
}

/// <summary>Splits a hull into rows and resolves threshold checks for a rules profile.</summary>
/// <remarks>
/// The layer arrives as an argument rather than as constructor state on purpose. The resolvers are
/// built once for the whole service, while the rules layer is per-match state that the owner can
/// still change during fleet setup, so a resolver that captured a profile would be answering for
/// whichever match happened to build it. Passing the profile in makes a resolver's answer a
/// function of (layer, question), which is exactly what "a match is played under one layer" means.
/// The profile is optional and falls back to the light cinematic default, matching
/// <see cref="RulesProfile.Empty"/>, so a caller that has no layer to hand still gets the default
/// rather than a null reference.
/// </remarks>
public interface IThresholdResolver
{
    /// <summary>
    /// The hull's damage track, as the number of boxes in each row from the top down.
    /// </summary>
    /// <param name="hullMax">Hull boxes the ship was built with.</param>
    /// <param name="rules">The layer being played, or null for the light cinematic default.</param>
    /// <param name="band">The hull's size band, where the layer sizes the track by class.</param>
    IReadOnlyList<int> HullRows(int hullMax, RulesProfile rules, ShipClassBand? band = null);

    /// <summary>How many hull rows are fully crossed off at this much damage.</summary>
    /// <param name="hullDamage">Damage recorded against the hull.</param>
    /// <param name="hullMax">Hull boxes the ship was built with.</param>
    /// <param name="rules">The layer being played, or null for the light cinematic default.</param>
    /// <param name="band">The hull's size band, where the layer sizes the track by class.</param>
    int RowsCompleted(int hullDamage, int hullMax, RulesProfile rules, ShipClassBand? band = null);

    /// <summary>Rolls one die per surviving system and reports what was knocked out.</summary>
    ThresholdCheckResult Resolve(ThresholdCheck check, RulesProfile rules);
}
