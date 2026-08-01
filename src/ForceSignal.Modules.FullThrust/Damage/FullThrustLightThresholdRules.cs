using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Damage;

/// <summary>
/// Threshold checks for the light cinematic rules set. The hull is drawn as four rows; completing
/// a row makes every surviving system roll to stay alive. Full Thrust Light knocks a system out on
/// a low roll - a 1 at the first threshold, 1-2 at the second, 1-3 at the third - and an attack
/// that tears through more than one row rolls once against the deepest row, one point worse for
/// each extra row passed.
/// </summary>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
public sealed class FullThrustLightThresholdRules(Func<int>? rollDie = null) : IThresholdResolver
{
    /// <summary>Rows in a hull damage track. The fourth row ending is the ship's destruction.</summary>
    public const int RowCount = 4;

    /// <summary>Deepest row that still rolls a check. Completing the last row destroys the ship.</summary>
    public const int DeepestThreshold = RowCount - 1;

    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <inheritdoc />
    public IReadOnlyList<int> HullRows(int hullMax) => HullRowsFor(hullMax);

    /// <inheritdoc />
    public int RowsCompleted(int hullDamage, int hullMax) => RowsCompletedFor(hullDamage, hullMax);

    /// <summary>
    /// The hull's damage track as the number of boxes per row, top down. Rows are as even as the
    /// hull allows, with any remainder going to the upper rows.
    /// </summary>
    public static int[] HullRowsFor(int hullMax)
    {
        if (hullMax <= 0)
        {
            return [];
        }

        if (hullMax < RowCount)
        {
            // Too small to split four ways: one box per row until the boxes run out.
            return [.. Enumerable.Repeat(1, hullMax)];
        }

        var baseSize = hullMax / RowCount;
        var remainder = hullMax % RowCount;
        return [.. Enumerable.Range(0, RowCount).Select(index => baseSize + (index < remainder ? 1 : 0))];
    }

    /// <summary>How many hull rows are fully crossed off at this much damage.</summary>
    public static int RowsCompletedFor(int hullDamage, int hullMax)
    {
        var completed = 0;
        var remaining = Math.Clamp(hullDamage, 0, Math.Max(0, hullMax));
        foreach (var row in HullRowsFor(hullMax))
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

    /// <inheritdoc />
    public ThresholdCheckResult Resolve(ThresholdCheck check)
    {
        // The deepest row reached sets the number, then one point worse per extra row torn through.
        var threshold = Math.Clamp(check.Threshold, 1, DeepestThreshold);
        var lostOn = Math.Clamp(threshold + Math.Max(0, check.ExtraThresholds), 1, 6);
        var rolls = check.Systems
            .Select(system =>
            {
                var die = Math.Clamp(_rollDie(), 1, 6);
                return new ThresholdRoll(system, die, die <= lostOn);
            })
            .ToArray();

        return new ThresholdCheckResult(threshold, lostOn, rolls);
    }
}
