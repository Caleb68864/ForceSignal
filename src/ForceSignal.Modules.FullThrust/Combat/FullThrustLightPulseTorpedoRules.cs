using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Combat;

/// <summary>
/// Pulse torpedo launchers, the other weapon in the light rules set. A launcher fires once per turn
/// in two steps: roll to hit against a number that worsens with every 6mu of range, then, on a hit,
/// roll a die whose face is the damage inflicted. Screens do not reduce it.
/// </summary>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
public sealed class FullThrustLightPulseTorpedoRules(Func<int>? rollDie = null) : IFiringResolver
{
    /// <summary>Longest reach of a pulse torpedo, in mu.</summary>
    public const int MaximumRange = 30;

    /// <summary>Width of each to-hit band, in mu.</summary>
    public const int BandWidth = 6;

    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <summary>
    /// The die roll a torpedo needs at this range: 2 or better inside 6mu, worsening by one for
    /// every further 6mu, so 30mu needs a 6. Band edges belong to the nearer band.
    /// </summary>
    public static int ToHitNumber(int range) =>
        Math.Clamp(2 + ((Math.Max(1, range) - 1) / BandWidth), 2, 6);

    /// <inheritdoc />
    public FiringValidationResult Validate(FiringSolution solution)
    {
        if (solution.Weapon.MaxRange <= 0)
        {
            return FiringValidationResult.Failure("Weapon range must be greater than zero.");
        }

        if (solution.Range <= 0)
        {
            return FiringValidationResult.Failure("Firing range must be greater than zero.");
        }

        var reach = Math.Min(solution.Weapon.MaxRange, MaximumRange);
        if (solution.Range > reach)
        {
            return FiringValidationResult.Failure($"{solution.Weapon.Name} is out of range.");
        }

        // The aft blind spot and mount bearing apply to torpedoes exactly as they do to beams.
        if (!FiringArcs.CanFireThrough(solution.TargetArc))
        {
            return FiringValidationResult.Failure("No weapon may fire through the aft arc.");
        }

        if (!solution.Weapon.Arcs.Contains(solution.TargetArc))
        {
            return FiringValidationResult.Failure(
                $"{solution.Weapon.Name} does not bear through the {FiringArcs.Describe(solution.TargetArc)} arc.");
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

        var toHit = ToHitNumber(solution.Range);
        var toHitRoll = Math.Clamp(_rollDie(), 1, 6);
        if (toHitRoll < toHit)
        {
            return new FiringResult(1, 0, 0, 0, 0, [toHitRoll], toHit, false);
        }

        // A hit rolls one die and takes its face as damage. Screens do not degrade a torpedo, so
        // there is nothing to deduct.
        var damageRoll = Math.Clamp(_rollDie(), 1, 6);
        return new FiringResult(1, 0, 0, 0, damageRoll, [toHitRoll, damageRoll], toHit, true);
    }
}
