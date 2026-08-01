using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Combat;

/// <summary>Lightweight deterministic firing profile used for play logging and damage tracking.</summary>
public sealed class FullThrustLightFiringRules : IFiringResolver
{
    /// <inheritdoc />
    public FiringValidationResult Validate(FiringSolution solution)
    {
        if (solution.Weapon.AttackDice <= 0)
        {
            return FiringValidationResult.Failure("Weapon attack dice must be greater than zero.");
        }

        if (solution.Weapon.MaxRange <= 0)
        {
            return FiringValidationResult.Failure("Weapon range must be greater than zero.");
        }

        if (solution.Range <= 0)
        {
            return FiringValidationResult.Failure("Firing range must be greater than zero.");
        }

        if (solution.Range > solution.Weapon.MaxRange)
        {
            return FiringValidationResult.Failure($"{solution.Weapon.Name} is out of range.");
        }

        return FiringValidationResult.Success;
    }

    /// <inheritdoc />
    public FiringResult Resolve(FiringSolution solution)
    {
        var validation = Validate(solution);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        }

        // A beam loses one die per full 12mu band: Class N rolls N dice at 0-12, N-1 at 12-24,
        // N-2 at 24-36. Range 12 is still the first band, so the -1 keeps the boundary inclusive.
        var rangePenalty = Math.Max(0, (solution.Range - 1) / 12);
        var screenReduction = Math.Clamp(solution.TargetScreenRating, 0, 3);
        var systemPenalty = Math.Clamp(solution.AttackerWeaponDamage, 0, solution.Weapon.AttackDice);
        var damage = Math.Max(0, solution.Weapon.AttackDice - rangePenalty - screenReduction - systemPenalty);
        return new FiringResult(solution.Weapon.AttackDice, rangePenalty, screenReduction, systemPenalty, damage);
    }
}
