namespace ForceSignal.Domain.Rules;

/// <summary>
/// One of a ship's six sixty-degree firing arcs, ordered clockwise from dead ahead.
/// Arcs are measured relative to the ship, unlike the table-fixed course clock.
/// </summary>
public enum FiringArc
{
    /// <summary>Dead ahead: eleven through one o'clock.</summary>
    Fore,

    /// <summary>One through three o'clock.</summary>
    ForeStarboard,

    /// <summary>Three through five o'clock.</summary>
    AftStarboard,

    /// <summary>Dead astern: five through seven o'clock. No weapon may fire through it.</summary>
    Aft,

    /// <summary>Seven through nine o'clock.</summary>
    AftPort,

    /// <summary>Nine through eleven o'clock.</summary>
    ForePort
}

/// <summary>Arc geometry shared by the firing rules and match orchestration.</summary>
public static class FiringArcs
{
    /// <summary>Every arc, clockwise from dead ahead.</summary>
    public static IReadOnlyList<FiringArc> All { get; } =
    [
        FiringArc.Fore,
        FiringArc.ForeStarboard,
        FiringArc.AftStarboard,
        FiringArc.Aft,
        FiringArc.AftPort,
        FiringArc.ForePort
    ];

    /// <summary>
    /// The arcs a weapon may fire through. The aft arc is a blind spot: every weapon has it
    /// blacked out, though incoming fire can still arrive through it.
    /// </summary>
    public static IReadOnlyList<FiringArc> Firable { get; } =
    [
        FiringArc.Fore,
        FiringArc.ForeStarboard,
        FiringArc.AftStarboard,
        FiringArc.AftPort,
        FiringArc.ForePort
    ];

    /// <summary>True when a weapon is allowed to fire through this arc at all.</summary>
    public static bool CanFireThrough(FiringArc arc) => arc != FiringArc.Aft;

    /// <summary>Human-readable arc name, for example "fore starboard".</summary>
    public static string Describe(FiringArc arc) => arc switch
    {
        FiringArc.Fore => "fore",
        FiringArc.ForeStarboard => "fore starboard",
        FiringArc.AftStarboard => "aft starboard",
        FiringArc.Aft => "aft",
        FiringArc.AftPort => "aft port",
        FiringArc.ForePort => "fore port",
        _ => arc.ToString()
    };

    /// <summary>
    /// The arc covering a bearing given in clock points to starboard of the ship's nose, where
    /// 0 is dead ahead and 6 is dead astern. Each arc spans two clock points; a bearing exactly
    /// on a boundary reads as the more clockwise arc, matching how the arcs are named - fore is
    /// eleven through one, fore starboard is one through three.
    /// </summary>
    /// <summary>
    /// How close a bearing has to be to a boundary before it is treated as sitting on it.
    /// </summary>
    /// <remarks>
    /// Positions are measured in decimal and converted to double to take an arc tangent, so a
    /// geometry that is exactly on a 30 degree boundary can land either side of it on the last bit
    /// of the mantissa. Which side it lands on decides whether a mount bears at all. Without this,
    /// a player who drags a ship to a round-numbered position and measures a clean right angle
    /// could be refused the shot, with nothing on screen to explain why. Snapping first makes the
    /// documented rule - a boundary reads as the more clockwise arc - actually hold.
    /// </remarks>
    private const double BoundaryTolerance = 1e-9;

    public static FiringArc FromRelativeClock(double clockPoints)
    {
        if (double.IsNaN(clockPoints) || double.IsInfinity(clockPoints))
        {
            // Nothing sensible to say about a bearing that is not a number, and casting one to an
            // integer silently saturates to zero - which would read as dead ahead.
            return FiringArc.Fore;
        }

        var wrapped = clockPoints % 12;
        if (wrapped < 0)
        {
            wrapped += 12;
        }

        // Snap a bearing that is within a rounding error of a clock point onto it, so the boundary
        // rule below decides the arc rather than the last bit of a floating point division.
        var nearest = Math.Round(wrapped);
        if (Math.Abs(wrapped - nearest) < BoundaryTolerance)
        {
            wrapped = nearest;
        }

        var index = (int)Math.Floor((wrapped + 1) / 2) % 6;
        return All[index];
    }

