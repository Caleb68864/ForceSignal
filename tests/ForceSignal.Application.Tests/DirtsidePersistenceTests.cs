using ForceSignal.Application.Ground;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.TestSupport;

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
            ScriptedChitPot.Handing(DamageChit.Numerical(ChitColour.Red, 8)));
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
    public void APotAndProfileSavedByNameComeBackNowThatOnlyNamesAreRead()
    {
        // The counted pot and the entered tables are saved as the words a request carries, and are
        // read back through the same name-only parsing on a restart. They were always written as
        // the members' names, so a save from before that parsing tightened still loads.
        var store = new MemoryStore();
        var first = new DirtsideGameService(null, store);
        var created = first.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9") with
        {
            ChitPot = new DirtsideChitPotDto(
                [new DirtsideNumericalChitsDto("red", 3, 12), new DirtsideNumericalChitsDto("Green", 0, 4)],
                [new DirtsideSpecialChitsDto("boom", 2)]),
        });
        first.AddPlatoon(created.GameId, DirtsideGameServiceTests.Platoon("alpha", "Alpha Troop", "blue"));

        var row = store.LoadAll().Single().State;
        Assert.Contains("Red", row, StringComparison.Ordinal);
        Assert.Contains("Enhanced", row, StringComparison.Ordinal);
        Assert.Contains("HullDown", row, StringComparison.Ordinal);

        var restarted = new DirtsideGameService(null, store);
        var after = restarted.GetSnapshot(created.GameId);

        Assert.Empty(restarted.SkippedSaves);
        Assert.Equal(created.Snapshot.ChitPot!.Numericals, after.ChitPot!.Numericals);
        Assert.Equal(created.Snapshot.ChitPot.Specials, after.ChitPot.Specials);
        Assert.Equal(created.Snapshot.Profile!.FireControl, after.Profile!.FireControl);
        Assert.Equal(created.Snapshot.Profile.Posture, after.Profile.Posture);
        Assert.Contains(after.ChitPot.Specials, entry => entry is { Special: "Boom", Count: 2 });
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
        Assert.NotNull(service.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9")));
        Assert.Single(service.SkippedSaves);
    }

    [Fact]
    public void WithNoStoreNothingIsWrittenAndNothingIsLost()
    {
        var service = new DirtsideGameService();
        var game = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9")).GameId;

        Assert.NotNull(service.GetSnapshot(game));
    }
}
