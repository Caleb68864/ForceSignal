namespace ForceSignal.Application;

/// <summary>
/// Something the caller addressed by id is not here: a match, a game, a ship, a seat, a room code.
/// </summary>
/// <remarks>
/// <para>
/// This is the one refusal the API answers with a 404 rather than a 400, and it used to be told
/// apart by the words "not found" appearing in the message. A hundred-odd throw sites and a status
/// code that turned on the phrasing of one of them was a bug waiting for somebody to reword a
/// sentence. The type is the signal now; the message is only for the player.
/// </para>
/// <para>
/// It derives from <see cref="InvalidOperationException"/> deliberately, so the code that already
/// catches a refusal from the service keeps catching this one.
/// </para>
/// </remarks>
public sealed class NotFoundException : InvalidOperationException
{
    /// <summary>Creates the exception with the default message.</summary>
    public NotFoundException()
        : base("The thing you asked for was not found.")
    {
    }

    /// <summary>Creates the exception with a message for the player.</summary>
    /// <param name="message">What was not found, in words a player can act on.</param>
    public NotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the failure underneath it.</summary>
    /// <param name="message">What was not found.</param>
    /// <param name="innerException">What went wrong looking.</param>
    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
