using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Combat;

/// <summary>
/// Needle beams: a sniping weapon that does no structural damage at all. Nominate one system on the
/// target and roll a single die - a 6 knocks that system out exactly as a failed threshold check
/// would, and anything less does nothing. Screens are ignored entirely, since there is no damage for
/// them to degrade. Short reach and one arc are the price.
/// </summary>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
public sealed class FullThrustNeedleBeamRules(Func<int>? rollDie = null) : IFiringResolver
{
    /// <summary>Longest reach of a needle beam, in mu.</summary>
    public const int MaximumRange = 9;

    /// <summary>The roll that knocks the nominated system out.</summary>
    public const int SystemKillRoll = 6;

    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <inheritdoc />
    public FiringValidationResult Validate(FiringSolution solution)
    {
        if (solution.Range <= 0)
        {
            return FiringValidationResult.Failure("Firing range must be greater than zero.");
        }

        var reach = Math.Min(solution.Weapon.MaxRange <= 0 ? MaximumRange : solution.Weapon.MaxRange, MaximumRange);
        if (solution.Range > reach)
        {
            return FiringValidationResult.Failure($"{solution.Weapon.Name} is out of range.");
        }

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

        var roll = Math.Clamp(_rollDie(), 1, 6);
        // No hull damage either way: a needle either takes the system or misses it.
        return new FiringResult(1, 0, 0, 0, 0, [roll], SystemKillRoll, roll >= SystemKillRoll);
    }
}