    /// <summary>
    /// The arc a target lies in, from the firing ship's course and the offset to the target.
    /// Course 12 points up the table, so the offset uses the same screen convention as movement:
    /// x grows toward course 3 and y grows down the table.
    /// </summary>
    public static FiringArc Bearing(int shipCourse, double offsetX, double offsetY)
    {
        if (offsetX == 0 && offsetY == 0)
        {
            return FiringArc.Fore;
        }

        var bearingDegrees = Math.Atan2(offsetX, -offsetY) * 180 / Math.PI;
        var relativeDegrees = bearingDegrees - (shipCourse * 30);
        return FromRelativeClock(relativeDegrees / 30);
    }
}

/// <summary>What sort of weapon a mount is, which decides how its fire is resolved.</summary>
public enum WeaponKind
{
    /// <summary>A beam battery: one die per class, downgraded by screens.</summary>
    Beam,

    /// <summary>A pulse torpedo launcher: roll to hit by range band, then roll for damage.</summary>
    PulseTorpedo,

    /// <summary>
    /// A needle beam: nominate one system on the target and roll a single die. A 6 knocks it out and
    /// nothing else happens - no hull damage, and screens have nothing to degrade.
    /// </summary>
    NeedleBeam
}

/// <summary>Rules profile for one weapon mount.</summary>
/// <param name="Name">Mount name as printed on the ship record.</param>
/// <param name="AttackDice">Dice the mount rolls at its closest range band.</param>
/// <param name="MaxRange">Longest range the mount can reach, in mu.</param>
/// <param name="Arcs">Arcs the mount bears through. A mount may bear through several.</param>
/// <param name="Kind">What sort of weapon this is.</param>
public sealed record WeaponAttackProfile(
    string Name,
    int AttackDice,
    int MaxRange,
    IReadOnlyList<FiringArc> Arcs,
    WeaponKind Kind = WeaponKind.Beam);

/// <summary>Inputs needed to validate and resolve a firing attack.</summary>
/// <param name="Weapon">The mount being fired.</param>
/// <param name="Range">Measured range to the target, in mu.</param>
/// <param name="TargetScreenRating">Screen level protecting the target, 0 through 3.</param>
/// <param name="AttackerWeaponDamage">Weapon damage on the firing ship, which costs dice.</param>
/// <param name="TargetArc">The arc the target actually lies in, relative to the firing ship.</param>
public sealed record FiringSolution(
    WeaponAttackProfile Weapon,
    int Range,
    int TargetScreenRating,
    int AttackerWeaponDamage,
    FiringArc TargetArc = FiringArc.Fore);

/// <summary>Validation result for a firing solution.</summary>
public sealed record FiringValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    /// <summary>Reusable successful firing validation result.</summary>
    public static FiringValidationResult Success { get; } = new(true, Array.Empty<string>());

    /// <summary>Creates a failed firing validation result with one or more human-readable errors.</summary>
    public static FiringValidationResult Failure(params string[] errors) => new(false, errors);
}

/// <summary>Resolved damage and modifiers for one weapon attack.</summary>
/// <param name="RawDice">The mount's full dice count before range and system losses.</param>
/// <param name="RangePenalty">Dice lost to range bands.</param>
/// <param name="ScreenReduction">Damage points the target's screens prevented.</param>
/// <param name="SystemPenalty">Dice lost to the attacker's weapon damage.</param>
/// <param name="Damage">Damage scored after screens.</param>
/// <param name="DiceRolls">Each die actually rolled, in order, so the table can audit the shot.</param>
/// <param name="ToHitNumber">
/// For a weapon that rolls to hit, the number it needed. Null for weapons that score per die.
/// </param>
/// <param name="IsHit">Whether a to-hit roll landed. Null for weapons that score per die.</param>
public sealed record FiringResult(
    int RawDice,
    int RangePenalty,
    int ScreenReduction,
    int SystemPenalty,
    int Damage,
    IReadOnlyList<int> DiceRolls,
    int? ToHitNumber = null,
    bool? IsHit = null);

/// <summary>Validates and resolves firing attacks for a rules profile.</summary>
public interface IFiringResolver
{
    /// <summary>Validates a firing solution before damage resolution.</summary>
    FiringValidationResult Validate(FiringSolution solution);

    /// <summary>Resolves a valid firing solution into damage and attack modifiers.</summary>
    FiringResult Resolve(FiringSolution solution);
}
