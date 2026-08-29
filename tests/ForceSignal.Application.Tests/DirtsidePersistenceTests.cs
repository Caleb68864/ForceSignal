using ForceSignal.Application.Ground;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.Dirtside.Chits;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Covers a Dirtside game surviving the process that was serving it.
/// </summary>
public sealed class DirtsidePersistenceTests
{
    [Fact]
    public void AGameMidActivationComesBackAfterARestart()
    {
        var store = new MemoryStore();
        var first = new DirtsideGameService(
            new ScriptedQualityDice(1, 8),
            store,
            new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 8)));
        var game = DirtsideGameServiceTests.Activated(first);
        var before = first.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close"));

        // A second service over the same store is what a restart looks like.
        var after = new DirtsideGameService(null, store).GetSnapshot(game);

        Assert.Equal(before.Phase, after.Phase);
        Assert.Equal(before.TurnNumber, after.TurnNumber);
        Assert.Equal(before.ActivatingUnitId, after.ActivatingUnitId);
        Assert.Equal(before.ElementsStillToChoose, after.ElementsStillToChoose);
        Assert.True(DirtsideGameServiceTests.Element(after, "bravo", "bravo-1").IsDestroyed);
        Assert.True(DirtsideGameServiceTests.Element(after, "alpha", "alpha-1").HasTakenCombatAction);
    }

    [Fact]
    public void ARestoredGameCanBePlayedOn()
    {
        var store = new MemoryStore();
        var first = new DirtsideGameService(null, store);
        var game = DirtsideGameServiceTests.Activated(first);
        first.StandDown(game, new DirtsideStandDownRequest("alpha-1"));

        var restarted = new DirtsideGameService(null, store);

        // The open activation came back with it, half-decided, so it can be finished rather than
        // started again.
        restarted.StandDown(game, new DirtsideStandDownRequest("alpha-2"));
        Assert.Equal("Activating", restarted.EndActivation(game).Phase);
    }

    [Fact]
    public void ASaveFromAnotherGameIsSkippedRatherThanStoppingStartup()
    {
        var store = new MemoryStore();
        store.Save(Guid.NewGuid(), "{\"this\":\"is a Full Thrust match\"}");

        var service = new DirtsideGameService(null, store);

        // Starting at all is the assertion: one unreadable save must not take the others with it.
        Assert.NotNull(service.CreateGame(new CreateDirtsideGameRequest("Ridge 9")));
        Assert.Single(service.SkippedSaves);
    }

    [Fact]
    public void WithNoStoreNothingIsWrittenAndNothingIsLost()
    {
        var service = new DirtsideGameService();
        var game = service.CreateGame(new CreateDirtsideGameRequest("Ridge 9")).GameId;

        Assert.NotNull(service.GetSnapshot(game));
    }

    /// <summary>A store that keeps its rows in memory, standing in for the SQLite one.</summary>
    private sealed class MemoryStore : IMatchStore
    {
        private readonly Dictionary<Guid, string> _rows = [];

        public void Save(Guid matchId, string state) => _rows[matchId] = state;

        public void Remove(Guid matchId) => _rows.Remove(matchId);

        public IReadOnlyList<StoredMatch> LoadAll() =>
            [.. _rows.Select(row => new StoredMatch(row.Key, row.Value))];
    }
}
