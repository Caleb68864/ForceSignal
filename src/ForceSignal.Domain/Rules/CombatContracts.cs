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
    public static FiringArc FromRelativeClock(double clockPoints)
    {
        var wrapped = clockPoints % 12;
        if (wrapped < 0)
        {
            wrapped += 12;
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

/// <summary>Rules profile for one weapon mount.</summary>
/// <param name="Name">Mount name as printed on the ship record.</param>
/// <param name="AttackDice">Dice the mount rolls at its closest range band.</param>
/// <param name="MaxRange">Longest range the mount can reach, in mu.</param>
/// <param name="Arcs">Arcs the mount bears through. A mount may bear through several.</param>
public sealed record WeaponAttackProfile(string Name, int AttackDice, int MaxRange, IReadOnlyList<FiringArc> Arcs);

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
public sealed record FiringResult(
    int RawDice,
    int RangePenalty,
    int ScreenReduction,
    int SystemPenalty,
    int Damage,
    IReadOnlyList<int> DiceRolls);

/// <summary>Validates and resolves firing attacks for a rules profile.</summary>
public interface IFiringResolver
{
    /// <summary>Validates a firing solution before damage resolution.</summary>
    FiringValidationResult Validate(FiringSolution solution);

    /// <summary>Resolves a valid firing solution into damage and attack modifiers.</summary>
    FiringResult Resolve(FiringSolution solution);
}
