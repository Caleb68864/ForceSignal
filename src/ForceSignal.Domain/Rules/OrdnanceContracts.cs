namespace ForceSignal.Domain.Rules;

/// <summary>What a burst of point defence fire shot down.</summary>
/// <param name="Rolls">Every die rolled, including rerolls, in order.</param>
/// <param name="Kills">Fighters or missiles destroyed, never more than were incoming.</param>
/// <param name="Overkill">Kills thrown away because the threat ran out first.</param>
public sealed record PointDefenseResult(IReadOnlyList<int> Rolls, int Kills, int Overkill);

/// <summary>Close-in defensive fire against fighters and missiles.</summary>
public interface IPointDefenseResolver
{
    /// <summary>Range at which point defence can engage, in mu.</summary>
    int RangeFor(RulesProfile rules);

    /// <summary>
    /// Rolls a die per working system against an incoming threat. Allocation is declared before
    /// rolling, so anything killed beyond what was incoming is wasted rather than carried over.
    /// </summary>
    PointDefenseResult Resolve(int systems, int incoming, RulesProfile rules);
}

/// <summary>One salvo of missiles resolving against a ship.</summary>
/// <param name="MissilesLaunched">Missiles in a full salvo.</param>
/// <param name="MissilesArriving">How many got through to the point of aim.</param>
/// <param name="ArrivalRoll">The die that decided the arrivals.</param>
/// <param name="PointDefense">What the target's close-in fire shot down.</param>
/// <param name="MissilesSurviving">Missiles left to strike after interception.</param>
/// <param name="DamageRolls">A die per surviving missile; each face is its damage.</param>
/// <param name="Damage">Total damage before armour splits it.</param>
public sealed record SalvoAttackResult(
    int MissilesLaunched,
    int MissilesArriving,
    int ArrivalRoll,
    PointDefenseResult PointDefense,
    int MissilesSurviving,
    IReadOnlyList<int> DamageRolls,
    int Damage);

/// <summary>Resolves a salvo of missiles that has found a target.</summary>
public interface ISalvoMissileResolver
{
    /// <summary>Missiles in a full salvo.</summary>
    int SalvoSizeFor(RulesProfile rules);

    /// <summary>How close an enemy must be to the point of aim to be attacked, in mu.</summary>
    int AttackRadiusFor(RulesProfile rules);

    /// <summary>Rolls arrivals, lets the target's point defence answer, then rolls damage.</summary>
    SalvoAttackResult Resolve(int targetPointDefenseSystems, RulesProfile rules);
}
