using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Damage;

/// <summary>
/// Threshold checks. The hull is drawn as a number of rows; completing a row makes every surviving
/// system roll to stay alive, and an attack that tears through more than one row rolls once against
/// the deepest row reached, one point worse for each extra row passed.
/// </summary>
/// <remarks>
/// How many rows a track has, and whether that count follows the hull's size band, are the player's.
/// What lives here is how a track is divided - as evenly as the hull allows, remainder to the upper
/// rows - and the rule that completing the last row is the ship's destruction rather than another
/// check.
/// </remarks>
/// <param name="rollDie">
/// Die source. Takes the number of faces and returns a face, so the die the table actually
/// plays with is the die that gets rolled - this used to be a nullary source that always
/// produced 1-6, with the profile's face count applied afterwards as a clamp, which cannot
/// produce a face above six and piles every face above the profile's onto its top one.
/// Injectable so tests and replays can be deterministic.
/// </param>
public sealed class FullThrustLightThresholdRules(Func<int, int>? rollDie = null) : IThresholdResolver
{
    private readonly Func<int, int> _rollDie = rollDie ?? (faces => Random.Shared.Next(1, faces + 1));

    /// <inheritdoc />
    public IReadOnlyList<int> HullRows(int hullMax, RulesProfile rules, ShipClassBand? band = null) =>
        HullRowsFor(hullMax, RowCountFor(rules, band));

    /// <inheritdoc />
    public int RowsCompleted(int hullDamage, int hullMax, RulesProfile rules, ShipClassBand? band = null) =>
        RowsCompletedFor(hullDamage, hullMax, RowCountFor(rules, band));

    /// <summary>
    /// How many rows this profile draws for a hull of this size band. A profile that does not size
    /// the track by class, and a band nobody could work out, both get the profile's row count.
    /// </summary>
    /// <param name="rules">The profile being played against.</param>
    /// <param name="band">The hull's size band, or null when it is unknown.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public static int RowCountFor(RulesProfile rules, ShipClassBand? band)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.ThresholdRows != ThresholdRowMode.ByShipClass)
        {
            return Math.Max(1, rules.ThresholdRowCount);
        }

        return band switch
        {
            ShipClassBand.Escort when rules.EscortRowCount > 0 => rules.EscortRowCount,
            ShipClassBand.Cruiser when rules.CruiserRowCount > 0 => rules.CruiserRowCount,
            _ => Math.Max(1, rules.ThresholdRowCount),
        };
    }

    /// <summary>
    /// The hull's damage track as the number of boxes per row, top down. Rows are as even as the
    /// hull allows, with any remainder going to the upper rows.
    /// </summary>
    /// <param name="hullMax">Hull boxes the ship was built with.</param>
    /// <param name="rowCount">Rows to divide the track into.</param>
    public static int[] HullRowsFor(int hullMax, int rowCount)
    {
        var rows = Math.Max(1, rowCount);
        if (hullMax <= 0)
        {
            return [];
        }

        if (hullMax < rows)
        {
            // Too small to split that many ways: one box per row until the boxes run out.
            return [.. Enumerable.Repeat(1, hullMax)];
        }

        var baseSize = hullMax / rows;
        var remainder = hullMax % rows;
        return [.. Enumerable.Range(0, rows).Select(index => baseSize + (index < remainder ? 1 : 0))];
    }

    /// <summary>How many hull rows are fully crossed off at this much damage.</summary>
    /// <param name="hullDamage">Damage recorded against the hull.</param>
    /// <param name="hullMax">Hull boxes the ship was built with.</param>
    /// <param name="rowCount">Rows the track is divided into.</param>
    public static int RowsCompletedFor(int hullDamage, int hullMax, int rowCount)
    {
        var completed = 0;
        var remaining = Math.Clamp(hullDamage, 0, Math.Max(0, hullMax));
        foreach (var row in HullRowsFor(hullMax, rowCount))
        {
            if (remaining < row)
            {
                break;
            }

            remaining -= row;
            completed++;
        }

        return completed;
    }

    /// <summary>
    /// Deepest row that still rolls a check for a track of this many rows. Completing the last row
    /// destroys the ship, so no check is rolled for it.
    /// </summary>
    /// <param name="rowCount">Rows the track is divided into.</param>
    public static int DeepestThresholdFor(int rowCount) => Math.Max(1, rowCount - 1);

    /// <inheritdoc />
    public ThresholdCheckResult Resolve(ThresholdCheck check, RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(rules);

        var faces = Math.Max(1, rules.DieFaces);

        // The deepest row reached sets the number, then one point worse per extra row torn through.
        var threshold = Math.Clamp(check.Threshold, 1, DeepestThresholdFor(Math.Max(1, rules.ThresholdRowCount)));
        var lostOn = Math.Clamp(threshold + Math.Max(0, check.ExtraThresholds), 1, faces);
        var rolls = check.Systems
            .Select(system =>
            {
                var die = Math.Clamp(_rollDie(faces), 1, faces);
                return new ThresholdRoll(system, die, die <= lostOn);
            })
            .ToArray();

        return new ThresholdCheckResult(threshold, lostOn, rolls);
    }
}
