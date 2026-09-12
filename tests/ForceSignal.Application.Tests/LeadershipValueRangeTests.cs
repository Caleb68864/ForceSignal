using ForceSignal.Application.Ground;
using ForceSignal.Contracts.Ground;
using ForceSignal.TestSupport;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Which numbers count as Leadership Values is the players', in both ground games.
/// </summary>
/// <remarks>
/// <para>
/// The two engines used to disagree, and both were wrong. StarGrunt's service held
/// <c>value is &gt;= 1 and &lt;= 3</c> and refused anything else by reciting the bound - "not one the
/// rules use (1 to 3, 1 best)" - which is a published page being read out to the player. Dirtside's
/// held nothing at all: a probe put 99 and -4 on a command marker and both went into the game
/// verbatim, to be added to a threat level and rolled against.
/// </para>
/// <para>
/// The bound is a reading off somebody's rulebook, so it belongs on the rules profile with every
/// other number those games read. These tests are written against an invented range of 2 to 5, which
/// is deliberately the opposite way round from the bound that used to be in the source: a card marked
/// 1 must now be refused and a card marked 4 must go on the table. A guard that still knew about
/// 1 to 3 would fail both halves.
/// </para>
/// <para>
/// <b>Where the range is read.</b> At the door a Leadership Value comes in through, in both services,
/// which is where the profile is in hand and where the question "is this a Leadership Value?" is
/// actually being asked. No roll reads the bound: a confidence test adds leadership to threat and
/// rolls, and nothing in that arithmetic consults an end of the range. A game stored before the range
/// existed therefore opens untouched, with whatever its cards already said, and is refused by name at
/// the next number somebody tries to enter.
/// </para>
/// </remarks>
public sealed class LeadershipValueRangeTests
{
    /// <summary>The sentence the old refusal recited, which must not come back.</summary>
    private const string RecitedBound = "1 to 3";

