using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Fighters;

/// <summary>
/// How many groups a ship can fly off and land in one turn, and how long a group that has just
/// landed takes to turn around.
/// </summary>
/// <remarks>
/// A profile picks one of two shapes. Either flight operations are capped by what the ship is - a
/// true carrier works one number of groups a turn and anything else with a bay works another, both
/// out of one shared budget - or the rate follows the ship's own fittings, one group per operational
/// bay out and half the bays back in, with launches and recoveries counted separately. Which shape,
/// and every number in it, are the player's.
/// </remarks>
/// <param name="rollDie">
/// Die source. Takes the number of faces and returns a face, so the die the table actually
/// plays with is the die that gets rolled - this used to be a nullary source that always
/// produced 1-6, with the profile's face count applied afterwards as a clamp, which cannot
/// produce a face above six and piles every face above the profile's onto its top one.
/// Injectable so tests and replays can be deterministic.
/// </param>
public sealed class FullThrustCarrierOperationRules(Func<int, int>? rollDie = null)
{
    private readonly Func<int, int> _rollDie = rollDie ?? (faces => Random.Shared.Next(1, faces + 1));

    /// <summary>
    /// What a ship may do with its bays this turn.
    /// </summary>
    /// <param name="rules">The profile being played against.</param>
    /// <param name="operationalBays">Bays still working after damage.</param>
    /// <param name="isTrueCarrier">
    /// Whether the ship is a carrier rather than a warship with bays fitted. Only the capped shape
    /// asks: a rate that follows the bays is precisely the change that stops needing the word
    /// "carrier" to mean anything in rules terms.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public static CarrierFlightAllowance AllowanceFor(RulesProfile rules, int operationalBays, bool isTrueCarrier)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var bays = Math.Max(0, operationalBays);
        if (!rules.CarrierRatesFollowBays)
        {
            var allowance = isTrueCarrier ? rules.TrueCarrierAllowance : rules.OtherShipAllowance;
            return new CarrierFlightAllowance(allowance, allowance, SharedAllowance: true);
        }

        // "Half the ship's operational bays" is printed without a rounding rule. Rounding up is the
        // only reading that does not make this shape strictly worse than the capped one for a small
        // ship: rounded down, a single-bay hull could never land anything at all. A table that
        // prefers rounding down should say so before the game, which is what the rules advise.
        return new CarrierFlightAllowance(bays, (bays + 1) / 2, SharedAllowance: false);
    }

    /// <summary>
    /// Rolls the turnaround for a group that has just landed: whether it can be sent out again, and
    /// how long the deck crews need. What each face means is the player's.
    /// </summary>
    /// <param name="rules">The profile being played against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public CarrierTurnaround RollTurnaround(RulesProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var roll = Math.Clamp(_rollDie(Faces(rules)), 1, Faces(rules));
        var outcome = rules.TurnaroundFor(roll);
        return new CarrierTurnaround(roll, outcome.IsGroundedForGame, outcome.TurnsBeforeRelaunch);
    }

    /// <summary>Faces on the die this table plays with. Never below one, so a blank profile still rolls.</summary>
    private static int Faces(RulesProfile rules) => Math.Max(1, rules.DieFaces);
}
