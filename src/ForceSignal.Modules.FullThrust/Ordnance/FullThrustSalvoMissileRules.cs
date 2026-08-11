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
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
/// <param name="pointDefense">Point defence rules used for the target's interception.</param>
public sealed class FullThrustSalvoMissileRules(Func<int>? rollDie = null, IPointDefenseResolver? pointDefense = null)
    : ISalvoMissileResolver
{
    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));
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
        var arrivalRoll = Math.Clamp(_rollDie(), 1, faces);
        var arriving = Math.Min(arrivalRoll, salvoSize);
        var intercepted = _pointDefense.Resolve(targetPointDefenseSystems, arriving, rules);
        var surviving = Math.Max(0, arriving - intercepted.Kills);

        var damageRolls = new int[surviving];
        var damage = 0;
        for (var missile = 0; missile < surviving; missile++)
        {
            // Each missile's die face is its damage, with no reroll.
            var die = Math.Clamp(_rollDie(), 1, faces);
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
