using System.Globalization;
using System.Text.RegularExpressions;
using ForceSignal.Application.Ground;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Game;
using ForceSignal.Modules.StarGrunt.Game;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The guards around the two ground-combat services: the token that opens a game, the ceilings
/// on what a game may hold, the retention that retires an abandoned one, and what happens to a
/// stored row that cannot be read.
/// </summary>
/// <remarks>
/// Neither of these is a rules test. Every one of them is about a request that needs no
/// credentials, or only a game token, being unable to do the server any harm.
/// </remarks>
public sealed partial class GroundGameHardeningTests
{
    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HexToken();

    [GeneratedRegex("\"lastActivity\":\"[^\"]*\"")]
    private static partial Regex LastActivity();

    [Fact]
    public void CreatingAGameMintsATokenThatOpensItAndNothingElse()
    {
        var starGrunt = new StarGruntGameService();
        var dirtside = new DirtsideGameService();
        var infantry = starGrunt.CreateGame(new CreateStarGruntGameRequest("Hill 43"));
        var armour = dirtside.CreateGame(new CreateDirtsideGameRequest("Ridge 9"));

        // The same shape as a Full Thrust participant token: 256 bits, hex, minted once.
        Assert.Matches(HexToken(), infantry.Token);
        Assert.Matches(HexToken(), armour.Token);
        Assert.NotEqual(infantry.Token, armour.Token);

        starGrunt.RequireToken(infantry.GameId, infantry.Token);
        dirtside.RequireToken(armour.GameId, armour.Token);

        Assert.Throws<UnauthorizedAccessException>(() => starGrunt.RequireToken(infantry.GameId, armour.Token));
        Assert.Throws<UnauthorizedAccessException>(() => dirtside.RequireToken(armour.GameId, infantry.Token));
        Assert.Throws<UnauthorizedAccessException>(() => starGrunt.RequireToken(infantry.GameId, "   "));
        Assert.Throws<NotFoundException>(() => starGrunt.RequireToken(Guid.NewGuid(), infantry.Token));
        Assert.Throws<NotFoundException>(() => dirtside.RequireToken(Guid.NewGuid(), armour.Token));
    }

    [Fact]
    public void TheTokenSurvivesARestart()
    {
        // A restart that minted a new token would lock every device at the table out of a game
        // that is otherwise still there - which is why it is stored with the game.
        var store = new MemoryStore();
        var created = new StarGruntGameService(null, store).CreateGame(new CreateStarGruntGameRequest("Hill 43"));

        var restarted = new StarGruntGameService(null, store);

        restarted.RequireToken(created.GameId, created.Token);
        Assert.Throws<UnauthorizedAccessException>(() => restarted.RequireToken(created.GameId, new string('0', 64)));
    }

    [Fact]
    public void ARowFromBeforeTokensExistedIsSkippedAndSaysWhy()
    {
        var store = new MemoryStore();
        store.Save(Guid.NewGuid(), StarGruntGameSerialization.Save(StarGruntGame.Create("Old Save")));
        store.Save(Guid.NewGuid(), DirtsideGameSerialization.Save(DirtsideGame.Create("Old Save")));

        // A game that came back without a token is a game nobody can play, which is no better than
        // one that is gone and harder to explain - so it is skipped, and the skip is on record.
        var starGrunt = new StarGruntGameService(null, store);
        var dirtside = new DirtsideGameService(null, store);

        Assert.Equal(2, starGrunt.SkippedSaves.Count);
        Assert.Equal(2, dirtside.SkippedSaves.Count);
        Assert.All(starGrunt.SkippedSaves, skip => Assert.False(string.IsNullOrWhiteSpace(skip.Reason)));
    }

    [Fact]
    public void ARowThatParsesButHoldsNoGameIsSkippedRatherThanTakingTheServiceDown()
    {
        var store = new MemoryStore();
        var good = new DirtsideGameService(null, store).CreateGame(new CreateDirtsideGameRequest("Ridge 9"));

        // Right format, right token shape, and a game with no roster. This got past the parser and
        // then failed being rebuilt - and the old catch did not reach that far, so every Dirtside
        // route answered 500 for the life of the process.
        var hollow = Guid.NewGuid();
        store.Save(hollow, Envelope(new string('a', 64), DateTimeOffset.UtcNow, """{"Name":"Hollow"}"""));

        var service = new DirtsideGameService(null, store);

        Assert.Equal("Ridge 9", service.GetSnapshot(good.GameId).Name);
        var skipped = Assert.Single(service.SkippedSaves);
        Assert.Equal(hollow, skipped.MatchId);
    }

