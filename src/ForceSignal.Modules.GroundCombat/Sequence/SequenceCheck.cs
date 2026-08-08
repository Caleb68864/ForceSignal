namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>
/// Whether a transition is legal, and if not, why not.
/// </summary>
/// <param name="IsAllowed">True when the transition may be applied.</param>
/// <param name="Reason">Why it may not, or null when it may.</param>
/// <remarks>
/// Every transition in this namespace comes in a pair: a <c>Can*</c> that returns one of these and a
/// verb that applies it. A user interface asks first and shows the reason; a caller that already
/// knows the move is legal just applies it and lets the throw catch a programming error. This is the
/// same split as the Solve/Roll pair the combat code uses, for the same reason - the question and
/// the commitment are different acts.
/// </remarks>
public readonly record struct SequenceCheck(bool IsAllowed, string? Reason)
{
    /// <summary>A transition that may be applied.</summary>
    public static SequenceCheck Allowed => new(true, null);

    /// <summary>A transition that may not be applied, with the reason a player would be shown.</summary>
    /// <param name="reason">Why the transition is illegal.</param>
    /// <returns>The refusal.</returns>
    public static SequenceCheck Refused(string reason) => new(false, reason);
}
