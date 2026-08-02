using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Ordnance;

/// <summary>
/// Salvo missiles: six one-turn missiles thrown at a point of aim. After the ships have moved, a
/// salvo attacks an enemy within 6mu of its counter. One die says how many of the six arrived, the
/// target's point defence shoots some of those down, and each survivor rolls a die whose face is its
/// damage - so a six is six points. Screens do not reduce that damage at all; armour halves it.
/// </summary>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
/// <param name="pointDefense">Point defence rules used for the target's interception.</param>
public sealed class FullThrustSalvoMissileRules(Func<int>? rollDie = null, IPointDefenseResolver? pointDefense = null)
    : ISalvoMissileResolver
{
    /// <summary>Missiles in one salvo.</summary>
    public const int MissilesPerSalvo = 6;

    /// <summary>How close an enemy must be to the point of aim to be attacked, in mu.</summary>
    public const int SalvoAttackRadius = 6;

    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));
    private readonly IPointDefenseResolver _pointDefense = pointDefense ?? new FullThrustPointDefenseRules(rollDie);

    /// <inheritdoc />
    public int SalvoSize => MissilesPerSalvo;

    /// <inheritdoc />
    public int AttackRadius => SalvoAttackRadius;

    /// <inheritdoc />
    public SalvoAttackResult Resolve(int targetPointDefenseSystems)
    {
        var arrivalRoll = Math.Clamp(_rollDie(), 1, 6);
        var arriving = Math.Min(arrivalRoll, MissilesPerSalvo);
        var intercepted = _pointDefense.Resolve(targetPointDefenseSystems, arriving);
        var surviving = Math.Max(0, arriving - intercepted.Kills);

        var damageRolls = new int[surviving];
        var damage = 0;
        for (var missile = 0; missile < surviving; missile++)
        {
            // Each missile's die face is its damage: a six is six points, with no reroll.
            var die = Math.Clamp(_rollDie(), 1, 6);
            damageRolls[missile] = die;
            damage += die;
        }

        return new SalvoAttackResult(
            MissilesPerSalvo,
            arriving,
            arrivalRoll,
            intercepted,
            surviving,
            damageRolls,
            damage);
    }
}
