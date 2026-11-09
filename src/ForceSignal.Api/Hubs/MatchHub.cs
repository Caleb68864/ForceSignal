using ForceSignal.Application.Matches;
using Microsoft.AspNetCore.SignalR;

namespace ForceSignal.Api.Hubs;

/// <summary>SignalR hub for match-scoped realtime snapshot notifications.</summary>
/// <param name="matches">Match service used to verify participant tokens and track presence.</param>
/// <param name="logger">Where each connection's join, refusal and drop is written down.</param>
/// <remarks>
/// The log lines here are the server's half of a two-device game: which connection joined which
/// match, which one was refused, and which one dropped and why. Tokens are never written; the
/// connection id is what ties a client's console to these lines.
/// </remarks>
public sealed partial class MatchHub(IMatchService matches, ILogger<MatchHub> logger) : Hub
{
    private const string SessionKey = "forcesignal.session";

    /// <summary>Adds the connection to a match notification group after verifying the participant token.</summary>
    public async Task JoinMatchGroup(string matchId, string participantToken)
    {
        if (!Guid.TryParse(matchId, out var parsedMatchId) || !matches.IsMatchParticipant(parsedMatchId, participantToken))
        {
            LogJoinRefused(logger, Context.ConnectionId, matchId);
            throw new HubException("A valid participant token is required to follow this match.");
        }

        // A connection follows one seat. Joining again with the same seat is a retry and changes
        // nothing; joining with a different one releases the first. Presence is counted per
        // connection, so letting either case count twice would leave a seat "connected" forever
        // after this connection had gone.
        if (Context.Items.TryGetValue(SessionKey, out var existing) && existing is (Guid previousMatchId, string previousToken))
        {
            if (previousMatchId == parsedMatchId && string.Equals(previousToken, participantToken, StringComparison.Ordinal))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, parsedMatchId.ToString());
                LogJoinRepeated(logger, Context.ConnectionId, parsedMatchId);
                return;
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, previousMatchId.ToString());
            Context.Items.Remove(SessionKey);
            await NotifyPresence(previousMatchId, previousToken, false);
        }

        var group = parsedMatchId.ToString();
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        Context.Items[SessionKey] = (parsedMatchId, participantToken);
        LogJoined(logger, Context.ConnectionId, parsedMatchId);
        await NotifyPresence(parsedMatchId, participantToken, true);
    }

    // There is deliberately no LeaveMatchGroup. There was one, and nothing ever called it: a
    // connection lives exactly as long as this device's seat at this match, so leaving is stopping,
    // and stopping is what OnDisconnectedAsync below already answers. A hub method is remotely
    // invokable by anyone who can reach the hub, which makes an uncalled one surface without a
    // caller to justify it. MatchHubSurfaceTests keeps the list to what is actually used.

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(SessionKey, out var session) && session is (Guid matchId, string token))
        {
            // The exception was dropped on the floor here, which is the one thing that says whether
            // the other device saw a clean close or a transport that died under it.
            if (exception is null)
            {
                LogLeft(logger, Context.ConnectionId, matchId);
            }
            else
            {
                LogDropped(logger, exception, Context.ConnectionId, matchId);
            }

            await NotifyPresence(matchId, token, false);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task NotifyPresence(Guid matchId, string participantToken, bool isConnected)
    {
        var snapshot = matches.SetParticipantConnection(matchId, participantToken, isConnected);
        if (snapshot is null)
        {
            return;
        }

        await Clients.Group(matchId.ToString())
            .SendAsync("MatchSnapshotChanged", snapshot.MatchId, snapshot.Version, "ParticipantConnectionChanged");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hub connection {ConnectionId} refused for match {MatchId}: not a participant token for it")]
    private static partial void LogJoinRefused(ILogger logger, string connectionId, string matchId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hub connection {ConnectionId} joined match {MatchId}")]
    private static partial void LogJoined(ILogger logger, string connectionId, Guid matchId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hub connection {ConnectionId} joined match {MatchId} again; already following it")]
    private static partial void LogJoinRepeated(ILogger logger, string connectionId, Guid matchId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hub connection {ConnectionId} left match {MatchId}")]
    private static partial void LogLeft(ILogger logger, string connectionId, Guid matchId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hub connection {ConnectionId} dropped from match {MatchId}")]
    private static partial void LogDropped(ILogger logger, Exception exception, string connectionId, Guid matchId);
}
