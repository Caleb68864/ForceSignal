using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;

namespace ForceSignal.Application.Tests;

public sealed class InMemoryMatchServiceRestoreTests
{
    [Fact]
    public void EmptyToken_NeverAuthenticates()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Seat Guard"));

        Assert.False(service.IsMatchParticipant(owner.MatchId, string.Empty));
        Assert.False(service.IsMatchParticipant(owner.MatchId, "   "));
        Assert.Throws<UnauthorizedAccessException>(() =>
            service.SetReady(owner.MatchId, string.Empty, true));
    }
}
