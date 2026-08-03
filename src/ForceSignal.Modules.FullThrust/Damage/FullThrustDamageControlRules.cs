using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Damage;

/// <summary>
/// Damage control parties jury-rigging systems lost to a threshold check. One party brings a system
/// back on a 6; each further party on the same job lowers the number needed by one, to a best case of
/// 4 or better with three - and all the parties on a job make a single roll between them. A failed
/// attempt can be tried again next turn.
/// </summary>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
public sealed class FullThrustDamageControlRules(Func<int>? rollDie = null) : IRepairResolver
{
    /// <summary>Three parties is as many as can usefully crowd one job.</summary>
    public const int PartiesPerJobLimit = 3;

    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <inheritdoc />
    public int MaxPartiesPerJob => PartiesPerJobLimit;

    /// <inheritdoc />
    public int NeededFor(int parties) =>
        Math.Clamp(6 - (Math.Clamp(parties, 1, PartiesPerJobLimit) - 1), 4, 6);

    /// <inheritdoc />
    public RepairAttempt Resolve(RepairJob job)
    {
        var needed = NeededFor(job.Parties);
        var roll = Math.Clamp(_rollDie(), 1, 6);
        return new RepairAttempt(job, needed, roll, roll >= needed);
    }
}
