using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Combat;

/// <summary>
/// Firing profile for the light cinematic rules set. A beam rolls one die per class, loses a die
/// per full 12mu range band, and each die is downgraded by the target's screens.
/// </summary>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
public sealed class FullThrustLightFiringRules(Func<int>? rollDie = null) : IFiringResolver
{
    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <summary>
    /// Damage one beam die scores against a screen level.
    /// Unscreened 4-5 score 1 and 6 scores 2; level 1 ignores 4s; level 2 caps every hit at 1;
    /// level 3 ignores everything but a 6.
    /// </summary>
    public static int DieDamage(int die, int screenLevel) => (die, Math.Clamp(screenLevel, 0, 3)) switch
    {
        (6, 0 or 1) => 2,
        (6, _) => 1,
        (5, 0 or 1 or 2) => 1,
        (4, 0) => 1,
        _ => 0,
    };

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
        var screenLevel = Math.Clamp(solution.TargetScreenRating, 0, 3);
        var systemPenalty = Math.Clamp(solution.AttackerWeaponDamage, 0, solution.Weapon.AttackDice);
        var diceToRoll = Math.Max(0, solution.Weapon.AttackDice - rangePenalty - systemPenalty);

        var rolls = new int[diceToRoll];
        var damage = 0;
        var unscreened = 0;
        for (var index = 0; index < diceToRoll; index++)
        {
            var die = Math.Clamp(_rollDie(), 1, 6);
            rolls[index] = die;
            damage += DieDamage(die, screenLevel);
            unscreened += DieDamage(die, 0);
        }

        return new FiringResult(
            solution.Weapon.AttackDice,
            rangePenalty,
            unscreened - damage,
            systemPenalty,
            damage,
            rolls);
    }
}
