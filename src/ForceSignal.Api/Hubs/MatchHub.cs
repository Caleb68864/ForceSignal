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
