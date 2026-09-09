using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Combat;

/// <summary>
/// Beam fire. A beam rolls one die per class, loses a die for every full range band between the
/// ships, and each die is read against the target's screens.
/// </summary>
/// <remarks>
/// How much a die scores, how wide a band is and how far screens degrade it are all the player's,
/// off their own profile. What lives here is the procedure: dice off for range, dice off for a
/// damaged mount, then every remaining die read twice - once through the screens and once as if
/// there were none - so the report can say what the screens were worth.
/// </remarks>
/// <param name="rollDie">
/// Die source. Takes the number of faces and returns a face, so the die the table actually
/// plays with is the die that gets rolled - this used to be a nullary source that always
/// produced 1-6, with the profile's face count applied afterwards as a clamp, which cannot
/// produce a face above six and piles every face above the profile's onto its top one.
/// Injectable so tests and replays can be deterministic.
/// </param>
public sealed class FullThrustLightFiringRules(Func<int, int>? rollDie = null) : IFiringResolver
{
    private readonly Func<int, int> _rollDie = rollDie ?? (faces => Random.Shared.Next(1, faces + 1));

    /// <inheritdoc />
    public FiringValidationResult Validate(FiringSolution solution, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

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

        // Every mount has the aft arc blacked out, so a target dead astern cannot be engaged at
        // all - not by an all-round mount, not by anything.
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

        // A beam loses one die per full band: Class N rolls N dice in the first band, N-1 in the
        // second, and so on. A range exactly on a band edge is still the nearer band, which is what
        // the -1 keeps true.
        var band = Math.Max(1, rules.BeamRangeBandWidth);
        var rangePenalty = Math.Max(0, (solution.Range - 1) / band);
        var screenLevel = Math.Clamp(solution.TargetScreenRating, 0, Math.Max(0, rules.MaxScreenLevel));
        var systemPenalty = Math.Clamp(solution.AttackerWeaponDamage, 0, solution.Weapon.AttackDice);
        var diceToRoll = Math.Max(0, solution.Weapon.AttackDice - rangePenalty - systemPenalty);
        var faces = Math.Max(1, rules.DieFaces);

        var rolls = new int[diceToRoll];
        var damage = 0;
        var unscreened = 0;
        for (var index = 0; index < diceToRoll; index++)
        {
            var die = Math.Clamp(_rollDie(faces), 1, faces);
            rolls[index] = die;
            damage += rules.BeamDamageFor(die, screenLevel);
            unscreened += rules.BeamDamageFor(die, 0);
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
