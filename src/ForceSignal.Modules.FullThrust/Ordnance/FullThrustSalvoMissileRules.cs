using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Ordnance;

/// <summary>
/// Salvo missiles: a flight of one-turn missiles thrown at a point of aim. After the ships have
/// moved, a salvo attacks an enemy within its radius of the counter. One die says how many of the
/// flight arrived, the target's point defence shoots some of those down, and each survivor rolls a
/// die whose face is its damage. Screens do not reduce that damage at all; armour halves it.
/// </summary>
/// <remarks>
/// How many missiles are in a salvo and how close the target must be are the player's. What lives
/// here is the sequence - arrive, be intercepted, then each survivor's die read at face value.
/// </remarks>
/// <param name="rollDie">
/// Die source. Takes the number of faces and returns a face, so the die the table actually
/// plays with is the die that gets rolled - this used to be a nullary source that always
/// produced 1-6, with the profile's face count applied afterwards as a clamp, which cannot
/// produce a face above six and piles every face above the profile's onto its top one.
/// Injectable so tests and replays can be deterministic.
/// </param>
/// <param name="pointDefense">Point defence rules used for the target's interception.</param>
public sealed class FullThrustSalvoMissileRules(Func<int, int>? rollDie = null, IPointDefenseResolver? pointDefense = null)
    : ISalvoMissileResolver
{
    private readonly Func<int, int> _rollDie = rollDie ?? (faces => Random.Shared.Next(1, faces + 1));
    private readonly IPointDefenseResolver _pointDefense = pointDefense ?? new FullThrustPointDefenseRules(rollDie);

    /// <inheritdoc />
    public int SalvoSizeFor(RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules.MissilesPerSalvo;
    }

    /// <inheritdoc />
    public int AttackRadiusFor(RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules.SalvoAttackRadius;
    }

    /// <inheritdoc />
    public SalvoAttackResult Resolve(int targetPointDefenseSystems, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var faces = Math.Max(1, rules.DieFaces);
        var salvoSize = Math.Max(0, rules.MissilesPerSalvo);
        var arrivalRoll = Math.Clamp(_rollDie(faces), 1, faces);
        var arriving = Math.Min(arrivalRoll, salvoSize);
        var intercepted = _pointDefense.Resolve(targetPointDefenseSystems, arriving, rules);
        var surviving = Math.Max(0, arriving - intercepted.Kills);

        var damageRolls = new int[surviving];
        var damage = 0;
        for (var missile = 0; missile < surviving; missile++)
        {
            // Each missile's die face is its damage, with no reroll.
            var die = Math.Clamp(_rollDie(faces), 1, faces);
            damageRolls[missile] = die;
            damage += die;
        }

        return new SalvoAttackResult(
            salvoSize,
            arriving,
            arrivalRoll,
            intercepted,
            surviving,
            damageRolls,
            damage);
    }
}
