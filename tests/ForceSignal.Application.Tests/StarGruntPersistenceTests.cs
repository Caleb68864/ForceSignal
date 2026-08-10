using ForceSignal.Application.Ground;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Ground;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Covers a StarGrunt game surviving the process that was serving it.
/// </summary>
public sealed class StarGruntPersistenceTests
{
    [Fact]
    public void AGameMidActivationComesBackAfterARestart()
    {
        var store = new MemoryStore();
        var first = new StarGruntGameService(new ScriptedQualityDice(6, 7, 5, 4, 5, 3, 5, 9, 4), store);
        var game = Table(first);
        first.BeginTurn(game);
        first.ChooseFirstActivator(game, new ChooseFirstActivatorRequest("blue", TakeIt: true));
        first.BeginActivation(game, new BeginStarGruntActivationRequest("blue", "alpha"));
        var before = first.Fire(game, new StarGruntFireRequest(
            "alpha", "bravo", "Rifles", FirepowerDie: 10, SupportWeapons: [], DistanceInches: 9, Cover: "Soft"));

        // A second service over the same store is what a restart looks like.
        var after = new StarGruntGameService(null, store).GetSnapshot(game);

        Assert.Equal(before.Phase, after.Phase);
        Assert.Equal(before.TurnNumber, after.TurnNumber);
        Assert.Equal(before.ActivatingUnitId, after.ActivatingUnitId);
        Assert.Equal(
            before.Units.Single(unit => unit.Id == "bravo").FiguresAlive,
            after.Units.Single(unit => unit.Id == "bravo").FiguresAlive);
        Assert.Equal(
            before.Units.Single(unit => unit.Id == "bravo").SuppressionMarkers,
            after.Units.Single(unit => unit.Id == "bravo").SuppressionMarkers);
    }

    [Fact]
    public void ARestoredGameCanBePlayedOn()
    {
        var store = new MemoryStore();
        var first = new StarGruntGameService(null, store);
        var game = Table(first);
        first.BeginTurn(game);
        first.ChooseFirstActivator(game, new ChooseFirstActivatorRequest("blue", TakeIt: true));
        first.BeginActivation(game, new BeginStarGruntActivationRequest("blue", "alpha"));

        var restarted = new StarGruntGameService(null, store);

        // The open activation came back with it, so it can be closed rather than restarted.
        Assert.Equal("Activating", restarted.EndActivation(game).Phase);
    }

    [Fact]
    public void ASaveFromAnotherGameIsSkippedRatherThanStoppingStartup()
    {
        var store = new MemoryStore();
        store.Save(Guid.NewGuid(), "{\"this\":\"is a Full Thrust match\"}");

        var service = new StarGruntGameService(null, store);

        // Starting at all is the assertion: one unreadable save must not take the others with it.
        Assert.NotNull(service.CreateGame(new CreateStarGruntGameRequest("Hill 43")));
    }

    [Fact]
    public void WithNoStoreNothingIsWrittenAndNothingIsLost()
    {
        var service = new StarGruntGameService();
        var game = service.CreateGame(new CreateStarGruntGameRequest("Hill 43")).GameId;

        Assert.NotNull(service.GetSnapshot(game));
    }

    private static Guid Table(StarGruntGameService service)
    {
        var created = service.CreateGame(new CreateStarGruntGameRequest("Hill 43"));
        service.AddUnit(created.GameId, Squad("alpha", "Alpha Squad", "blue"));
        service.AddUnit(created.GameId, Squad("bravo", "Bravo Squad", "red"));
        return created.GameId;
    }

    private static AddStarGruntUnitRequest Squad(string id, string name, string side) => new(
        id,
        name,
        side,
        "Squad",
        QualityDie: 8,
        LeadershipValue: 2,
        Figures: [.. Enumerable.Repeat(new StarGruntFigureDto(6), 8)],
        Weapons: [new StarGruntWeaponDto("Rifles", 10)]);

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
