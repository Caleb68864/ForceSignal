namespace ForceSignal.Domain.Rules;

/// <summary>Weapon firing arc used for attack validation and battle logging.</summary>
public enum FiringArc
{
    /// <summary>Forward firing arc.</summary>
    Fore,

    /// <summary>Rear firing arc.</summary>
    Aft,

    /// <summary>Port-side firing arc.</summary>
    Port,

    /// <summary>Starboard-side firing arc.</summary>
    Starboard,

    /// <summary>Omnidirectional firing arc.</summary>
    All
}

/// <summary>Rules profile for one weapon attack.</summary>
public sealed record WeaponAttackProfile(string Name, int AttackDice, int MaxRange, FiringArc Arc);

/// <summary>Inputs needed to validate and resolve a firing attack.</summary>
public sealed record FiringSolution(WeaponAttackProfile Weapon, int Range, int TargetScreenRating, int AttackerWeaponDamage);

/// <summary>Validation result for a firing solution.</summary>
public sealed record FiringValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    /// <summary>Reusable successful firing validation result.</summary>
    public static FiringValidationResult Success { get; } = new(true, Array.Empty<string>());

    /// <summary>Creates a failed firing validation result with one or more human-readable errors.</summary>
    public static FiringValidationResult Failure(params string[] errors) => new(false, errors);
}

/// <summary>Resolved damage and modifiers for one weapon attack.</summary>
public sealed record FiringResult(
    int RawDice,
    int RangePenalty,
    int ScreenReduction,
    int SystemPenalty,
    int Damage);

/// <summary>Validates and resolves firing attacks for a rules profile.</summary>
public interface IFiringResolver
{
    /// <summary>Validates a firing solution before damage resolution.</summary>
    FiringValidationResult Validate(FiringSolution solution);

    /// <summary>Resolves a valid firing solution into damage and attack modifiers.</summary>
    FiringResult Resolve(FiringSolution solution);
}
