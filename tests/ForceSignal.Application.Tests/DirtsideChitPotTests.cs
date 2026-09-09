using System.Text.Json;
using System.Text.Json.Nodes;
using ForceSignal.Application.Ground;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.Dirtside.Chits;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Covers the chit pot being the players' and belonging to one game.
/// </summary>
/// <remarks>
/// <para>
/// The composition is the single most sensitive input in the whole damage model - every probability
/// in the game moves when it moves - and until this change it was a static initialiser wrapped in a
/// process-wide singleton, so no table could say what was in the bag they were drawing from and every
/// table on the server was drawing from the same one.
/// </para>
/// <para>
/// Every count in this file is the fixture's, which is to say a player's. Where a test needs the
/// built-in fallback it says so by sending nothing, and asserts that the server admits the counts
/// are a guess.
/// </para>
/// </remarks>
public sealed class DirtsideChitPotTests
{
    /// <summary>A pot of nothing but Boom, which makes every hit a kill whatever the armour is.</summary>
    private static DirtsideChitPotDto AllBoom { get; } = new([], [new DirtsideSpecialChitsDto("Boom", 12)]);

    /// <summary>A pot of nothing but zeros, which makes every hit harmless whatever the armour is.</summary>
    private static DirtsideChitPotDto AllZeroes { get; } = new(
        [new DirtsideNumericalChitsDto("Red", 0, 12)],
        []);

    [Fact]
    public void TheCountsOnTheCreateRequestAreTheCountsTheGameDrawsFrom()
    {
        // The whole chain, request to draw: no factory injected, so the real ChitPot is shuffled
        // around whatever the request carried. Armour 3 against a bag holding nothing but Boom, and
        // Boom removes a vehicle whatever its armour is - so the outcome is settled by the
        // composition alone and by nothing else in the resolution.
        var service = new DirtsideGameService(new ScriptedQualityDice(1, 8));
        var game = Activated(service, AllBoom);

        var fired = service.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close"));

        Assert.True(DirtsideGameServiceTests.Element(fired, "bravo", "bravo-1").IsDestroyed);
    }

