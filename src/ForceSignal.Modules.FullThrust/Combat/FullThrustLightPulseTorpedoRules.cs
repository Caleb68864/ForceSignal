using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Combat;

/// <summary>
/// Pulse torpedo launchers. A launcher fires once per turn in two steps: roll to hit against a
/// number that worsens with range, then, on a hit, roll a die whose face is the damage inflicted.
/// Screens do not reduce it.
/// </summary>
/// <remarks>
/// The reach, the width of a band and the number needed in the closest one are the player's. What
/// lives here is the ladder itself - the to-hit number worsening by one per band, band edges
/// belonging to the nearer band, and the damage die read at face value with nothing deducted.
/// </remarks>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
public sealed class FullThrustLightPulseTorpedoRules(Func<int>? rollDie = null) : IFiringResolver
{
    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <summary>
    /// The die roll a torpedo needs at this range: the profile's best number in the closest band,
    /// worsening by one per further band, and never worse than the die can roll.
    /// </summary>
    /// <param name="range">Distance to the target, in mu.</param>
    /// <param name="rules">The profile being played against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public static int ToHitNumber(int range, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var band = Math.Max(1, rules.TorpedoBandWidth);
        var best = Math.Max(1, rules.TorpedoBestToHit);
        return Math.Clamp(best + ((Math.Max(1, range) - 1) / band), best, Math.Max(best, rules.DieFaces));
    }

    /// <summary>How far a torpedo reaches: the mount's own range, held to the profile's limit.</summary>
    /// <param name="weapon">The mount being fired.</param>
    /// <param name="rules">The profile being played against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public static int ReachFor(WeaponAttackProfile weapon, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules.TorpedoMaximumRange > 0
            ? Math.Min(weapon.MaxRange, rules.TorpedoMaximumRange)
            : weapon.MaxRange;
    }

    /// <inheritdoc />
    public FiringValidationResult Validate(FiringSolution solution, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (solution.Weapon.MaxRange <= 0)
        {
            return FiringValidationResult.Failure("Weapon range must be greater than zero.");
        }

        if (solution.Range <= 0)
        {
            return FiringValidationResult.Failure("Firing range must be greater than zero.");
        }

        if (solution.Range > ReachFor(solution.Weapon, rules))
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
    public FiringResult Resolve(FiringSolution solution, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var validation = Validate(solution, rules);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        }

        var faces = Math.Max(1, rules.DieFaces);
        var toHit = ToHitNumber(solution.Range, rules);
        var toHitRoll = Math.Clamp(_rollDie(), 1, faces);
        if (toHitRoll < toHit)
        {
            return new FiringResult(1, 0, 0, 0, 0, [toHitRoll], toHit, false);
        }

        // A hit rolls one die and takes its face as damage. Screens do not degrade a torpedo, so
        // there is nothing to deduct.
        var damageRoll = Math.Clamp(_rollDie(), 1, faces);
        return new FiringResult(1, 0, 0, 0, damageRoll, [toHitRoll, damageRoll], toHit, true);
    }
}
