namespace ForceSignal.Modules.GroundCombat.Sequence;

/// <summary>
/// What a command did: the game it produced, or the reason it was not allowed to.
/// </summary>
/// <typeparam name="T">What the command produces when it is allowed.</typeparam>
/// <remarks>
/// <para>
/// The same split the sequence layer already made with <c>SequenceCheck</c>, carried up to the game:
/// a refusal is an answer, not an exception. A player asking whether they may do something and a
/// player doing it are different acts, and only the second one is an error when it fails.
/// </para>
/// <para>
/// This matters more than it looks. It is what lets the screen show why a button is disabled using
/// the same words the command would refuse with, rather than growing its own opinion about the rules
/// and drifting from them.
/// </para>
/// <para>
/// Shared rather than per-game. It began in the StarGrunt module because that was the only game with
/// commands to refuse; it moved here the moment Dirtside grew some, since a refusal is the shape of
/// an answer rather than anything either game owns.
/// </para>
/// </remarks>
/// <param name="IsAllowed">True when the command ran.</param>
/// <param name="Reason">Why it did not, or null when it did.</param>
/// <param name="Value">What it produced, or null when it was refused.</param>
public readonly record struct GameOutcome<T>(bool IsAllowed, string? Reason, T? Value);

/// <summary>
/// Builds outcomes, letting the type be inferred at the call site.
/// </summary>
/// <remarks>
/// A companion to <see cref="GameOutcome{T}"/> rather than static members on it: statics on a
/// generic type have to be written out in full at every use, which turns each one into a restatement
/// of the type it already returns.
/// </remarks>
public static class GameOutcome
{
    /// <summary>A command that ran.</summary>
    /// <typeparam name="T">What it produced.</typeparam>
    /// <param name="value">The result.</param>
    /// <returns>The outcome.</returns>
    public static GameOutcome<T> Allowed<T>(T value) => new(true, null, value);

    /// <summary>A command that was refused, in words a player would be shown.</summary>
    /// <typeparam name="T">What it would have produced.</typeparam>
    /// <param name="reason">Why it did not.</param>
    /// <returns>The outcome.</returns>
    public static GameOutcome<T> Refused<T>(string reason) => new(false, reason, default);
}
