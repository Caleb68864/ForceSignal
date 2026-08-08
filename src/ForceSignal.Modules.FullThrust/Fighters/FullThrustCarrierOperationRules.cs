using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Fighters;

/// <summary>
/// How many groups a ship can fly off and land in one turn, and how long a group that has just
/// landed takes to turn around.
///
/// The older layer caps flight operations by what the ship is: a true carrier works two groups a
/// turn and anything else with a bay manages one, launches and recoveries sharing that one budget.
/// The Fleet Book replaces that with a rate the ship's own fittings set - one group per operational
/// bay out, half the bays back in, and a ship may do both in the same turn. The point of the change
/// is that a big carrier's alpha strike is no longer throttled by a rule about what to call it.
/// </summary>
/// <param name="rollDie">Die source, injectable so tests and replays can be deterministic.</param>
public sealed class FullThrustCarrierOperationRules(Func<int>? rollDie = null)
{
    /// <summary>Groups a true carrier may work in a turn under the older cap.</summary>
    public const int TrueCarrierAllowance = 2;

    /// <summary>Groups any other ship with a bay may work in a turn under the older cap.</summary>
    public const int OtherShipAllowance = 1;

    private readonly Func<int> _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <summary>
    /// What a ship may do with its bays this turn.
    /// </summary>
    /// <param name="rules">The layer being played, or null for the light cinematic default.</param>
    /// <param name="operationalBays">Bays still working after damage.</param>
    /// <param name="isTrueCarrier">
    /// Whether the ship is a carrier rather than a warship with bays fitted. Only the older layer
    /// asks: the Fleet Book rate follows the bays, which is precisely why it stops needing the word
    /// "carrier" to mean anything in rules terms.
    /// </param>
    public static CarrierFlightAllowance AllowanceFor(RulesProfile? rules, int operationalBays, bool isTrueCarrier)
    {
        var bays = Math.Max(0, operationalBays);
        if (!(rules ?? RulesProfile.LightCinematic).CarrierRatesFollowBays)
        {
            var allowance = isTrueCarrier ? TrueCarrierAllowance : OtherShipAllowance;
            return new CarrierFlightAllowance(allowance, allowance, SharedAllowance: true);
        }

        // "Half the ship's operational bays" is printed without a rounding rule. Rounding up is the
        // only reading that does not make the newer layer strictly worse than the older one for a
        // small ship: rounded down, a single-bay hull could never land anything at all, where the
        // older layer let it recover one. A table that prefers rounding down should say so before
        // the game, which is what the rules themselves advise.
        return new CarrierFlightAllowance(bays, (bays + 1) / 2, SharedAllowance: false);
    }

    /// <summary>
    /// Rolls the turnaround for a group that has just landed: whether it can be sent out again, and
    /// how long the deck crews need. A 1 grounds the group for the rest of the game, a 6 has it back
    /// on the catapult next turn, and anything between takes a full turn to rearm and refuel.
    /// </summary>
    public CarrierTurnaround RollTurnaround()
    {
        var roll = Math.Clamp(_rollDie(), 1, 6);
        return roll switch
        {
            1 => new CarrierTurnaround(roll, IsGroundedForGame: true, TurnsBeforeRelaunch: 0),
            6 => new CarrierTurnaround(roll, IsGroundedForGame: false, TurnsBeforeRelaunch: 1),
            _ => new CarrierTurnaround(roll, IsGroundedForGame: false, TurnsBeforeRelaunch: 2),
        };
    }
}
