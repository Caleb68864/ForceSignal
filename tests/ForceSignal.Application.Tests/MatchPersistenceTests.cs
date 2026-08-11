using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// What has to survive the process dying mid-game.
///
/// The bar here is higher than "the match still exists". A restart has to be invisible to the
/// devices at the table: the same tokens still work, the same ship ids still address the same
/// ships, and an order already locked is still locked and still revealable. Anything less and the
/// players have to sort out an administrative mess in the middle of a turn, which is exactly when
/// they have least patience for one.
/// </summary>
public sealed class MatchPersistenceTests
{
    [Fact]
    public void AMatchComesBackAfterTheProcessDies()
    {
        var store = new InMemoryTestStore();
        var before = new InMemoryMatchService(null, store, loadPersisted: true);
        var owner = before.CreateMatch(new CreateMatchRequest("Blue", "Long Game", Rules: TestRules.Invented));
        var fleet = before.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue Watch", "Custom")).Fleets.Single();
        before.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant"));

        // The process goes away. A new one starts against the same file.
        var after = new InMemoryMatchService(null, store, loadPersisted: true);
        var snapshot = after.GetSnapshot(owner.MatchId);

        Assert.Equal("Long Game", snapshot.Name);
        Assert.Equal(owner.JoinCode, snapshot.JoinCode);
        var ship = Assert.Single(snapshot.Ships);
        Assert.Equal("Valiant", ship.Name);
        Assert.Equal("Blue Watch", snapshot.Fleets.Single().Name);
    }

    [Fact]
    public void TheTokenOnTheDeviceStillCommandsItsShipsAfterARestart()
    {
        // This is the difference between persistence and the export/restore path, which reissues
        // ids and makes everybody claim their seat again.
        var store = new InMemoryTestStore();
        var before = new InMemoryMatchService(null, store, loadPersisted: true);
        var owner = before.CreateMatch(new CreateMatchRequest("Blue", "Same Seat", Rules: TestRules.Invented));
        var fleet = before.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        var shipId = before.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant")).Ships.Single().Id;

        var after = new InMemoryMatchService(null, store, loadPersisted: true);

        // Same token, same ship id, no re-claiming.
        var damaged = after.UpdateShipDamage(shipId, new UpdateShipDamageRequest(owner.ParticipantToken, 3, 0, 0, 0, 0, 0, 0));
        Assert.Equal(3, damaged.Ships.Single().HullDamage);
        Assert.True(after.IsMatchParticipant(owner.MatchId, owner.ParticipantToken));
    }