    [Fact]
    public void AnIdleGameIsRetiredWhenAnotherIsOpened_AndALiveOneIsNot()
    {
        var store = new MemoryStore();
        var service = new StarGruntGameService(null, store);
        var idle = service.CreateGame(new CreateStarGruntGameRequest("Idle"));
        var live = service.CreateGame(new CreateStarGruntGameRequest("Live"));

        // The clock is not injectable, so the stored row is aged instead - which is also what a
        // server restarted after a long weekend sees.
        store.Age(idle.GameId, TimeSpan.FromDays(2));
        var restarted = new StarGruntGameService(null, store);
        restarted.CreateGame(new CreateStarGruntGameRequest("Newcomer"));

        Assert.Throws<NotFoundException>(() => restarted.GetSnapshot(idle.GameId));
        Assert.Equal("Live", restarted.GetSnapshot(live.GameId).Name);

        // Retired is retired: the stored copy went too, or the next restart would undo it.
        Assert.DoesNotContain(store.LoadAll(), row => row.MatchId == idle.GameId);
    }

    [Fact]
    public void AtTheCeilingANewGameIsRefusedRatherThanRetiringALiveOne()
    {
        var service = new DirtsideGameService();
        var first = service.CreateGame(new CreateDirtsideGameRequest("First"));
        for (var i = 1; i < 200; i++)
        {
            service.CreateGame(new CreateDirtsideGameRequest($"Game {i}"));
        }

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.CreateGame(new CreateDirtsideGameRequest("One Too Many")));

        Assert.Contains("as many as it holds", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("First", service.GetSnapshot(first.GameId).Name);
    }

    [Fact]
    public void AGameHoldsOnlySoManyUnits()
    {
        var starGrunt = new StarGruntGameService();
        var infantry = starGrunt.CreateGame(new CreateStarGruntGameRequest("Hill 43")).GameId;
        for (var i = 0; i < 200; i++)
        {
            starGrunt.AddUnit(infantry, Squad($"unit-{i}", i % 2 == 0 ? "blue" : "red", figures: 1));
        }

        var refused = Assert.Throws<InvalidOperationException>(() =>
            starGrunt.AddUnit(infantry, Squad("one-too-many", "blue", figures: 1)));
        Assert.Contains("as many as it tracks", refused.Message, StringComparison.OrdinalIgnoreCase);

        var dirtside = new DirtsideGameService();
        var armour = dirtside.CreateGame(new CreateDirtsideGameRequest("Ridge 9")).GameId;
        for (var i = 0; i < 200; i++)
        {
            dirtside.AddPlatoon(armour, DirtsideGameServiceTests.Platoon($"platoon-{i}", "Troop", i % 2 == 0 ? "blue" : "red"));
        }

        Assert.Throws<InvalidOperationException>(() =>
            dirtside.AddPlatoon(armour, DirtsideGameServiceTests.Platoon("one-too-many", "Troop", "blue")));
    }

