namespace ForceSignal.Domain.Rules;

/// <summary>One repair job: a system to work on and how many parties are put on it.</summary>
/// <param name="Kind">The sort of system being worked on.</param>
/// <param name="WeaponId">The mount being worked on, for weapons only.</param>
/// <param name="Parties">Damage control parties assigned to this job.</param>
public sealed record RepairJob(ShipSystemKind Kind, Guid? WeaponId, int Parties);

/// <summary>What one repair job rolled.</summary>
/// <param name="Job">The job that was attempted.</param>
/// <param name="Needed">The number the roll had to beat.</param>
/// <param name="Roll">What it rolled.</param>
/// <param name="IsRepaired">Whether the system came back online.</param>
public sealed record RepairAttempt(RepairJob Job, int Needed, int Roll, bool IsRepaired);

/// <summary>Rolls damage control repairs for a rules profile.</summary>
public interface IRepairResolver
{
    /// <summary>Most parties that can usefully work one job.</summary>
    int MaxPartiesPerJob { get; }

    /// <summary>The number a job needs, given how many parties are on it.</summary>
    int NeededFor(int parties);

    /// <summary>Rolls one job. All the parties on a job make a single roll between them.</summary>
    RepairAttempt Resolve(RepairJob job);
}
