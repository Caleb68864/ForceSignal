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

    /// <inheritdoc />
    public PointDefenseResult Resolve(int systems, int incoming)
    {
        var rolls = new List<int>();
        var scored = 0;
        for (var system = 0; system < Math.Max(0, systems); system++)
        {
            // A six kills two and earns another die, which scores the same way.
            var rolling = true;
            while (rolling)
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
