using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Damage;

/// <summary>
/// Damage control parties jury-rigging systems lost to a threshold check. One party brings a system
/// back on a roll the profile sets; each further party on the same job lowers the number needed by
/// one, down to the best the profile allows - and all the parties on a job make a single roll
/// between them. A failed attempt can be tried again next turn.
/// </summary>
/// <remarks>
/// The roll one party needs, the floor more parties can reach, and how many can usefully crowd one
/// job are the player's. What lives here is the shape: more hands make the number easier by one
/// each, and it stops getting easier at the floor.
/// </remarks>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
public sealed class FullThrustDamageControlRules(Func<int>? rollDie = null) : IRepairResolver
{
    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <inheritdoc />
    public int MaxPartiesPerJob(RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return Math.Max(1, rules.MaxPartiesPerJob);
    }

    /// <inheritdoc />
    public int NeededFor(int parties, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var crowd = Math.Clamp(parties, 1, MaxPartiesPerJob(rules));
        var best = Math.Max(1, rules.RepairBestRoll);
        var alone = Math.Max(best, rules.RepairRollWithOneParty);
        return Math.Clamp(alone - (crowd - 1), best, alone);
    }

    /// <inheritdoc />
    public RepairAttempt Resolve(RepairJob job, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(rules);

        var needed = NeededFor(job.Parties, rules);
        var roll = Math.Clamp(_rollDie(), 1, Math.Max(1, rules.DieFaces));
        return new RepairAttempt(job, needed, roll, roll >= needed);
    }
}