    [Fact]
    public void TwoGamesOnOneServerGetPotsBuiltFromTheirOwnCounts()
    {
        // The finding this change exists to close: one pot per process meant one bag for every table
        // on the machine. Asserted at the seam rather than through a draw, because "these two games
        // are drawing from different bags" is a statement about which composition each pot was built
        // from, and reading it off outcomes would be reading it off the shuffle.
        var built = new List<ChitPotComposition>();
        var service = new DirtsideGameService(
            new ScriptedQualityDice(1, 8),
            null,
            composition =>
            {
                built.Add(composition);
                return new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 0));
            });

        Activated(service, AllBoom);
        Activated(service, AllZeroes);

        Assert.Equal(2, built.Count);
        Assert.Equal(12, built[0].CountOf(DamageChit.Of(ChitSpecial.Boom)));
        Assert.Equal(0, built[0].CountOf(DamageChit.Numerical(ChitColour.Red, 0)));
        Assert.Equal(0, built[1].CountOf(DamageChit.Of(ChitSpecial.Boom)));
        Assert.Equal(12, built[1].CountOf(DamageChit.Numerical(ChitColour.Red, 0)));
    }

    [Fact]
    public void AGameWithNoCountsGetsAPotBuiltFromTheBuiltInDefault()
    {
        // The other half of the same seam: the fallback is a real composition handed to the same
        // factory, not a special case that skips it.
        var built = new List<ChitPotComposition>();
        var service = new DirtsideGameService(
            null,
            null,
            composition =>
            {
                built.Add(composition);
                return new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 0));
            });

        service.CreateGame(new CreateDirtsideGameRequest("Ridge 9"));
        var fallback = Assert.Single(built);

        // Held to the composition itself rather than to its total. This read
        // `Assert.Equal(ChitPotComposition.Default.Count, ...Count)`, and the pot the fallback path
        // returns *is* Default - so it compared Default's total to Default's total, and it compared
        // a total rather than contents. Replacing the fallback with a hundred and eighteen Boom
        // chits - every hit a kill whatever the armour, the most destructive change this engine
        // admits - left all 1350 tests green. This is the guard that was missing: the fallback is
        // the documented guess, and not a bag of the same size.
        Assert.Same(ChitPotComposition.Default, fallback);

        // And what that reference is worth, so the assertion above cannot be satisfied by a
        // different composition that happens to be assigned to Default one day: a hundred numbered
        // chits, half red and the rest split, with the specials the minority the counter sheet
        // describes. The special counts themselves are a documented guess and are deliberately not
        // pinned here - ChitPotCompositionTests owns the composition's own coherence, and says why.
        Assert.Equal(100, fallback.Chits.Count(chit => !chit.IsSpecial));
        Assert.Equal(50, fallback.Chits.Count(chit => chit.Colour == ChitColour.Red));
        Assert.Equal(25, fallback.Chits.Count(chit => chit.Colour == ChitColour.Yellow));
        Assert.Equal(25, fallback.Chits.Count(chit => chit.Colour == ChitColour.Green));
        Assert.True(
            fallback.CountOf(DamageChit.Of(ChitSpecial.Boom))
            < fallback.CountOf(DamageChit.Numerical(ChitColour.Red, 0)),
            "A table on the fallback must not draw Boom more often than it draws a numbered chit.");
    }

    [Fact]
    public void TheSnapshotReadsBackTheCountsThePlayersEntered()
    {
        var service = new DirtsideGameService();
        var created = service.CreateGame(new CreateDirtsideGameRequest(
            "Ridge 9",
            new DirtsideChitPotDto(
                [new DirtsideNumericalChitsDto("Red", 0, 4), new DirtsideNumericalChitsDto("Green", 2, 1)],
                [new DirtsideSpecialChitsDto("Boom", 2)])));

        var pot = created.Snapshot.ChitPot;
        Assert.NotNull(pot);
        Assert.False(pot.IsBuiltInDefaultGuess);
        Assert.Equal(
            [("Red", 0, 4), ("Green", 2, 1)],
            pot.Numericals.Select(entry => (entry.Colour, entry.Value, entry.Count)).OrderByDescending(entry => entry.Count));
        Assert.Equal([("Boom", 2)], pot.Specials.Select(entry => (entry.Special, entry.Count)));
    }

    [Fact]
    public void AGameCreatedWithNoPotSaysOutLoudThatItsCountsAreOurs()
    {
        // The default survives one release, and the price of that is that it never passes itself off
        // as the players'. A screen reading this flag can say where the numbers came from.
        var service = new DirtsideGameService();
        var created = service.CreateGame(new CreateDirtsideGameRequest("Ridge 9"));

        Assert.NotNull(created.Snapshot.ChitPot);
        Assert.True(created.Snapshot.ChitPot.IsBuiltInDefaultGuess);
    }

    [Fact]
    public void APotSurvivesARestart()
    {
        var store = new MemoryStore();
        var first = new DirtsideGameService(new ScriptedQualityDice(1, 8), store);
        var game = Activated(first, AllZeroes);

        // A second service over the same store is what a restart looks like.
        var restarted = new DirtsideGameService(new ScriptedQualityDice(1, 8), store);
        var fired = restarted.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close"));

        Assert.False(restarted.GetSnapshot(game).ChitPot!.IsBuiltInDefaultGuess);
        Assert.False(DirtsideGameServiceTests.Element(fired, "bravo", "bravo-1").IsDestroyed);
        Assert.False(DirtsideGameServiceTests.Element(fired, "bravo", "bravo-1").IsDamaged);
    }

    [Fact]
    public void AGameStoredBeforeThePotWasCarriedStillOpens()
    {
        // The one that would have bitten. GroundGameRecord skips a row it cannot read rather than
        // migrating it, so a *required* settings field would have retired every stored Dirtside game
        // on the machine in silence. The field is optional, and this is a row from before it existed:
        // written by hand rather than by the current Wrap, because the point is that the old bytes
        // still load.
        var store = new MemoryStore();
        var gameId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            token = new string('a', 64),
            lastActivity = DateTimeOffset.UtcNow,
            game = JsonDocument.Parse(SavedGame()).RootElement,
        });
        store.Save(gameId, body);

        var service = new DirtsideGameService(null, store);

        Assert.Empty(service.SkippedSaves);
        var snapshot = service.GetSnapshot(gameId);
        Assert.Equal("Ridge 9", snapshot.Name);

        // And it falls back the same way a create request carrying no pot does, admitting as much.
        Assert.True(snapshot.ChitPot!.IsBuiltInDefaultGuess);
    }

    [Fact]
    public void AGameWhoseSettingsCannotBeReadKeepsTheGameAndLosesOnlyTheSettings()
    {
        // The other half of the optional-settings promise. The design survives a settings field
        // that is *absent* - the test above proves it - and it did not survive one that is present
        // and a different shape, which is what the next change to this blob produces. ReadSettings
        // sat inside the per-row try whose catch is `catch (Exception)`, so shape drift in a field
        // that carries no part of the game retired the whole game: the document, the token, the
        // turn, all of it, on a row that was perfectly readable.
        var store = new MemoryStore();
        var first = new DirtsideGameService(new ScriptedQualityDice(1, 8), store);
        var game = Activated(first, AllZeroes);

        // Drift rather than garbage: the field is there and holds JSON, it is simply not the shape
        // this version reads. The game document is left exactly as it was written.
        var row = JsonNode.Parse(store.LoadAll().Single(saved => saved.MatchId == game).State)!;
        row["settings"] = JsonNode.Parse("""{"chitPot":[]}""");
        store.Save(game, row.ToJsonString());

        var restarted = new DirtsideGameService(new ScriptedQualityDice(1, 8), store);

        Assert.Empty(restarted.SkippedSaves);
        var snapshot = restarted.GetSnapshot(game);
        Assert.Equal("Ridge 9", snapshot.Name);

        // The settings are what was lost, and losing them is not silent: the pot falls back the way
        // an absent one does, and says out loud that its counts are ours rather than the players'.
        Assert.True(snapshot.ChitPot!.IsBuiltInDefaultGuess);
    }

    [Fact]
    public void AGameStoredWithNoPotStillSaysTheCountsAreAGuessAfterASecondRestart()
    {
        // The flag has to be stored, not re-derived: a fallback game that came back claiming the
        // counts were the players' would launder the guess into an answer on the first restart.
        var store = new MemoryStore();
        var first = new DirtsideGameService(null, store);
        var game = first.CreateGame(new CreateDirtsideGameRequest("Ridge 9")).GameId;

        var second = new DirtsideGameService(null, store);
        second.GetSnapshot(game);
        var third = new DirtsideGameService(null, store);

        Assert.True(third.GetSnapshot(game).ChitPot!.IsBuiltInDefaultGuess);
    }

    [Theory]
    [InlineData("Purple", 0, 1, "is not a chit colour")]
    [InlineData("Red", -1, 1, "is not one this server will hold")]
    [InlineData("Red", 100, 1, "is not one this server will hold")]
    [InlineData("Red", 0, -1, "cannot hold")]
    public void ACountThatDoesNotDescribeAChitIsRefused(string colour, int value, int count, string says)
    {
        var service = new DirtsideGameService();
        var request = new CreateDirtsideGameRequest(
            "Ridge 9",
            new DirtsideChitPotDto([new DirtsideNumericalChitsDto(colour, value, count)], []));

        var error = Assert.Throws<InvalidOperationException>(() => service.CreateGame(request));
        Assert.Contains(says, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASpecialNobodyPrintsIsRefusedAndTheMessageNamesTheOnesThatExist()
    {
        var service = new DirtsideGameService();
        var request = new CreateDirtsideGameRequest(
            "Ridge 9",
            new DirtsideChitPotDto([], [new DirtsideSpecialChitsDto("Fire", 3)]));

        var error = Assert.Throws<InvalidOperationException>(() => service.CreateGame(request));
        foreach (var special in DirtsideWire.ChitSpecials)
        {
            Assert.Contains(special, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnEmptyPotIsRefusedRatherThanStartingAGameNothingCanBeResolvedIn()
    {
        var service = new DirtsideGameService();
        var request = new CreateDirtsideGameRequest("Ridge 9", new DirtsideChitPotDto([], []));

        var error = Assert.Throws<InvalidOperationException>(() => service.CreateGame(request));
        Assert.Contains("count your chits", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APotPastTheCeilingIsRefusedRatherThanAllocated()
    {
        // The count arrives off an unauthenticated create route and becomes the length of an array.
        // Five thousand is DirtsideChitPotMapping.MaxChitsInPot, spelt out here because the mapping
        // is internal to the Application assembly and this project holds it to its behaviour rather
        // than reaching in for its constants.
        var service = new DirtsideGameService();
        var request = new CreateDirtsideGameRequest(
            "Ridge 9",
            new DirtsideChitPotDto([new DirtsideNumericalChitsDto("Red", 0, 5001)], []));

        var error = Assert.Throws<InvalidOperationException>(() => service.CreateGame(request));
        Assert.Contains("more than this server will shuffle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameChitCountedTwiceTotalsRatherThanReplacing()
    {
        var service = new DirtsideGameService();
        var created = service.CreateGame(new CreateDirtsideGameRequest(
            "Ridge 9",
            new DirtsideChitPotDto(
                [new DirtsideNumericalChitsDto("Red", 0, 2), new DirtsideNumericalChitsDto("red", 0, 3)],
                [])));

        var only = Assert.Single(created.Snapshot.ChitPot!.Numericals);
        Assert.Equal(5, only.Count);
    }

    private static Guid Activated(DirtsideGameService service, DirtsideChitPotDto pot)
    {
        var created = service.CreateGame(new CreateDirtsideGameRequest("Ridge 9", pot));
        service.AddPlatoon(created.GameId, DirtsideGameServiceTests.Platoon("alpha", "Alpha Troop", "blue"));
        service.AddPlatoon(created.GameId, DirtsideGameServiceTests.Platoon("bravo", "Bravo Troop", "red"));
        service.BeginTurn(created.GameId);
        service.ChooseFirstActivator(created.GameId, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(created.GameId, new BeginDirtsideActivationRequest("blue", "alpha"));
        return created.GameId;
    }

    /// <summary>
    /// The module's document for a real game, lifted out of a row the current writer produced, so
    /// that the hand-built legacy row differs from a current one in exactly one thing: no settings.
    /// </summary>
    private static string SavedGame()
    {
        var store = new MemoryStore();
        var id = DirtsideGameServiceTests.Table(new DirtsideGameService(null, store));
        var row = store.LoadAll().Single(saved => saved.MatchId == id).State;
        return JsonDocument.Parse(row).RootElement.GetProperty("game").GetRawText();
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
