using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Combat;

/// <summary>
/// Needle beams: a sniping weapon that picks off one named system rather than breaking the hull.
/// Nominate one system on the target and roll a single die - a 6 knocks that system out exactly as a
/// failed threshold check would. Screens are ignored entirely, because a needle does not score the
/// kind of damage screens degrade. Short reach and one arc are the price.
///
/// The Fleet Book's enhanced needle is the same weapon with blood on it: it reaches further, and it
/// puts a single point into the hull on a 5 as well as on a 6 - so a 5 draws blood without taking
/// the system, and a 6 does both. That point ignores armour as well as screens.
/// </summary>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
public sealed class FullThrustNeedleBeamRules(Func<int>? rollDie = null) : IFiringResolver
{
    /// <summary>
    /// Longest reach of a needle beam under the light cinematic layer, in mu. This is only the
    /// fallback for a mount that declares no range of its own; the layer's own reach wins.
    /// </summary>
    public const int MaximumRange = 9;

    /// <summary>The roll that knocks the nominated system out, under either layer.</summary>
    public const int SystemKillRoll = 6;

    /// <summary>
    /// The roll an enhanced needle needs to put its point of hull damage in. A 5 draws blood
    /// without taking the system; a 6 does both.
    /// </summary>
    public const int EnhancedHullDamageRoll = 5;

    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <summary>
    /// How far a needle reaches under a layer: the mount's own range, or the layer's reach when the
    /// mount does not declare one.
    /// </summary>
    /// <param name="weapon">The mount being fired.</param>
    /// <param name="rules">The layer being played, or null for the light cinematic default.</param>
    public static int ReachFor(WeaponAttackProfile weapon, RulesProfile? rules) =>
        weapon.MaxRange <= 0 ? (rules ?? RulesProfile.LightCinematic).NeedleBeamRange : weapon.MaxRange;

    /// <inheritdoc />
    public FiringValidationResult Validate(FiringSolution solution, RulesProfile? rules = null)
    {
        if (solution.Range <= 0)
        {
            return FiringValidationResult.Failure("Firing range must be greater than zero.");
        }

        var reach = ReachFor(solution.Weapon, rules);
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
    public FiringResult Resolve(FiringSolution solution, RulesProfile? rules = null)
    {
        var validation = Validate(solution, rules);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        }

        var roll = Math.Clamp(_rollDie(), 1, 6);
        // Under the light layer a needle either takes the system or does nothing at all. The
        // enhanced needle adds a single point of hull damage from a 5 upward, so a 5 draws blood
        // without taking the system. The kill number is reported either way, because that is still
        // what the shot was aimed at doing.
        var hullDamage = (rules ?? RulesProfile.LightCinematic).EnhancedNeedleBeams && roll >= EnhancedHullDamageRoll
            ? 1
            : 0;
        return new FiringResult(1, 0, 0, 0, hullDamage, [roll], SystemKillRoll, roll >= SystemKillRoll);
    }
}