    [Fact]
    public void AStarGruntCardBelowTheEnteredRangeIsRefusedAndNamesTheEntry()
    {
        var service = new StarGruntGameService();
        var game = service.CreateGame(StarGruntTestProfile.CreateGame("Hill 43")).GameId;

        // 1 was the best value the old bound knew and is below the one these players entered.
        var refused = Assert.Throws<InvalidOperationException>(
            () => service.AddUnit(game, Squad(leadershipValue: 1)));

        Assert.Contains("Leadership Value", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Alpha Squad", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RecitedBound, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStarGruntCardAboveTheEnteredRangeIsRefusedAndNamesTheEntry()
    {
        var service = new StarGruntGameService();
        var game = service.CreateGame(StarGruntTestProfile.CreateGame("Hill 43")).GameId;

        var refused = Assert.Throws<InvalidOperationException>(
            () => service.AddUnit(game, Squad(leadershipValue: 6)));

        Assert.Contains("Leadership Value", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RecitedBound, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    public void AStarGruntCardInsideTheEnteredRangeGoesOnTheTable(int value)
    {
        // The accept control, and the half that catches a guard which refuses everything. 4 and 5 are
        // the interesting ones: the bound that used to be in the source refused both.
        var service = new StarGruntGameService();
        var game = service.CreateGame(StarGruntTestProfile.CreateGame("Hill 43")).GameId;

        var snapshot = service.AddUnit(game, Squad(leadershipValue: value));

        Assert.Equal(value, snapshot.Units.Single().LeadershipValue);
    }

    [Fact]
    public void AStarGruntGameWithNoLeadershipValuesEnteredRefusesACardAndNamesTheEntry()
    {
        var service = new StarGruntGameService();
        var game = service.CreateGame(StarGruntTestProfile.CreateGameWithNoLeadershipValues("Hill 43")).GameId;

        var refused = Assert.Throws<InvalidOperationException>(
            () => service.AddUnit(game, Squad(leadershipValue: 2)));

        Assert.Contains("Leadership Value", refused.Message, StringComparison.Ordinal);
        Assert.Contains("entered", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RecitedBound, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-4)]
    [InlineData(1)]
    public void ADirtsideCommandMarkerOutsideTheEnteredRangeIsRefused(int value)
    {
        // 99 and -4 are the two the probe put on a marker and watched the service store. 1 is the
        // one the other engine's old bound called best, and is outside what these players entered.
        var service = new DirtsideGameService();
        var game = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9")).GameId;

        var refused = Assert.Throws<InvalidOperationException>(
            () => service.AddPlatoon(game, Platoon(leadershipValue: value)));

        Assert.Contains("Leadership Value", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Alpha Troop", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RecitedBound, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    public void ADirtsideCommandMarkerInsideTheEnteredRangeGoesOnTheTable(int value)
    {
        var service = new DirtsideGameService();
        var game = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9")).GameId;

        var snapshot = service.AddPlatoon(game, Platoon(leadershipValue: value));

        Assert.Equal(value, snapshot.Units.Single().LeadershipValue);
    }

    [Fact]
    public void ADirtsideCommandMarkerThatSaysNothingIsStillAcceptedWithNoRangeEntered()
    {
        // Dirtside's leadership is optional on the card and stays optional: a platoon whose marker
        // does not say is not an error, it is a platoon whose nerve cannot be tested yet - and the
        // engine already refuses that roll by name. Nothing is being looked up, so no entry can be
        // missing, and a range nobody entered must not retire the platoon.
        var service = new DirtsideGameService();
        var game = service.CreateGame(DirtsideTestProfile.CreateGameWithNoLeadershipValues("Ridge 9")).GameId;

        var snapshot = service.AddPlatoon(game, Platoon(leadershipValue: null));

        Assert.Null(snapshot.Units.Single().LeadershipValue);
    }

    [Fact]
    public void ADirtsideGameWithNoLeadershipValuesEnteredRefusesAMarkerThatDoesSay()
    {
        var service = new DirtsideGameService();
        var game = service.CreateGame(DirtsideTestProfile.CreateGameWithNoLeadershipValues("Ridge 9")).GameId;

        var refused = Assert.Throws<InvalidOperationException>(
            () => service.AddPlatoon(game, Platoon(leadershipValue: 2)));

        Assert.Contains("Leadership Value", refused.Message, StringComparison.Ordinal);
        Assert.Contains("entered", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RecitedBound, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HalfARangeIsRefusedWhereItIsEnteredAndNamesTheEndThatIsMissing()
    {
        var service = new StarGruntGameService();

        var refused = Assert.Throws<InvalidOperationException>(() => service.CreateGame(
            new CreateStarGruntGameRequest(
                "Hill 43",
                StarGruntTestProfile.Invented with { HighestLeadershipValue = null })));

        Assert.Contains("highest", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARangeTheWrongWayRoundIsRefusedWhereItIsEntered()
    {
        var service = new DirtsideGameService();

        var refused = Assert.Throws<InvalidOperationException>(() => service.CreateGame(
            new CreateDirtsideGameRequest(
                "Ridge 9",
                null,
                DirtsideTestProfile.Invented with { LowestLeadershipValue = 5, HighestLeadershipValue = 2 })));

        Assert.Contains("above", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEnteredRangeComesBackOnTheSnapshotSoAScreenCanSayWhatItIs()
    {
        var service = new StarGruntGameService();
        var created = service.CreateGame(StarGruntTestProfile.CreateGame("Hill 43"));

        Assert.Equal(2, created.Snapshot.Profile!.LowestLeadershipValue);
        Assert.Equal(5, created.Snapshot.Profile.HighestLeadershipValue);
    }

    [Fact]
    public void AStarGruntGameStoredBeforeTheRangeExistedStillOpensAndThenSaysWhatItWants()
    {
        var store = new MemoryStore();
        var first = new StarGruntGameService(null, store);
        var game = first.CreateGame(StarGruntTestProfile.CreateGame("Hill 43")).GameId;
        first.AddUnit(game, Squad(leadershipValue: 4));

        StripLeadershipValues(store, game);

        // It opened: nothing was skipped, and the squad is still on the table with the number its
        // card carried. A row written before the entry existed must never be retired over it.
        var reopened = new StarGruntGameService(null, store);
        Assert.Empty(reopened.SkippedSaves);

        var snapshot = reopened.GetSnapshot(game);
        Assert.Equal(4, snapshot.Units.Single().LeadershipValue);
        Assert.Null(snapshot.Profile!.LowestLeadershipValue);

        // And the next card somebody tries to enter is refused by name rather than accepted on no
        // authority at all.
        var refused = Assert.Throws<InvalidOperationException>(
            () => reopened.AddUnit(game, Squad(leadershipValue: 4) with { Id = "bravo", Name = "Bravo Squad" }));

        Assert.Contains("Leadership Value", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RecitedBound, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADirtsideGameStoredBeforeTheRangeExistedStillOpensAndThenSaysWhatItWants()
    {
        var store = new MemoryStore();
        var first = new DirtsideGameService(null, store);
        var game = first.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9")).GameId;
        first.AddPlatoon(game, Platoon(leadershipValue: 4));

        StripLeadershipValues(store, game);

        var reopened = new DirtsideGameService(null, store);
        Assert.Empty(reopened.SkippedSaves);

        var snapshot = reopened.GetSnapshot(game);
        Assert.Equal(4, snapshot.Units.Single().LeadershipValue);
        Assert.Null(snapshot.Profile!.LowestLeadershipValue);

        var refused = Assert.Throws<InvalidOperationException>(
            () => reopened.AddPlatoon(game, Platoon(leadershipValue: 4) with { Id = "bravo", Name = "Bravo Troop" }));

        Assert.Contains("Leadership Value", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RecitedBound, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Takes the two Leadership Value entries out of a stored row, standing in for a version that
    /// never wrote them.
    /// </summary>
    private static void StripLeadershipValues(MemoryStore store, Guid game)
    {
        var row = store.Rows[game];
        var stripped = row
            .Replace("\"lowestLeadershipValue\":2,", string.Empty, StringComparison.Ordinal)
            .Replace("\"highestLeadershipValue\":5,", string.Empty, StringComparison.Ordinal)
            .Replace(",\"lowestLeadershipValue\":2", string.Empty, StringComparison.Ordinal)
            .Replace(",\"highestLeadershipValue\":5", string.Empty, StringComparison.Ordinal);

        // Reached-the-subject: a rewrite that matched nothing would leave this testing an ordinary
        // stored game and passing for the wrong reason.
        Assert.NotEqual(row, stripped);
        Assert.DoesNotContain("LeadershipValue\":2", stripped, StringComparison.OrdinalIgnoreCase);
        store.Seed(game, stripped);
    }

    private static AddStarGruntUnitRequest Squad(int leadershipValue) => new(
        "alpha",
        "Alpha Squad",
        "blue",
        "Squad",
        QualityDie: 8,
        LeadershipValue: leadershipValue,
        Figures: [new StarGruntFigureDto(6)],
        Weapons: [new StarGruntWeaponDto("Rifles", 10)]);

    private static AddDirtsidePlatoonRequest Platoon(int? leadershipValue) =>
        DirtsideGameServiceTests.Platoon("alpha", "Alpha Troop", "blue") with
        {
            QualityDie = "D8",
            LeadershipValue = leadershipValue,
        };
}