    [Fact]
    public void AnOrderLockedBeforeTheRestartIsStillLockedAndStillRevealable()
    {
        // The salt was never on the server to lose - it lives in the browser that made it - so the
        // hash surviving is enough for the reveal to go through exactly as it would have.
        var store = new InMemoryTestStore();
        var before = new InMemoryMatchService(null, store, loadPersisted: true);
        var owner = before.CreateMatch(new CreateMatchRequest("Blue", "Mid Turn", Rules: TestRules.Invented));
        var fleet = before.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        var shipId = before.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant")).Ships.Single().Id;
        before.SetReady(owner.MatchId, owner.ParticipantToken, true);

        var order = new MovementOrder(1, 0, TurnDirection.None);
        var locked = before.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, shipId, order, "the-only-copy-is-in-the-browser"));
        Assert.True(locked.OrderStatuses.Single(status => status.ShipId == shipId).IsCommitted);

        var after = new InMemoryMatchService(null, store, loadPersisted: true);
        Assert.True(after.GetSnapshot(owner.MatchId).OrderStatuses.Single(status => status.ShipId == shipId).IsCommitted);

        var revealed = after.RevealOrder(owner.MatchId, new RevealOrderRequest(owner.ParticipantToken, shipId, order, "the-only-copy-is-in-the-browser"));
        var status = revealed.OrderStatuses.Single(entry => entry.ShipId == shipId);
        Assert.True(status.IsRevealed);
        Assert.False(status.VerificationFailed);
    }

    [Fact]
    public void TheVersionCarriesOverSoClientsDoNotIgnoreTheRestartedServer()
    {
        // Every client drops a snapshot whose version is not newer than the one it is showing. A
        // server that began counting again at one would be ignored until it caught up, and the
        // board would simply stop moving with nothing on screen to say why.
        var store = new InMemoryTestStore();
        var before = new InMemoryMatchService(null, store, loadPersisted: true);
        var owner = before.CreateMatch(new CreateMatchRequest("Blue", "Versioned", Rules: TestRules.Invented));
        for (var i = 0; i < 5; i++)
        {
            before.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, $"Fleet {i}", null));
        }

        var lastSeen = before.GetSnapshot(owner.MatchId).Version;
        Assert.True(lastSeen > 1);

        var after = new InMemoryMatchService(null, store, loadPersisted: true);
        Assert.Equal(lastSeen, after.GetSnapshot(owner.MatchId).Version);

        var next = after.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "After", null));
        Assert.True(next.Version > lastSeen);
    }

    [Fact]
    public void TheBattleLogAndItsSequenceSurvive()
    {
        var store = new InMemoryTestStore();
        var before = new InMemoryMatchService(null, store, loadPersisted: true);
        var owner = before.CreateMatch(new CreateMatchRequest("Blue", "Log", Rules: TestRules.Invented));
        before.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null));
        var written = before.GetSnapshot(owner.MatchId).MatchLog;

        var after = new InMemoryMatchService(null, store, loadPersisted: true);
        var reloaded = after.GetSnapshot(owner.MatchId).MatchLog;

        Assert.Equal(written.Select(entry => entry.Message), reloaded.Select(entry => entry.Message));

        // The next entry continues the numbering rather than repeating one already used.
        after.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Red", null));
        var continued = after.GetSnapshot(owner.MatchId).MatchLog;
        Assert.Equal(continued.Count, continued.Select(entry => entry.Sequence).Distinct().Count());
        Assert.True(continued[^1].Sequence > written[^1].Sequence);
    }

    [Fact]
    public void ARetiredMatchDoesNotComeBackFromTheDead()
    {
        var store = new InMemoryTestStore();
        var service = new InMemoryMatchService(null, store, loadPersisted: true);
        var first = service.CreateMatch(new CreateMatchRequest("Blue", "First", Rules: TestRules.Invented));

        // Push past the concurrent ceiling so the oldest is retired.
        for (var i = 0; i < 520; i++)
        {
            service.CreateMatch(new CreateMatchRequest("Blue", $"Match {i}", Rules: TestRules.Invented));
        }

        Assert.Throws<InvalidOperationException>(() => service.GetSnapshot(first.MatchId));

        var after = new InMemoryMatchService(null, store, loadPersisted: true);
        Assert.Throws<InvalidOperationException>(() => after.GetSnapshot(first.MatchId));
    }

    [Fact]
    public void AnUnreadableRecordCostsThatMatchAndNoOther()
    {
        var store = new InMemoryTestStore();
        var before = new InMemoryMatchService(null, store, loadPersisted: true);
        var good = before.CreateMatch(new CreateMatchRequest("Blue", "Readable", Rules: TestRules.Invented));
        store.Save(Guid.NewGuid(), "{ this is not a match }");

        var after = new InMemoryMatchService(null, store, loadPersisted: true);

        Assert.Equal("Readable", after.GetSnapshot(good.MatchId).Name);
    }

    [Fact]
    public void WithNoStoreNothingIsWrittenAtAll()
    {
        // The default has to stay the old behaviour, so a test or a throwaway session does not
        // start leaving files behind.
        var store = new InMemoryTestStore();
        var service = new InMemoryMatchService();
        service.CreateMatch(new CreateMatchRequest("Blue", "Ephemeral", Rules: TestRules.Invented));

        Assert.Empty(store.LoadAll());
    }

    private static CreateShipRequest Ship(string token, string name) => new(
        token, name, "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24);

    /// <summary>A store that keeps what it is given in memory, standing in for the file.</summary>
    private sealed class InMemoryTestStore : IMatchStore
    {
        private readonly Dictionary<Guid, string> _rows = [];

        public void Save(Guid matchId, string state) => _rows[matchId] = state;

        public void Remove(Guid matchId) => _rows.Remove(matchId);

        public IReadOnlyList<StoredMatch> LoadAll() =>
            [.. _rows.Select(row => new StoredMatch(row.Key, row.Value))];
    }
}
