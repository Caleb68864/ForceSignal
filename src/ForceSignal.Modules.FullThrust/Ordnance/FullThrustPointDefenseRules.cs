using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Ordnance;

/// <summary>
/// Point defence: short-range turrets with their own fire control, so they do not draw on the ship's
/// fire control at all. Each system rolls one die against fighters and missiles alike, and a face
/// the profile marks as chaining earns another die that scores the same way. Allocation is declared
/// before rolling, so kills past the size of the threat are wasted.
/// </summary>
/// <remarks>
/// What each face shoots down, which face chains, and how far the turrets reach are the player's.
/// What lives here is the chain and the waste.
/// </remarks>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
public sealed class FullThrustPointDefenseRules(Func<int>? rollDie = null) : IPointDefenseResolver
{
    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <summary>
    /// Longest chain one system may roll before the chain is cut.
    /// </summary>
    /// <remarks>
    /// A safety net rather than a rule, which is why it is not in the profile. A chaining face earns
    /// another die, so the chain is unbounded by the rules, and a die source that keeps returning
    /// that face never stops - which used to spin forever inside the service's lock and take every
    /// match on the server with it. A real die reaches this length about once in three billion
    /// chains, so no game will ever notice.
    /// </remarks>
    public const int MaxChainLength = 20;

    /// <summary>Most point defence systems one ship's fire is resolved with. A bound, not a rule.</summary>
    public const int MaxSystems = 32;

    /// <inheritdoc />
    public int RangeFor(RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules.PointDefenseRange;
    }

    /// <inheritdoc />
    public PointDefenseResult Resolve(int systems, int incoming, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var faces = Math.Max(1, rules.DieFaces);
        var rolls = new List<int>();
        var scored = 0;
        for (var system = 0; system < Math.Clamp(systems, 0, MaxSystems); system++)
        {
            var rolling = true;
            for (var chain = 0; rolling && chain < MaxChainLength; chain++)
            {
                var die = Math.Clamp(_rollDie(), 1, faces);
                rolls.Add(die);
                scored += rules.PointDefenseKillsFor(die);

                // A profile with no chaining face rolls one die per system and stops.
                rolling = rules.PointDefenseChainOnFace > 0 && die == rules.PointDefenseChainOnFace;
            }
        }

        var kills = Math.Min(scored, Math.Max(0, incoming));
        return new PointDefenseResult(rolls, kills, scored - kills);
    }
}
