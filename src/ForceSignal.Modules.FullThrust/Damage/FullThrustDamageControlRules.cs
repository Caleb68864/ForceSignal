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
/// <param name="rollDie">
/// Die source. Takes the number of faces and returns a face, so the die the table actually
/// plays with is the die that gets rolled - this used to be a nullary source that always
/// produced 1-6, with the profile's face count applied afterwards as a clamp, which cannot
/// produce a face above six and piles every face above the profile's onto its top one.
/// Injectable so tests and replays can be deterministic.
/// </param>
public sealed class FullThrustDamageControlRules(Func<int, int>? rollDie = null) : IRepairResolver
{
    private readonly Func<int, int> _rollDie = rollDie ?? (faces => Random.Shared.Next(1, faces + 1));

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
        var roll = Math.Clamp(_rollDie(Faces(rules)), 1, Faces(rules));
        return new RepairAttempt(job, needed, roll, roll >= needed);
    }

    /// <summary>Faces on the die this table plays with. Never below one, so a blank profile still rolls.</summary>
    private static int Faces(RulesProfile rules) => Math.Max(1, rules.DieFaces);
}
