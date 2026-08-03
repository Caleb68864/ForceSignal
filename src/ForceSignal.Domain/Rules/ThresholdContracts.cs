namespace ForceSignal.Domain.Rules;

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
    FighterBay
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
public interface IThresholdResolver
{
    /// <summary>
    /// The hull's damage track, as the number of boxes in each row from the top down.
    /// </summary>
    IReadOnlyList<int> HullRows(int hullMax);

    /// <summary>How many hull rows are fully crossed off at this much damage.</summary>
    int RowsCompleted(int hullDamage, int hullMax);

    /// <summary>Rolls one die per surviving system and reports what was knocked out.</summary>
    ThresholdCheckResult Resolve(ThresholdCheck check);
}
