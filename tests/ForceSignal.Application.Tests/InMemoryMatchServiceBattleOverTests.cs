using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A Full Thrust battle ends (playtest M1). It never did: with one side wiped out the loser was asked
/// to plot zero ships and the winner to plot turn five. Two ends, and no others - this app does not
/// invent victory conditions: a side with no ship left, and the host saying the battle is over.
/// </summary>
public sealed class InMemoryMatchServiceBattleOverTests
{
    private sealed record Table(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        string OpponentToken,
        ShipDto BlueShip,
        ShipDto RedShip);

    private static Table Ready(IMatchStore? store = null)
    {
        var service = new InMemoryMatchService(_ => 6, store, loadPersisted: store is not null);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue Admiral", "Battle Over", 72, 48, Rules: TestRules.Invented));
        var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red Admiral"));
        var blueFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue Squadron", null))
            .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var redFleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red Squadron", null))
            .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        var blueShip = service.CreateShip(blueFleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Blue One", "Cruiser", 6, 0, 12, 10, 0, StartX: 18, StartY: 30)).Ships.Single(s => s.Name == "Blue One");
        var redShip = service.CreateShip(redFleet.Id, new CreateShipRequest(
            opponent.ParticipantToken, "Red One", "Cruiser", 6, 0, 6, 10, 0, StartX: 50, StartY: 18)).Ships.Single(s => s.Name == "Red One");
        service.SetReady(owner.MatchId, owner.ParticipantToken, true);
        service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
        return new Table(service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, blueShip, redShip);
    }

    [Fact]
    public void AMatchInPlayIsNotOver()
    {
        var t = Ready();

        Assert.Null(t.Service.GetSnapshot(t.MatchId).BattleOver);
    }

    [Fact]
    public void ASideWithNoShipLeftEndsTheBattle_AndNoMoreTurnsArePlayed()
    {
        var t = Ready();

        var wiped = t.Service.UpdateShipDamage(t.RedShip.Id, new UpdateShipDamageRequest(t.OpponentToken, 10, 0, 0, 0, 0));

        Assert.NotNull(wiped.BattleOver);
        Assert.Equal("OneSideLeft", wiped.BattleOver.Reason);
        Assert.Contains("Red Admiral has no ships left", wiped.BattleOver.Message, StringComparison.Ordinal);
        var refused = Assert.Throws<InvalidOperationException>(() => t.Service.CommitOrder(t.MatchId, new CommitOrderRequest(
            t.OwnerToken, t.BlueShip.Id, new MovementOrder(0, 0, TurnDirection.None), "salt")));
        Assert.Contains("The battle is over", refused.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => t.Service.AdvanceTurn(t.MatchId, t.OwnerToken));
    }

    [Fact]
    public void TheHostCanEndTheBattle_TheGuestCannot()
    {
        var t = Ready();

        Assert.Throws<UnauthorizedAccessException>(() => t.Service.EndBattle(t.MatchId, new EndBattleRequest(t.OpponentToken)));
        var ended = t.Service.EndBattle(t.MatchId, new EndBattleRequest(t.OwnerToken));

        Assert.NotNull(ended.BattleOver);
        Assert.Equal("EndedByHost", ended.BattleOver.Reason);
        Assert.Contains("Blue Admiral ended the battle", ended.BattleOver.Message, StringComparison.Ordinal);
        Assert.Contains(ended.MatchLog, entry => entry.Message.Contains("ended the battle", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => t.Service.EndBattle(t.MatchId, new EndBattleRequest(t.OwnerToken)));
    }

    [Fact]
    public void ThereIsNoBattleToEndDuringFleetSetup()
    {
        var service = new InMemoryMatchService(_ => 6);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue Admiral", "Not Yet", 72, 48, Rules: TestRules.Invented));

        var refused = Assert.Throws<InvalidOperationException>(() => service.EndBattle(owner.MatchId, new EndBattleRequest(owner.ParticipantToken)));
        Assert.Contains("fleet setup", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEndedBattleStaysEndedAcrossARestart()
    {
        var store = new MemoryStore();
        var t = Ready(store);
        t.Service.EndBattle(t.MatchId, new EndBattleRequest(t.OwnerToken));

        var reopened = new InMemoryMatchService(_ => 6, store, loadPersisted: true);

        Assert.Equal("EndedByHost", reopened.GetSnapshot(t.MatchId).BattleOver?.Reason);
    }
}
