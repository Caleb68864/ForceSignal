namespace ForceSignal.Domain.Rules;

/// <summary>Which way a fighter group is crossing the deck.</summary>
public enum CarrierOperation
{
    /// <summary>The group is leaving the ship.</summary>
    Launch,

    /// <summary>The group is coming aboard.</summary>
    Recovery
}

/// <summary>
/// What a carrier is allowed to do with its bays this turn under one rules layer.
/// </summary>
/// <param name="Launches">Groups the ship may put up this turn.</param>
/// <param name="Recoveries">Groups the ship may bring aboard this turn.</param>
/// <param name="SharedAllowance">
/// True when launches and recoveries draw on one budget between them, so a ship that has launched
/// its allowance cannot then recover. The older layer works this way; the Fleet Book gives launch
/// and recovery separate allowances and lets a ship do both in a turn.
/// </param>
public sealed record CarrierFlightAllowance(int Launches, int Recoveries, bool SharedAllowance);

/// <summary>
/// The turnaround roll a recovered group makes before it can be sent out again.
/// </summary>
/// <param name="Roll">The die face rolled.</param>
/// <param name="IsGroundedForGame">True when the group cannot be launched again at all.</param>
/// <param name="TurnsBeforeRelaunch">
/// Turns that must pass before the group may launch again, counted from the turn it landed. One
/// means the turn immediately following. Zero when the group is grounded for good.
/// </param>
public sealed record CarrierTurnaround(int Roll, bool IsGroundedForGame, int TurnsBeforeRelaunch);
