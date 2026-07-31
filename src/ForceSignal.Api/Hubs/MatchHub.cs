using ForceSignal.Application.Matches;
using Microsoft.AspNetCore.SignalR;

namespace ForceSignal.Api.Hubs;

/// <summary>SignalR hub for match-scoped realtime snapshot notifications.</summary>
/// <param name="matches">Match service used to verify participant tokens and track presence.</param>
public sealed class MatchHub(IMatchService matches) : Hub
{
    private const string SessionKey = "forcesignal.session";

    /// <summary>Adds the connection to a match notification group after verifying the participant token.</summary>
    public async Task JoinMatchGroup(string matchId, string participantToken)
    {
        if (!Guid.TryParse(matchId, out var parsedMatchId) || !matches.IsMatchParticipant(parsedMatchId, participantToken))
        {
            throw new HubException("A valid participant token is required to follow this match.");
        }

        var group = parsedMatchId.ToString();
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        Context.Items[SessionKey] = (parsedMatchId, participantToken);
        await NotifyPresence(parsedMatchId, participantToken, true);
    }

    /// <summary>Removes the connection from a match notification group.</summary>
    public async Task LeaveMatchGroup(string matchId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, matchId);
        if (Context.Items.Remove(SessionKey, out var session) && session is (Guid trackedMatchId, string token))
        {
            await NotifyPresence(trackedMatchId, token, false);
        }
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(SessionKey, out var session) && session is (Guid matchId, string token))
        {
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
}
