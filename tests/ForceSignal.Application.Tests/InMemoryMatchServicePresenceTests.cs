using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Presence is what the other device is told about this one, so it has to follow the connections
/// that actually exist rather than the last thing any one of them said.
/// </summary>
public sealed class InMemoryMatchServicePresenceTests
{
    [Fact]
    public void AnOldConnectionDroppingLate_DoesNotMarkAPlayerWhoReconnectedAsGone()
    {
        // A SignalR reconnect opens the new connection first; the server only drops the old one at
        // its timeout. The order the hub sees is connect, connect, disconnect - and the player is
        // still online on the new connection. A single flag said "disconnected".
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Presence", Rules: TestRules.Invented));

        Assert.NotNull(service.SetParticipantConnection(owner.MatchId, owner.ParticipantToken, true));
        Assert.Null(service.SetParticipantConnection(owner.MatchId, owner.ParticipantToken, true));
        Assert.Null(service.SetParticipantConnection(owner.MatchId, owner.ParticipantToken, false));

        var seat = service.GetSnapshot(owner.MatchId).Participants.Single();
        Assert.True(seat.IsConnected);
    }

    [Fact]
    public void TheLastConnectionLeaving_IsStillReportedAsADisconnect()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Presence", Rules: TestRules.Invented));

        service.SetParticipantConnection(owner.MatchId, owner.ParticipantToken, true);
        service.SetParticipantConnection(owner.MatchId, owner.ParticipantToken, true);
        service.SetParticipantConnection(owner.MatchId, owner.ParticipantToken, false);
        var last = service.SetParticipantConnection(owner.MatchId, owner.ParticipantToken, false);

        Assert.NotNull(last);
        Assert.False(last.Participants.Single().IsConnected);
        // A stray extra disconnect cannot drive the count below zero and hide the next connect.
        Assert.Null(service.SetParticipantConnection(owner.MatchId, owner.ParticipantToken, false));
        Assert.NotNull(service.SetParticipantConnection(owner.MatchId, owner.ParticipantToken, true));
    }
}
