namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>
/// Everything the shared sequence layer does not know, supplied by whichever game is being played.
/// </summary>
/// <remarks>
/// <para>
/// The split is deliberate and narrow. The shared layer owns the shape of a turn - alternation, the
/// frame stack, interrupt windows, who may pass - and owns the two rules the games share verbatim.
/// It owns no numbers at all. Whether a step is legal, whether a frame is finished, and what a step
/// exposes the actor to are all questions with different answers in the two games, and every one of
/// them is asked rather than assumed.
/// </para>
/// <para>
/// In particular the shared layer never counts steps. Counting would encode StarGrunt's two-action
/// budget, and Dirtside has no such budget to count.
/// </para>
/// </remarks>
public interface IActivationPolicy
{
    /// <summary>
    /// Whether the acting unit may take this step now.
    /// </summary>
    /// <param name="session">The session as it stands.</param>
    /// <param name="frame">The frame the step would go into.</param>
    /// <param name="step">The proposed step.</param>
    /// <returns>Allowed, or the reason it is not.</returns>
    /// <remarks>
    /// Resource limits belong here, and they should be written against
    /// <see cref="ActivationFrame.ResourcesSpent"/> rather than against anything the policy keeps for
    /// itself. Read off the frame, a limit resets exactly when a new frame is pushed - which is what
    /// makes a weapon's fire limit per activation rather than per turn without anyone arranging it.
    /// </remarks>
    SequenceCheck IsStepLegal(GroundCombatSession session, ActivationFrame frame, ActivationStep step);

    /// <summary>
    /// Whether this frame has done everything it is going to.
    /// </summary>
    /// <param name="session">The session as it stands.</param>
    /// <param name="frame">The frame in question.</param>
    /// <returns>True when the frame may be closed.</returns>
    /// <remarks>
    /// StarGrunt answers this by counting to two. Dirtside answers it by asking whether every element
    /// in the platoon has finished choosing. Both are the game's business, not the sequence layer's.
    /// </remarks>
    bool IsFrameComplete(GroundCombatSession session, ActivationFrame frame);

    /// <summary>
    /// Whether taking this step gives the other side an opening, and if so, whose and where.
    /// </summary>
    /// <param name="session">The session as it stands, before the step is recorded.</param>
    /// <param name="frame">The frame the step is going into.</param>
    /// <param name="step">The step being taken.</param>
    /// <returns>The window to open, or null when the step is not a trigger.</returns>
    /// <remarks>
    /// The geometry comes back with the request because only the game knows where the interrupt
    /// resolves, and for reaction fire that is a point the moving token never actually occupies.
    /// Once returned it is stored verbatim and never recomputed.
    /// </remarks>
    InterruptWindowRequest? WindowOpenedBy(
        GroundCombatSession session,
        ActivationFrame frame,
        ActivationStep step);
}
