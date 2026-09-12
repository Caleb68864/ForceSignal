using System.Globalization;
using ForceSignal.Application.Ground;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;
using ForceSignal.TestSupport;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Covers StarGrunt's range table being the players' and belonging to one game.
/// </summary>
/// <remarks>
/// <para>
/// The range page was the last rules table this app still shipped: a band was the firer's quality die
/// read as inches, the target's die walked up the ladder one rung per band from the bottom, soft and
/// hard cover were worth one and two rungs, being dug in one more, and the reach was the ladder's
/// length. It is <c>StarGruntRulesProfile</c> now, entered per game.
/// </para>
/// <para>
/// As with Dirtside's tables, the behaviour worth pinning is what happens when the players have not
/// entered something - a refusal that names the entry, lands before the volley is spent, asks only for
/// what the shot reads, and does not cost a stored game its evening.
/// </para>
/// </remarks>
public sealed class StarGruntRulesProfileTests
{
    private static readonly int[] AKillAndAStop = [6, 7, 5, 4, 5, 3, 5, 9, 4];

    [Fact]
    public void AGameWithNoTableRefusesItsFirstShotAndSaysWhichEntryItWants()
    {
        var service = new StarGruntGameService(new ScriptedQualityDice(AKillAndAStop), null, new ScriptedFigureAllocator());
        var game = Activated(service, StarGruntTestProfile.LeadershipValuesOnly);

        var refused = Assert.Throws<InvalidOperationException>(() => service.Fire(game, Volley()));

        // The entry, not "your profile is incomplete". A table reading this knows which line of their
        // own rulebook to go and type in.
        Assert.Contains("range band is for D8 troops", refused.Message, StringComparison.Ordinal);
        Assert.Contains("rules profile", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRefusedShotIsNotChargedToTheUnit()
    {
        var service = new StarGruntGameService(new ScriptedQualityDice(AKillAndAStop), null, new ScriptedFigureAllocator());
        var game = Activated(service, StarGruntTestProfile.LeadershipValuesOnly);
        var before = service.GetSnapshot(game);

        Assert.Throws<InvalidOperationException>(() => service.Fire(game, Volley()));

        // The rifles are still loaded, the target untouched, and nothing was stored or logged.
        var snapshot = service.GetSnapshot(game);
        var rifles = snapshot.Units.Single(unit => unit.Id == "alpha").WeaponLegality.Single(weapon => weapon.Name == "Rifles");
        Assert.True(rifles.CanFire, rifles.Blocker);
        Assert.Equal(8, snapshot.Units.Single(unit => unit.Id == "bravo").FiguresAlive);
        Assert.Equal(before.Version, snapshot.Version);
        Assert.Equal(before.Log, snapshot.Log);
    }

    [Fact]
    public void TheSameShotGoesThroughOnceTheTableIsThere()
    {
        // The control that must be accepted: the same volley on the same dice, the table entered.
        var service = new StarGruntGameService(new ScriptedQualityDice(AKillAndAStop), null, new ScriptedFigureAllocator());
        var game = Activated(service, StarGruntTestProfile.Invented);

        var fired = service.Fire(game, Volley());

        Assert.Equal(7, fired.Units.Single(unit => unit.Id == "bravo").FiguresAlive);
    }

    [Fact]
    public void APartlyFilledTableSettlesTheShotsItHasEntriesFor()
    {
        // One band width, one row and one cover shift: exactly what the volley reads. The over-strict
        // trap this project has paid for twice would refuse this for the rows it never asks about.
        var partial = new StarGruntRulesProfileDto(
            BandWidths: [new StarGruntBandWidthDto(8, 7)],
            RangeDice: [new StarGruntRangeDieDto(2, 4)],
            SoftCoverShift: 2,
            LowestLeadershipValue: 2,
            HighestLeadershipValue: 5);
        var service = new StarGruntGameService(new ScriptedQualityDice(AKillAndAStop), null, new ScriptedFigureAllocator());
        var game = Activated(service, partial);

        // Hard cover is not on it, and the refusal says so rather than borrowing soft cover's number.
        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.Fire(game, Volley() with { Cover = "Hard" }));
        Assert.Contains("hard cover", refused.Message, StringComparison.Ordinal);

        var fired = service.Fire(game, Volley());
        Assert.Equal(7, fired.Units.Single(unit => unit.Id == "bravo").FiguresAlive);
    }

    [Fact]
    public void AMeleeInCoverReadsWhatCoverIsWorthOffThisGamesProfile()
    {
        // The melee cover shift was this module's `CoverShift = 1`. Through the service it is the
        // game's own entry: refused by name when missing, never asked in the open, read when there.
        var blank = new StarGruntGameService(new ScriptedQualityDice(3, 9, 3, 9));
        var game = Activated(blank, StarGruntTestProfile.LeadershipValuesOnly);
        var inCover = new StarGruntMeleeRequest("alpha", "bravo", [new StarGruntMeleePairingDto()], DefendersInCover: true);

        var refused = Assert.Throws<InvalidOperationException>(() => blank.FightMelee(game, inCover));
        Assert.Contains("cover is worth to a defender", refused.Message, StringComparison.Ordinal);

        // The control on the same blank table: in the open, the round is fought.
        var open = blank.FightMelee(game, inCover with { DefendersInCover = false });
        Assert.Contains(open.Log, entry => entry.Contains("D8 3 against D8 9", StringComparison.Ordinal));

        // And with the table entered, the defender's die is moved by its two invented rungs.
        var entered = new StarGruntGameService(new ScriptedQualityDice(3, 9));
        var withTable = Activated(entered, StarGruntTestProfile.Invented);
        var fought = entered.FightMelee(withTable, inCover);
        Assert.Contains(fought.Log, entry => entry.Contains("D8 3 against D12 9", StringComparison.Ordinal));
    }

    [Fact]
    public void ASnapshotReadsBackTheTableThePlayersEntered()
    {
        var service = new StarGruntGameService();
        var created = service.CreateGame(StarGruntTestProfile.CreateGame("Hill 43"));

        var profile = service.GetSnapshot(created.GameId).Profile!;

        Assert.Equal(StarGruntTestProfile.Invented.BandWidths, profile.BandWidths);
        Assert.Equal(StarGruntTestProfile.Invented.RangeDice, profile.RangeDice);
        Assert.Equal(4, profile.EffectiveBands);
        Assert.Equal(2, profile.SoftCoverShift);
        Assert.Equal(4, profile.HardCoverShift);
        Assert.Equal(3, profile.InPositionShift);
        Assert.Equal(2, profile.MeleeCoverShift);
    }

    [Fact]
    public void AGameWithNoTableSaysSoOnEverySnapshotRatherThanOnlyWhenItRefuses()
    {
        var service = new StarGruntGameService();
        var created = service.CreateGame(new CreateStarGruntGameRequest("Hill 43"));

        var profile = service.GetSnapshot(created.GameId).Profile;

        // Present and empty - not absent, which a screen could not tell from an older server.
        Assert.NotNull(profile);
        Assert.Empty(profile.BandWidths!);
        Assert.Empty(profile.RangeDice!);
        Assert.Null(profile.EffectiveBands);
        Assert.Null(profile.SoftCoverShift);
    }

    [Fact]
    public void ATableSurvivesARestart()
    {
        var store = new MemoryStore();
        var before = new StarGruntGameService(null, store);
        var game = before.CreateGame(StarGruntTestProfile.CreateGame("Hill 43")).GameId;

        var restarted = new StarGruntGameService(null, store);

        Assert.Empty(restarted.SkippedSaves);
        Assert.Equal(before.GetSnapshot(game).Profile, restarted.GetSnapshot(game).Profile, ProfileComparer.Instance);
    }

    [Fact]
    public void AGameWithNoTableWritesTheRowItAlwaysWrote()
    {
        // No settings at all when nothing was entered, so the row a blank-table game writes is the row
        // this service wrote before tables existed, and an older build reading it back sees nothing new.
        var store = new MemoryStore();
        var game = new StarGruntGameService(null, store).CreateGame(new CreateStarGruntGameRequest("Hill 43")).GameId;

        Assert.DoesNotContain("settings", store.Rows[game], StringComparison.Ordinal);

        // The control: a game with a table does write one, or the restart above proves nothing.
        var withTable = new StarGruntGameService(null, store).CreateGame(StarGruntTestProfile.CreateGame("Hill 43")).GameId;
        Assert.Contains("\"settings\"", store.Rows[withTable], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"settings\":null")]
    public void AGameStoredBeforeTablesExistedStillOpens(string settings)
    {
        // The one that would have bitten. GroundGameRecord skips a row it cannot read rather than
        // migrating it, so a required field would have retired every stored StarGrunt game on the
        // machine in silence. This row is written by hand the way the service wrote it before this
        // change - format 1, a token, the module's document, and no settings - rather than by the
        // service under test, which could only ever write the new shape.
        var id = Guid.NewGuid();
        var document = StarGruntGameSerialization.Save(
            StarGruntGame.Create("Hill 43")
                .WithUnit(Squad("alpha", "Alpha Squad", "blue"))
                .WithUnit(Squad("bravo", "Bravo Squad", "red")));
        var store = new MemoryStore();
        store.Seed(id, OldRow(document, settings));

        var service = new StarGruntGameService(new ScriptedQualityDice(AKillAndAStop), store, new ScriptedFigureAllocator());

        Assert.Empty(service.SkippedSaves);
        Assert.Equal(2, service.GetSnapshot(id).Units.Count);
        Assert.Empty(service.GetSnapshot(id).Profile!.BandWidths!);

        // It plays: a turn opens and a unit activates. Its first shot is refused by name, which is the
        // whole cost of having been stored before the table was the players'.
        service.BeginTurn(id);
        service.ChooseFirstActivator(id, new ChooseFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(id, new BeginStarGruntActivationRequest("blue", "alpha"));
        var refused = Assert.Throws<InvalidOperationException>(() => service.Fire(id, Volley()));
        Assert.Contains("range table", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATableThisVersionCannotReadCostsTheTableAndNotTheGame()
    {
        // A settings blob that is present and not this version's shape - a die renamed, a field of
        // another type. Read in its own try, so it comes back blank and the game still opens.
        var id = Guid.NewGuid();
        var document = StarGruntGameSerialization.Save(StarGruntGame.Create("Hill 43"));
        var store = new MemoryStore();
        store.Seed(id, OldRow(document, ",\"settings\":{\"profile\":{\"bandWidths\":[{\"qualityDie\":7,\"inches\":3}]}}"));

        var service = new StarGruntGameService(null, store);

        Assert.Empty(service.SkippedSaves);
        Assert.Empty(service.GetSnapshot(id).Profile!.BandWidths!);
    }

    [Theory]
    [InlineData(7, 3, "quality ladder")]
    [InlineData(8, 0, "0 inches")]
    [InlineData(8, -2, "-2 inches")]
    public void ABandWidthRowThatIsNotOneIsRefused(int quality, int inches, string named)
    {
        var refused = Assert.Throws<InvalidOperationException>(() => Create(new StarGruntRulesProfileDto(
            BandWidths: [new StarGruntBandWidthDto(quality, inches)])));

        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 4, "count from 1")]
    [InlineData(1, 5, "quality ladder")]
    public void ARangeDieRowThatIsNotOneIsRefused(int bandsOut, int die, string named)
    {
        var refused = Assert.Throws<InvalidOperationException>(() => Create(new StarGruntRulesProfileDto(
            RangeDice: [new StarGruntRangeDieDto(bandsOut, die)])));

        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANumberNobodyCouldHaveReadOffAPageIsRefusedRatherThanClamped()
    {
        Assert.Throws<InvalidOperationException>(() => Create(new StarGruntRulesProfileDto(SoftCoverShift: -1)));
        Assert.Throws<InvalidOperationException>(() => Create(new StarGruntRulesProfileDto(HardCoverShift: 101)));
        Assert.Throws<InvalidOperationException>(() => Create(new StarGruntRulesProfileDto(EffectiveBands: 0)));
    }

    [Fact]
    public void ZeroIsARealAnswerForACoverShift()
    {
        // The control for the refusal above, and the reason the shifts are nullable: "this cover does
        // nothing in our rules" is a thing a table may enter, and it must not read back as unentered.
        var service = new StarGruntGameService();
        var created = service.CreateGame(new CreateStarGruntGameRequest(
            "Hill 43", new StarGruntRulesProfileDto(SoftCoverShift: 0, InPositionShift: 0)));

        var profile = service.GetSnapshot(created.GameId).Profile!;
        Assert.Equal(0, profile.SoftCoverShift);
        Assert.Equal(0, profile.InPositionShift);
        Assert.Null(profile.HardCoverShift);
    }

    [Fact]
    public void ARowTypedTwiceIsTheLastOneTyped()
    {
        // A correction at a table, not a refusal.
        var service = new StarGruntGameService();
        var created = service.CreateGame(new CreateStarGruntGameRequest(
            "Hill 43",
            new StarGruntRulesProfileDto(BandWidths: [new StarGruntBandWidthDto(8, 5), new StarGruntBandWidthDto(8, 7)])));

        Assert.Equal([new StarGruntBandWidthDto(8, 7)], service.GetSnapshot(created.GameId).Profile!.BandWidths);
    }

    [Fact]
    public void ATableWithMoreRowsThanThisServerWillHoldIsRefused()
    {
        // The row count arrives off an unauthenticated create route and becomes the size of a
        // dictionary. This sends an amount no rulebook has and asserts that something stops it, rather
        // than restating the mapping's own ceiling and pinning the two together for no reason.
        var rows = Enumerable.Range(1, 5000)
            .Select(band => new StarGruntRangeDieDto(band, 4))
            .ToArray();

        var refused = Assert.Throws<InvalidOperationException>(() => Create(new StarGruntRulesProfileDto(RangeDice: rows)));
        Assert.Contains("more than this server will hold", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A stored row written by hand in the shape this service wrote before range tables existed:
    /// format 1, a token, the time, the module's document, and whatever trails it.
    /// </summary>
    private static string OldRow(string document, string trailing) =>
        string.Concat(
            "{\"formatVersion\":1,\"token\":\"old-token\",\"lastActivity\":\"",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            "\",\"game\":",
            document,
            trailing,
            "}");

    private static StarGruntGameCreatedResponse Create(StarGruntRulesProfileDto profile) =>
        new StarGruntGameService().CreateGame(new CreateStarGruntGameRequest("Hill 43", profile));

    /// <summary>Alpha's rifles at Bravo, nine inches away in soft cover.</summary>
    private static StarGruntFireRequest Volley() =>
        new("alpha", "bravo", "Rifles", FirepowerDie: 10, SupportWeapons: [], DistanceInches: 9, Cover: "Soft");

    /// <summary>Two squads on the table and Alpha activated, on whatever table the test hands in.</summary>
    private static Guid Activated(StarGruntGameService service, StarGruntRulesProfileDto? profile)
    {
        var game = service.CreateGame(new CreateStarGruntGameRequest("Hill 43", profile)).GameId;
        service.AddUnit(game, SquadRequest("alpha", "Alpha Squad", "blue"));
        service.AddUnit(game, SquadRequest("bravo", "Bravo Squad", "red"));
        service.BeginTurn(game);
        service.ChooseFirstActivator(game, new ChooseFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(game, new BeginStarGruntActivationRequest("blue", "alpha"));
        return game;
    }

    private static AddStarGruntUnitRequest SquadRequest(string id, string name, string side) => new(
        id,
        name,
        side,
        "Squad",
        QualityDie: 8,
        LeadershipValue: 2,
        Figures: [.. Enumerable.Repeat(new StarGruntFigureDto(6), 8)],
        Weapons: [new StarGruntWeaponDto("Rifles", 10)]);

    /// <summary>A squad as the module holds it, for a row written by hand.</summary>
    private static UnitDefinition Squad(string id, string name, string side) => new()
    {
        Id = new UnitId(id),
        Name = name,
        Side = new SideId(side),
        Level = CommandLevel.Squad,
        QualityDie = QualityDie.D8,
        LeadershipValue = 2,
        Figures = [.. Enumerable.Repeat(new FigureProfile(QualityDie.D6), 8)],
        Weapons = [new WeaponProfile { Name = "Rifles", ImpactDie = QualityDie.D10 }],
    };

    /// <summary>Compares two wire profiles by their contents, since their lists compare by reference.</summary>
    private sealed class ProfileComparer : IEqualityComparer<StarGruntRulesProfileDto?>
    {
        public static ProfileComparer Instance { get; } = new();

        public bool Equals(StarGruntRulesProfileDto? x, StarGruntRulesProfileDto? y) =>
            x is not null && y is not null
            && (x.BandWidths ?? []).SequenceEqual(y.BandWidths ?? [])
            && (x.RangeDice ?? []).SequenceEqual(y.RangeDice ?? [])
            && x.EffectiveBands == y.EffectiveBands
            && x.SoftCoverShift == y.SoftCoverShift
            && x.HardCoverShift == y.HardCoverShift
            && x.InPositionShift == y.InPositionShift
            && x.MeleeCoverShift == y.MeleeCoverShift;

        public int GetHashCode(StarGruntRulesProfileDto? obj) => 0;
    }
}
