namespace ForceSignal.Modules.Dirtside.Sequence;

/// <summary>
/// What one element of an activated platoon may do.
/// </summary>
/// <remarks>
/// <para>
/// There is no budget here to spend these from, and that is the whole shape of Dirtside's activation.
/// The other game hands a unit two actions and lets it choose; this one activates a platoon and then
/// each element inside it independently picks move-then-act, act-then-move, act only, move only, or
/// nothing at all. Those five patterns are not enumerated anywhere in the engine - they fall out of
/// one move and one combat action per element, taken in whichever order the player likes.
/// </para>
/// <para>
/// <see cref="Nothing"/> is a real step rather than the absence of one. An element that sits out has
/// given up its go for the whole turn and may not act later, so saying so is what closes the
/// activation honestly; see <see cref="DirtsideActivationPolicy.IsFrameComplete"/>.
/// </para>
/// </remarks>
public enum DirtsideAction
{
    /// <summary>Move. Once, and not a combat action.</summary>
    Move = 0,

    /// <summary>Open fire with one weapon system, or join an infantry firefight.</summary>
    DirectFire = 1,

    /// <summary>Watch, so that indirect fire can be called onto what is watched.</summary>
    ObserveForIndirectFire = 2,

    /// <summary>Go in against a held position.</summary>
    CloseAssault = 3,

    /// <summary>Switch the area-defence sensors on or off.</summary>
    ToggleAreaDefenceSensors = 4,

    /// <summary>Sit this one out, for the rest of the turn.</summary>
    Nothing = 5,
}

/// <summary>Which of the actions are combat actions, and which of those name a weapon.</summary>
public static class DirtsideActions
{
    /// <summary>How many combat actions one element gets.</summary>
    /// <remarks>
    /// Per element, per activation - not per activation as a whole, and not per weapon. It is the
    /// closest thing Dirtside has to the other game's action budget and it is scoped differently
    /// enough that the two must not be expressed against each other.
    /// </remarks>
    public const int CombatActionsPerElement = 1;

    /// <summary>True when this action is the element's one combat action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>True for a combat action.</returns>
    /// <remarks>
    /// Switching the air-defence sensors counts, which looks generous until you notice what it buys:
    /// live sensors let the vehicle intercept all turn without ever spending an activation again. The
    /// combat action is the price of the standing reaction.
    /// </remarks>
    public static bool IsCombatAction(DirtsideAction action) =>
        action is not (DirtsideAction.Move or DirtsideAction.Nothing);

    /// <summary>True when this action has to say which weapon system it uses.</summary>
    /// <param name="action">The action.</param>
    /// <returns>True when a weapon must be named.</returns>
    public static bool NamesAWeapon(DirtsideAction action) => action == DirtsideAction.DirectFire;
}
