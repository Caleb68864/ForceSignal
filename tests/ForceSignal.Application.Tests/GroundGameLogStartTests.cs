using System.Text.Json.Nodes;
using ForceSignal.Application.Ground;
using ForceSignal.Contracts.Ground;
using ForceSignal.TestSupport;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Both ground engines keep the newest <c>GroundGameGuards.MaxLogEntries</c> log lines. Past that
/// the log's length stops moving, so a screen that read "it grew" as "it logged" said nothing, and
/// one that compared the newest line could not tell the same sentence twice from nothing at all.
/// The snapshot's <c>LogStart</c> - how many lines the ceiling has dropped - keeps
/// <c>LogStart + Log.Count</c> moving, and survives a restart.
/// </summary>
public sealed class GroundGameLogStartTests
{
    [Fact]
    public void AStarGruntSnapshotCountsTheLinesTheCeilingDropped()
    {
        var store = new MemoryStore();
        var service = new StarGruntGameService(null, store);
        var game = service.CreateGame(new CreateStarGruntGameRequest("Long Night", StarGruntTestProfile.LeadershipValuesOnly)).GameId;
        service.AddUnit(game, new AddStarGruntUnitRequest(
            "alpha", "Alpha", "blue", "Squad", 8, 3, [new StarGruntFigureDto(8)], [new StarGruntWeaponDto("Rifles", 8)]));
        FillTheLog(store, game);

        var reopened = new StarGruntGameService(null, store);
        var before = reopened.GetSnapshot(game);
        var once = reopened.TakeConfidenceTest(game, new StarGruntConfidenceTestRequest("alpha", 1));
        var twice = reopened.TakeConfidenceTest(game, new StarGruntConfidenceTestRequest("alpha", 1));

        Assert.Equal(2000, twice.Log.Count);
        Assert.Equal(Total(before) + 1, Total(once));
        Assert.Equal(Total(once) + 1, Total(twice));
        Assert.Equal(twice.LogStart, new StarGruntGameService(null, store).GetSnapshot(game).LogStart);
    }

    [Fact]
    public void ADirtsideSnapshotCountsTheLinesTheCeilingDropped()
    {
        var store = new MemoryStore();
        var service = new DirtsideGameService(store: store);
        var game = service.CreateGame(new CreateDirtsideGameRequest("Long Night", null, DirtsideTestProfile.Invented)).GameId;
        service.AddPlatoon(game, DirtsideGameServiceTests.Platoon("alpha", "Alpha Troop", "blue") with
        {
            QualityDie = "D8",
            LeadershipValue = 3,
        });
        FillTheLog(store, game);

        var reopened = new DirtsideGameService(store: store);
        var before = reopened.GetSnapshot(game);
        var once = reopened.TakeConfidenceTest(game, new DirtsideNerveTestRequest("alpha", 1));
        var twice = reopened.TakeConfidenceTest(game, new DirtsideNerveTestRequest("alpha", 1));

        Assert.Equal(2000, twice.Log.Count);
        Assert.Equal(Total(before) + 1, Total(once));
        Assert.Equal(Total(once) + 1, Total(twice));
        Assert.Equal(twice.LogStart, new DirtsideGameService(store: store).GetSnapshot(game).LogStart);
    }

    private static int Total(StarGruntSnapshotDto snapshot) => snapshot.LogStart + snapshot.Log.Count;

    private static int Total(DirtsideSnapshotDto snapshot) => snapshot.LogStart + snapshot.Log.Count;

    /// <summary>Rewrites the stored game's log to exactly the ceiling, so the next line drops one.</summary>
    private static void FillTheLog(MemoryStore store, Guid game)
    {
        var row = JsonNode.Parse(store.Rows[game])!.AsObject();
        var log = new JsonArray();
        for (var i = 0; i < 2000; i++)
        {
            log.Add($"line {i}");
        }

        // The module documents are written PascalCase or camelCase depending on the engine.
        var document = row["game"]!.AsObject();
        var key = document.Select(property => property.Key)
            .Single(name => string.Equals(name, "log", StringComparison.OrdinalIgnoreCase));
        document[key] = log;
        store.Save(game, row.ToJsonString());
    }
}
