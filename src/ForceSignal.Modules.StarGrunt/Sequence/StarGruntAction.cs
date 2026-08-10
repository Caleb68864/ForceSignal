using ForceSignal.Modules.StarGrunt.Morale;

namespace ForceSignal.Modules.StarGrunt.Sequence;

/// <summary>
/// The menu an activation spends its actions from.
/// </summary>
/// <remarks>
/// The rules split these into <em>motivation</em> actions - the leader getting the squad to do
/// something - and <em>leader</em> actions, where the leader acts himself. The split matters because
/// a pinned squad can still be led even when it cannot be moved, so nearly everything a suppressed
/// unit may still do is a leader action.
/// </remarks>
public enum StarGruntAction
{
    /// <summary>A single move, leaving an action for something else.</summary>
    Move = 0,

    /// <summary>
    /// Both actions spent on movement, declared as one thing rather than as two moves.
    /// </summary>
    /// <remarks>
    /// See <see cref="StarGruntActivationPolicy"/> for why this is its own action rather than moving
    /// twice. In short: the reaction-fire window has to open deterministically, and a window that
    /// appears after the first move and is taken back if the second never comes tells the opponent
    /// something he has not earned.
    /// </remarks>
    Dash = 1,

    /// <summary>Open fire with one weapon.</summary>
    Fire = 2,

    /// <summary>Go in with the bayonet.</summary>
    CloseAssault = 3,

    /// <summary>Pull the squad back into integrity without shifting its position.</summary>
    Reorganise = 4,

    /// <summary>A superior talking a shaken unit back up the confidence ladder.</summary>
    Rally = 5,

    /// <summary>Get a message out.</summary>
    Communicate = 6,

    /// <summary>Watch, and be able to call fire onto what is watched.</summary>
    Observe = 7,

    /// <summary>Try to get one suppression marker off.</summary>
    RemoveSuppression = 8,

    /// <summary>Hand a subordinate a whole extra activation.</summary>
    TransferAction = 9,

    /// <summary>Split a sub-group off to act on its own.</summary>
    FormDetachedElement = 10,

    /// <summary>Settle into a prepared firing position.</summary>
    GoInPosition = 11,

    /// <summary>
    /// An order the troops would not carry out.
    /// </summary>
    /// <remarks>
    /// Not something a unit chooses, which is why it reads oddly in this list: it is what is left of
    /// an action after a failed reaction test. It belongs here rather than as an invented step kind
    /// because the action economy is derived from the steps in a frame, so a lost action has to be a
    /// step or it is not lost at all.
    /// </remarks>
    RefusedOrder = 12,
}

/// <summary>
/// What each action costs, who performs it, and whether it may be attempted twice.
/// </summary>
public static class StarGruntActions
{
    /// <summary>
    /// Actions an activation buys.
    /// </summary>
    /// <remarks>
    /// This is the one place StarGrunt genuinely is a budget, and it is why the shared layer must not
    /// be: Dirtside has no equivalent, so counting belongs here rather than underneath.
    /// </remarks>
    public const int ActionsPerActivation = 2;

    /// <summary>How many subordinates one commander may spring in a turn.</summary>
    public const int TransfersPerCommander = 2;

    /// <summary>How many actions this action costs.</summary>
    /// <param name="action">The action.</param>
    /// <returns>Its cost.</returns>
    /// <remarks>
    /// A dash is two actions bought together rather than two actions taken one after the other. That
    /// is the whole difference, and it is what makes the reaction-fire trigger a declaration instead
    /// of a deduction.
    /// </remarks>
    public static int Cost(StarGruntAction action) => action == StarGruntAction.Dash ? 2 : 1;

    /// <summary>True when the leader is acting himself rather than motivating the squad.</summary>
    /// <param name="action">The action.</param>
    /// <returns>True for a leader action.</returns>
    public static bool IsLeaderAction(StarGruntAction action) => action switch
    {
        StarGruntAction.Communicate
            or StarGruntAction.Observe
            or StarGruntAction.RemoveSuppression
            or StarGruntAction.TransferAction
            or StarGruntAction.FormDetachedElement
            or StarGruntAction.Rally => true,
        _ => false,
    };

    /// <summary>
    /// True when this action may be attempted twice in one activation.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>True when a second attempt is allowed.</returns>
    /// <remarks>
    /// <para>
    /// Firing is repeatable but the weapon is not, which is why the fire limit is a resource rather
    /// than a repeat rule - two fire actions are legal only with two different weapons.
    /// </para>
    /// <para>
    /// Moving is listed as repeatable in the rules but is not repeatable here, deliberately: two move
    /// actions <em>are</em> a dash, and asking for it up front is the only way to open the reaction
    /// window without leaking the fact that a second move was considered.
    /// </para>
    /// </remarks>
    public static bool MayRepeat(StarGruntAction action) => action switch
    {
        StarGruntAction.Fire
            or StarGruntAction.Communicate
            or StarGruntAction.Observe
            or StarGruntAction.RemoveSuppression
            or StarGruntAction.TransferAction
            or StarGruntAction.FormDetachedElement => true,
        _ => false,
    };

    /// <summary>
    /// How a pinned unit sees this action.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>The category the suppression rules judge it by.</returns>
    /// <remarks>
    /// Two of these are judgement calls rather than quotations. A transfer is a communication, so a
    /// pinned commander may still spring a subordinate - his own squad stays pinned either way.
    /// Detaching an element is treated as movement, because splitting a squad up under effective fire
    /// is exactly the thing suppression is meant to stop.
    /// </remarks>
    public static SuppressedAction AsSuppressedAction(StarGruntAction action) => action switch
    {
        StarGruntAction.Observe => SuppressedAction.Observe,
        StarGruntAction.Communicate or StarGruntAction.TransferAction or StarGruntAction.Rally =>
            SuppressedAction.Communicate,
        StarGruntAction.RemoveSuppression => SuppressedAction.RemoveSuppression,
        StarGruntAction.Reorganise => SuppressedAction.Reorganise,
        StarGruntAction.Fire => SuppressedAction.Fire,
        // A refusal costs the action whatever the unit's state, so it is never gated as a move.
        StarGruntAction.RefusedOrder => SuppressedAction.Observe,
        _ => SuppressedAction.Move,
    };
}