    [Fact]
    public void AUnitCarriesOnlySoManyFiguresElementsAndWeapons()
    {
        var starGrunt = new StarGruntGameService();
        var infantry = starGrunt.CreateGame(new CreateStarGruntGameRequest("Hill 43")).GameId;

        var crowd = Assert.Throws<InvalidOperationException>(() =>
            starGrunt.AddUnit(infantry, Squad("crowd", "blue", figures: 51)));
        Assert.Contains("figures", crowd.Message, StringComparison.OrdinalIgnoreCase);

        var arsenal = Assert.Throws<InvalidOperationException>(() =>
            starGrunt.AddUnit(infantry, Squad("arsenal", "blue", figures: 1) with
            {
                Weapons = [.. Enumerable.Range(0, 41).Select(i => new StarGruntWeaponDto($"Gun {i}", 6))],
            }));
        Assert.Contains("weapons", arsenal.Message, StringComparison.OrdinalIgnoreCase);

        var dirtside = new DirtsideGameService();
        var armour = dirtside.CreateGame(new CreateDirtsideGameRequest("Ridge 9")).GameId;

        var column = Assert.Throws<InvalidOperationException>(() =>
            dirtside.AddPlatoon(armour, DirtsideGameServiceTests.Platoon("column", "Column", "blue") with
            {
                Elements = [.. Enumerable.Range(0, 51).Select(i => DirtsideGameServiceTests.Vehicle($"v-{i}", "Vehicle"))],
            }));
        Assert.Contains("elements", column.Message, StringComparison.OrdinalIgnoreCase);

        var bristling = Assert.Throws<InvalidOperationException>(() =>
            dirtside.AddPlatoon(armour, DirtsideGameServiceTests.Platoon("bristling", "Bristling", "blue") with
            {
                Elements =
                [
                    DirtsideGameServiceTests.Vehicle("v-1", "Vehicle") with
                    {
                        Weapons = [.. Enumerable.Range(0, 41).Select(_ => DirtsideGameServiceTests.MainGun)],
                    },
                ],
            }));
        Assert.Contains("weapons", bristling.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMountFiresOnlySoManyBarrelsHoweverManyItClaimsToHave()
    {
        // The target rolls first and the barrel second, so every barrel that fires hits and lands in
        // the log by name - which is how the count below is taken.
        var service = new DirtsideGameService(
            new ScriptedQualityDice(1, 8),
            null,
            new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 8)));
        var game = service.CreateGame(new CreateDirtsideGameRequest("Ridge 9")).GameId;

        // A mount claiming every barrel an int can hold. The number arrives off the wire and used to
        // be floored at one and left alone above, where it became the trip count of the dice loop in
        // DirectFire - and that loop runs inside the service's lock, so this one request stopped
        // every other game on the server too. StarGruntGame.Assault.cs clamps its downed-figure
        // count with a comment saying exactly this; the same loop one engine over did not.
        service.AddPlatoon(game, DirtsideGameServiceTests.Platoon("alpha", "Alpha Troop", "blue") with
        {
            Elements =
            [
                DirtsideGameServiceTests.Vehicle("alpha-1", "Alpha Troop One") with
                {
                    Weapons = [DirtsideGameServiceTests.MainGun with { Barrels = int.MaxValue }],
                },
            ],
        });
        service.AddPlatoon(game, DirtsideGameServiceTests.Platoon("bravo", "Bravo Troop", "red"));
        service.BeginTurn(game);
        service.ChooseFirstActivator(game, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(game, new BeginDirtsideActivationRequest("blue", "alpha"));

        var fired = service.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close"));

        // The shot writes one roll into the log per barrel that actually fired - "<die> <n> against
        // <m>: hit" - so counting those counts the barrels the engine was willing to roll for.
        // Twenty is GroundGameGuards.MaxBarrelsPerMount, spelt out here because the guards are
        // internal to the application, as the other ceilings in this file are.
        var barrelsFired = fired.Log[^1].Split(" against ", StringSplitOptions.None).Length - 1;
        Assert.InRange(barrelsFired, 1, 20);
    }

    [Fact]
    public void AMeleeRoundNamesOnlySoManyPairings()
    {
        var service = new StarGruntGameService();
        var game = service.CreateGame(new CreateStarGruntGameRequest("Hill 43")).GameId;

        var refused = Assert.Throws<InvalidOperationException>(() => service.FightMelee(game, new StarGruntMeleeRequest(
            "alpha", "bravo", [.. Enumerable.Range(0, 101).Select(_ => new StarGruntMeleePairingDto())])));

        Assert.Contains("pairings", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettlingTheDownedCannotBeAskedToRollForever()
    {
        // Every roll a 1, so every settled figure is dead. The count arrives off the wire; before
        // the clamp, two billion of them rolled two billion dice inside the lock.
        var service = new StarGruntGameService(new ScriptedQualityDice());
        var game = service.CreateGame(new CreateStarGruntGameRequest("Hill 43")).GameId;
        service.AddUnit(game, Squad("alpha", "blue", figures: 8));

        var settled = service.SettleTheDowned(game, new StarGruntSettleDownedRequest(
            "alpha", Downed: int.MaxValue, WonTheAssault: true, DeadUpTo: 2, WoundedUpTo: 4));

        Assert.Contains(settled.Log, entry => entry.Contains("8 dead", StringComparison.Ordinal));
    }

    [Fact]
    public void NamesAreCutToTheDisplayCeiling()
    {
        var tooLong = new string('x', 5000);
        var starGrunt = new StarGruntGameService();
        var infantry = starGrunt.CreateGame(new CreateStarGruntGameRequest(tooLong));
        var unit = starGrunt.AddUnit(infantry.GameId, Squad(tooLong, "blue", figures: 1) with { Name = tooLong })
            .Units.Single();

        Assert.Equal(120, infantry.Snapshot.Name.Length);
        Assert.Equal(120, unit.Id.Length);
        Assert.Equal(120, unit.Name.Length);
        Assert.All(unit.Weapons, weapon => Assert.True(weapon.Name.Length <= 120));

        var dirtside = new DirtsideGameService();
        var armour = dirtside.CreateGame(new CreateDirtsideGameRequest(tooLong));
        var platoon = dirtside.AddPlatoon(armour.GameId, DirtsideGameServiceTests.Platoon("alpha", tooLong, "blue") with
        {
            Elements = [DirtsideGameServiceTests.Vehicle(tooLong, tooLong) with { Weapons = [DirtsideGameServiceTests.MainGun with { Name = tooLong }] }],
        }).Units.Single();

        Assert.Equal(120, armour.Snapshot.Name.Length);
        Assert.Equal(120, platoon.Name.Length);
        Assert.Equal(120, platoon.Elements.Single().Id.Length);
        Assert.Equal(120, platoon.Elements.Single().Name.Length);
        Assert.Equal(120, platoon.Elements.Single().Weapons.Single().Length);
    }

    [Fact]
    public void TheLogKeepsOnlyTheLatestLines()
    {
        // A game with a log far past the ceiling, put down and picked up again. The whole log rides
        // in every snapshot and the whole game is rewritten to disk on every command, so the oldest
        // lines go the moment the game next changes.
        var store = new MemoryStore();
        var game = StarGruntGame.Create("Long Night");
        for (var i = 0; i < 2500; i++)
        {
            game = game.WithLog($"line {i}");
        }

        var id = Guid.NewGuid();
        var token = new string('b', 64);
        store.Save(id, Envelope(token, DateTimeOffset.UtcNow, StarGruntGameSerialization.Save(game)));

        var service = new StarGruntGameService(null, store);
        service.RequireToken(id, token);
        var snapshot = service.AddUnit(id, Squad("alpha", "blue", figures: 1));

        Assert.Equal(2000, snapshot.Log.Count);
        Assert.DoesNotContain("line 0", snapshot.Log);
        Assert.Contains("line 2499", snapshot.Log);
    }

    private static AddStarGruntUnitRequest Squad(string id, string side, int figures) => new(
        id,
        $"{id} Squad",
        side,
        "Squad",
        QualityDie: 8,
        LeadershipValue: 2,
        Figures: [.. Enumerable.Repeat(new StarGruntFigureDto(6), figures)],
        Weapons: [new StarGruntWeaponDto("Rifles", 10)]);

    /// <summary>The row shape the services write, built by hand so a test can put a known one in the store.</summary>
    private static string Envelope(string token, DateTimeOffset lastActivity, string game) =>
        $$"""{"formatVersion":1,"token":"{{token}}","lastActivity":"{{lastActivity.ToString("o", CultureInfo.InvariantCulture)}}","game":{{game}}}""";

    /// <summary>A store that keeps its rows in memory, and can age one.</summary>
    private sealed class MemoryStore : IMatchStore
    {
        private readonly Dictionary<Guid, string> _rows = [];

        public void Save(Guid matchId, string state) => _rows[matchId] = state;

        public void Remove(Guid matchId) => _rows.Remove(matchId);

        public IReadOnlyList<StoredMatch> LoadAll() =>
            [.. _rows.Select(row => new StoredMatch(row.Key, row.Value))];

        public void Age(Guid matchId, TimeSpan by)
        {
            var then = (DateTimeOffset.UtcNow - by).ToString("o", CultureInfo.InvariantCulture);
            _rows[matchId] = LastActivity().Replace(_rows[matchId], $"\"lastActivity\":\"{then}\"");
        }
    }
}
