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

        var rangePenalty = Math.Max(0, (solution.Range - 1) / 6);
        var screenReduction = Math.Clamp(solution.TargetScreenRating, 0, 3);
        var systemPenalty = Math.Clamp(solution.AttackerWeaponDamage, 0, solution.Weapon.AttackDice);
        var damage = Math.Max(0, solution.Weapon.AttackDice - rangePenalty - screenReduction - systemPenalty);
        return new FiringResult(solution.Weapon.AttackDice, rangePenalty, screenReduction, systemPenalty, damage);
    }
}
