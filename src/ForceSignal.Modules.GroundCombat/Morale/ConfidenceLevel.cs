namespace ForceSignal.Modules.GroundCombat.Morale;

/// <summary>
/// How much fight a unit has left. The ladder runs from ready for anything down to running away,
/// and the lower it sinks the less the unit will agree to do.
/// </summary>
/// <remarks>
/// <para>
/// The numeric values run high-to-low so that losing a level is subtraction, which is how both
/// rulebooks describe it - a failed test drops a unit one rung, a badly failed one drops it two.
/// </para>
/// <para>
/// The five rungs are shared by both games word for word, so they live here. What a rung
/// <em>stops a unit doing</em> is not shared and deliberately has no home in this namespace: one
/// game's restrictions are cumulative for everybody, the other's are two separate columns for foot
/// and for armour. Pushing an effects table down here would have to pick one of those and be wrong
/// for the other game.
/// </para>
/// </remarks>
public enum ConfidenceLevel
{
    /// <summary>Morale shattered. Out of the fight and heading for the back edge.</summary>
    Routed = 0,

    /// <summary>Morale almost gone. No longer willing to fight, only to get out of the way.</summary>
    Broken = 1,

    /// <summary>Distinctly worried. Reluctant to take a risk it would otherwise take.</summary>
    Shaken = 2,

    /// <summary>Morale holding. Generally still willing to fight.</summary>
    Steady = 3,

    /// <summary>Morale high. Ready for anything.</summary>
    Confident = 4,
}
