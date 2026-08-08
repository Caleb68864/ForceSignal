using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Ordnance;

/// <summary>
/// Point defence: short-range turrets with their own fire control, so they do not draw on the ship's
/// fire control at all. Each system rolls one die against fighters and missiles alike - 1 to 3 does
/// nothing, a 4 or 5 kills one, and a 6 kills two and rolls again, chaining while the sixes last.
/// Allocation is declared before rolling, so kills past the size of the threat are wasted.
/// </summary>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
public sealed class FullThrustPointDefenseRules(Func<int>? rollDie = null) : IPointDefenseResolver
{
    /// <summary>Point defence reaches 6mu, and may fire through the aft arc.</summary>
    public const int PointDefenseRange = 6;

    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <inheritdoc />
    public int Range => PointDefenseRange;

    /// <summary>
    /// Longest chain of sixes one system may roll before the chain is cut.
    /// </summary>
    /// <remarks>
    /// This is a safety net rather than a rule. A six earns another die, so the chain is unbounded
    /// by the rules, and a die source that keeps returning six never stops - which used to spin
    /// forever inside the service's lock and take every match on the server with it. A real die
    /// reaches this length about once in three billion chains, so no game will ever notice, and
    /// the same bound already guards the firing initiative die-off.
    /// </remarks>
    public const int MaxChainLength = 20;

    /// <summary>Most point defence systems one ship's fire is resolved with.</summary>
    public const int MaxSystems = 32;

    /// <inheritdoc />
    public PointDefenseResult Resolve(int systems, int incoming)
    {
        var rolls = new List<int>();
        var scored = 0;
        for (var system = 0; system < Math.Clamp(systems, 0, MaxSystems); system++)
        {
            // A six kills two and earns another die, which scores the same way.
            var rolling = true;
            for (var chain = 0; rolling && chain < MaxChainLength; chain++)
            {
                var die = Math.Clamp(_rollDie(), 1, 6);
                rolls.Add(die);
                scored += die switch
                {
                    6 => 2,
                    5 or 4 => 1,
                    _ => 0,
                };
                rolling = die == 6;
            }
        }

        var kills = Math.Min(scored, Math.Max(0, incoming));
        return new PointDefenseResult(rolls, kills, scored - kills);
    }
}
