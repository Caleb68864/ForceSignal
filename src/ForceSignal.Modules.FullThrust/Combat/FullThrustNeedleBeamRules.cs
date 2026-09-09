using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Combat;

/// <summary>
/// Needle beams: a sniping weapon that picks off one named system rather than breaking the hull.
/// Nominate one system on the target and roll a single die - a high enough roll knocks that system
/// out exactly as a failed threshold check would. Screens are ignored entirely, because a needle
/// does not score the kind of damage screens degrade.
/// </summary>
/// <remarks>
/// A profile may describe an enhanced needle, which is the same weapon with blood on it: it also
/// puts a single point into the hull from a slightly lower roll, so that roll draws blood without
/// taking the system and the kill roll does both. That point ignores armour as well as screens.
/// Which rolls those are, and how far the weapon reaches, are the player's.
/// </remarks>
/// <param name="rollDie">
/// Die source. Takes the number of faces and returns a face, so the die the table actually
/// plays with is the die that gets rolled - this used to be a nullary source that always
/// produced 1-6, with the profile's face count applied afterwards as a clamp, which cannot
/// produce a face above six and piles every face above the profile's onto its top one.
/// Injectable so tests and replays can be deterministic.
/// </param>
public sealed class FullThrustNeedleBeamRules(Func<int, int>? rollDie = null) : IFiringResolver
{
    private readonly Func<int, int> _rollDie = rollDie ?? (faces => Random.Shared.Next(1, faces + 1));

    /// <summary>
    /// How far a needle reaches: the mount's own range, or the profile's reach when the mount does
    /// not declare one.
    /// </summary>
    /// <param name="weapon">The mount being fired.</param>
    /// <param name="rules">The profile being played against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public static int ReachFor(WeaponAttackProfile weapon, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return weapon.MaxRange <= 0 ? rules.NeedleBeamRange : weapon.MaxRange;
    }

    /// <inheritdoc />
    public FiringValidationResult Validate(FiringSolution solution, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (solution.Range <= 0)
        {
            return FiringValidationResult.Failure("Firing range must be greater than zero.");
        }

        if (solution.Range > ReachFor(solution.Weapon, rules))
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
    public FiringResult Resolve(FiringSolution solution, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var validation = Validate(solution, rules);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        }

        var roll = Math.Clamp(_rollDie(Faces(rules)), 1, Faces(rules));

        // A plain needle either takes the system or does nothing at all. An enhanced one adds a
        // single point of hull damage from its own roll upward, so that roll draws blood without
        // taking the system. The kill number is reported either way, because that is still what the
        // shot was aimed at doing.
        var hullDamage = rules.EnhancedNeedleBeams
            && rules.NeedleHullDamageRoll > 0
            && roll >= rules.NeedleHullDamageRoll
                ? 1
                : 0;

        var kills = rules.NeedleSystemKillRoll > 0 && roll >= rules.NeedleSystemKillRoll;
        return new FiringResult(1, 0, 0, 0, hullDamage, [roll], rules.NeedleSystemKillRoll, kills);
    }

    /// <summary>Faces on the die this table plays with. Never below one, so a blank profile still rolls.</summary>
    private static int Faces(RulesProfile rules) => Math.Max(1, rules.DieFaces);
}
