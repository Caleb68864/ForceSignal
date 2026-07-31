using ForceSignal.Application.Matches;
using Microsoft.AspNetCore.SignalR;

namespace ForceSignal.Api.Hubs;

/// <summary>SignalR hub for match-scoped realtime snapshot notifications.</summary>
/// <param name="matches">Match service used to verify participant tokens before group membership.</param>
public sealed class MatchHub(IMatchService matches) : Hub
{
    /// <summary>Adds the connection to a match notification group after verifying the participant token.</summary>
    public Task JoinMatchGroup(string matchId, string participantToken)
    {
        if (!Guid.TryParse(matchId, out var parsedMatchId) || !matches.IsMatchParticipant(parsedMatchId, participantToken))
        {
            throw new HubException("A valid participant token is required to follow this match.");
        }

        return Groups.AddToGroupAsync(Context.ConnectionId, parsedMatchId.ToString());
    }

    /// <summary>Removes the connection from a match notification group.</summary>
    public Task LeaveMatchGroup(string matchId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, matchId);
}
