using ForceSignal.Application.Ground;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.TestSupport;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Close assault and systems-down recovery through the Dirtside service: the wire shapes reach the
/// game, refusals arrive as errors, the snapshot says where the assault stands, and an assault put
/// down in the middle comes back from the store at the same point.
/// </summary>
public sealed class DirtsideAssaultServiceTests
{
    private static readonly DirtsideValidityDto Everything = new("All", "FaceValue");

    [Fact]
    public void AWholeAssaultCanBeFoughtThroughTheService()
    {
        // Every die is a 6 and every chit an 8: the launch goes in, the defender stands, and one
        // round removes every stand on both sides, which the aftermath settles without a roll.
        var service = new DirtsideGameService(
            new ScriptedQualityDice { Fallback = 6 },
            null,
            ScriptedChitPot.Handing(DamageChit.Numerical(ChitColour.Red, 8)));
        var game = Activated(service);

        var launched = service.LaunchAssault(game, new LaunchDirtsideAssaultRequest("bravo", ["alpha-1", "alpha-2"], 1, Everything));
        Assert.NotNull(launched.Assault);
        Assert.Equal("AwaitingDefender", launched.Assault.Stage);
        Assert.Equal("alpha", launched.Assault.AttackerUnitId);
        Assert.Equal("bravo", launched.Assault.DefenderUnitId);
        Assert.Equal(["alpha-1", "alpha-2"], launched.Assault.AttackerElementIds);
        Assert.Empty(launched.Assault.DefenderElementIds);
        Assert.True(launched.Units.Single(unit => unit.Id == "bravo").HasActivated);

        var stood = service.DefenderStands(game, new DirtsideAssaultStandRequest(["bravo-1", "bravo-2"], 2, Everything));
        Assert.Equal("AwaitingRound", stood.Assault!.Stage);
        Assert.Equal(["bravo-1", "bravo-2"], stood.Assault.DefenderElementIds);

        var fought = service.FightAssaultRound(game);
        Assert.Equal("AwaitingAftermath", fought.Assault!.Stage);
        Assert.Empty(fought.Assault.AttackerElementIds);
        Assert.Empty(fought.Assault.DefenderElementIds);
        Assert.All(fought.Units.SelectMany(unit => unit.Elements), element => Assert.True(element.IsDestroyed));

        var settled = service.ResolveAssaultAftermath(game, new DirtsideAssaultAftermathRequest(1, 3));
        Assert.Equal("AwaitingFollowThrough", settled.Assault!.Stage);

        var through = service.FollowThrough(game, new DirtsideFollowThroughRequest(1));
        Assert.Null(through.Assault);
        Assert.Contains(through.Log, entry => entry.Contains("drives on through", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAssaultPutDownInTheMiddleComesBackFromTheStoreAtTheSamePoint()
    {
        var store = new RecordingStore();
        var first = new DirtsideGameService(new ScriptedQualityDice { Fallback = 6 }, store);
        var game = Activated(first);
        first.LaunchAssault(game, new LaunchDirtsideAssaultRequest("bravo", ["alpha-1"], 0, Everything));
        var before = first.DefenderStands(game, new DirtsideAssaultStandRequest(["bravo-1"], 0, Everything));

        var restarted = new DirtsideGameService(new ScriptedQualityDice { Fallback = 6 }, store, ScriptedChitPot.Handing(DamageChit.Numerical(ChitColour.Red, 1)));

        var after = restarted.GetSnapshot(game);
        Assert.Equal("AwaitingRound", after.Assault!.Stage);
        Assert.Equal(before.Assault!.Round, after.Assault.Round);
        Assert.Equal(before.Assault.AttackerElementIds, after.Assault.AttackerElementIds);
        Assert.Equal(before.Assault.DefenderElementIds, after.Assault.DefenderElementIds);

        // And the fight goes on from there.
        var fought = restarted.FightAssaultRound(game);
        Assert.Equal("AwaitingAftermath", fought.Assault!.Stage);
    }

    [Fact]
    public void AStepThatIsNotTheOneOwedIsRefusedAsAnError()
    {
        var service = new DirtsideGameService(new ScriptedQualityDice { Fallback = 6 });
        var game = Activated(service);

        var refused = Assert.Throws<InvalidOperationException>(() => service.FightAssaultRound(game));

        Assert.Contains("No assault", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssaultWithoutAValidityRowIsRefusedRatherThanFoughtIneffectively()
    {
        var service = new DirtsideGameService(new ScriptedQualityDice { Fallback = 6 });
        var game = Activated(service);

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.LaunchAssault(game, new LaunchDirtsideAssaultRequest("bravo", ["alpha-1"], 0, null!)));

        Assert.Contains("what its chits may count", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADieThatIsNotOnTheLadderIsRefused()
    {
        var service = new DirtsideGameService();
        var created = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge"));

        Assert.Throws<InvalidOperationException>(() => service.AddPlatoon(
            created.GameId, Platoon("alpha", "Alpha Troop", "blue") with { QualityDie = "D20" }));
    }

    [Fact]
    public void TheSnapshotCarriesTheCommandMarkerAndTheCardNumbers()
    {
        var service = new DirtsideGameService();
        var game = Activated(service);

        var alpha = service.GetSnapshot(game).Units.Single(unit => unit.Id == "alpha");

        Assert.Equal("D8", alpha.QualityDie);
        Assert.Equal(2, alpha.LeadershipValue);
        Assert.Equal(3, alpha.Elements[0].AssaultChits);
        Assert.Equal(4, alpha.Elements[0].KillThreshold);
        Assert.True(alpha.Elements[0].HasBackupSystems);
    }

    [Fact]
    public void SystemsDownRecoveryIsRefusedOnTheActivationAndTakenOnTheNext()
    {
        // Alpha One's gun fails on its first activation - the chit says so - and its own systems go
        // down. The attempt the same activation is refused, and the snapshot says why in the same
        // words. Next turn the crew roll a 4, which is what the invented profile asks of a vehicle
        // with backup systems.
        var service = new DirtsideGameService(
            new ScriptedQualityDice(1, 8) { Fallback = 4 },
            null,
            ScriptedChitPot.Handing(DamageChit.Of(ChitSpecial.SystemsDownFirer)));
        var game = Activated(service);

        var down = service.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close"));
        var alphaOne = DirtsideGameServiceTests.Element(down, "alpha", "alpha-1");
        Assert.True(alphaOne.IsSystemsDown);
        Assert.False(alphaOne.CanRecoverSystems);

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.RecoverSystems(game, new DirtsideRecoverSystemsRequest("alpha-1")));
        Assert.Equal(alphaOne.WhyItCannotRecoverSystems, refused.Message);

        service.StandDown(game, new DirtsideStandDownRequest("alpha-2"));
        service.EndActivation(game);
        service.BeginActivation(game, new BeginDirtsideActivationRequest("red", "bravo"));
        service.StandDown(game, new DirtsideStandDownRequest("bravo-1"));
        service.StandDown(game, new DirtsideStandDownRequest("bravo-2"));
        service.EndActivation(game);
        service.EndTurn(game);
        service.BeginTurn(game);
        service.ChooseFirstActivator(game, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));
        var next = service.BeginActivation(game, new BeginDirtsideActivationRequest("blue", "alpha"));
        Assert.True(DirtsideGameServiceTests.Element(next, "alpha", "alpha-1").CanRecoverSystems);

        var recovered = service.RecoverSystems(game, new DirtsideRecoverSystemsRequest("alpha-1"));

        alphaOne = DirtsideGameServiceTests.Element(recovered, "alpha", "alpha-1");
        Assert.False(alphaOne.IsSystemsDown);
        Assert.True(alphaOne.HasTakenCombatAction);
    }

    [Fact]
    public void APlatoonAddedOnceTheGameHasBegun_IsRefused_AndTheTurnIsLeftAlone()
    {
        // Adding a platoon rebuilt the session from the roster, so mid-turn it put the game back to
        // turn 0, not started, with the open activation and everyone's "has activated" gone - and
        // saved that. The add-platoon form is always on screen.
        var service = new DirtsideGameService(new ScriptedQualityDice { Fallback = 6 });
        var game = Activated(service);

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            service.AddPlatoon(game, Platoon("charlie", "Charlie Troop", "red")));

        Assert.Contains("has begun", refusal.Message, StringComparison.Ordinal);
        var after = service.GetSnapshot(game);
        Assert.Equal(1, after.TurnNumber);
        Assert.Equal("alpha", after.ActivatingUnitId);
        Assert.DoesNotContain(after.Units, unit => unit.Id == "charlie");
    }

    [Fact]
    public void AnAssaultOnAPlatoonWhoseCardCannotStand_IsRefusedAtLaunch_SoTheGameIsNotStuck()
    {
        // Bravo's card carries no die, leadership or assault numbers - which the form says is
        // allowed, it just cannot receive a close assault. The launch used to be accepted anyway, and
        // then the stand was refused every time, the activation could not end with the assault
        // open, and nothing cleared it.
        var service = new DirtsideGameService(new ScriptedQualityDice { Fallback = 6 });
        var created = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9"));
        service.AddPlatoon(created.GameId, Platoon("alpha", "Alpha Troop", "blue"));
        service.AddPlatoon(created.GameId, DirtsideGameServiceTests.Platoon("bravo", "Bravo Troop", "red"));
        service.BeginTurn(created.GameId);
        service.ChooseFirstActivator(created.GameId, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(created.GameId, new BeginDirtsideActivationRequest("blue", "alpha"));

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            service.LaunchAssault(created.GameId, new LaunchDirtsideAssaultRequest("bravo", ["alpha-1"], 0, Everything)));

        Assert.Contains("Bravo Troop", refusal.Message, StringComparison.Ordinal);
        var after = service.GetSnapshot(created.GameId);
        Assert.Null(after.Assault);
        // And nothing about an assault stands between this activation and its end, which is what
        // the stuck game could never get past.
        Assert.DoesNotContain("assault", after.WhyActivationCannotEnd ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAssaultOnAPlatoonWithNoStandLeftThatCouldCommit_IsRefusedAtLaunch()
    {
        // A tested card, but none of its elements says how many chits it draws: the same trap.
        var service = new DirtsideGameService(new ScriptedQualityDice { Fallback = 6 });
        var created = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9"));
        service.AddPlatoon(created.GameId, Platoon("alpha", "Alpha Troop", "blue"));
        service.AddPlatoon(created.GameId, DirtsideGameServiceTests.Platoon("bravo", "Bravo Troop", "red") with { QualityDie = "D8", LeadershipValue = 2 });
        service.BeginTurn(created.GameId);
        service.ChooseFirstActivator(created.GameId, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(created.GameId, new BeginDirtsideActivationRequest("blue", "alpha"));

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            service.LaunchAssault(created.GameId, new LaunchDirtsideAssaultRequest("bravo", ["alpha-1"], 0, Everything)));

        Assert.Contains("cannot stand", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100_000)]
    public void AssaultChitsThePotCannotHandOut_AreRefusedWhenThePlatoonIsAdded(int chits)
    {
        // Unchecked, they reached the pot at the round - after both sides had committed - which
        // refused the draw on every try and left the assault, and so the game, stuck.
        var service = new DirtsideGameService(new ScriptedQualityDice { Fallback = 6 });
        var created = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9"));
        var platoon = Platoon("alpha", "Alpha Troop", "blue");
        var bad = platoon with { Elements = [platoon.Elements[0] with { AssaultChits = chits }, platoon.Elements[1]] };

        var refusal = Assert.Throws<InvalidOperationException>(() => service.AddPlatoon(created.GameId, bad));

        Assert.Contains("assault chits", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(service.GetSnapshot(created.GameId).Units);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100_000)]
    public void AWeaponChitCountThePotCannotHandOut_IsRefusedWhenThePlatoonIsAdded(int chits)
    {
        var service = new DirtsideGameService(new ScriptedQualityDice { Fallback = 6 });
        var created = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9"));
        var platoon = Platoon("alpha", "Alpha Troop", "blue");
        var element = platoon.Elements[0];
        var bad = platoon with { Elements = [element with { Weapons = [element.Weapons[0] with { ChitCount = chits }] }] };

        var refusal = Assert.Throws<InvalidOperationException>(() => service.AddPlatoon(created.GameId, bad));

        Assert.Contains("chit count", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Two platoons that can assault and be assaulted, with Alpha's activation open.</summary>
    private static Guid Activated(DirtsideGameService service)
    {
        var created = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9"));
        service.AddPlatoon(created.GameId, Platoon("alpha", "Alpha Troop", "blue"));
        service.AddPlatoon(created.GameId, Platoon("bravo", "Bravo Troop", "red"));
        service.BeginTurn(created.GameId);
        service.ChooseFirstActivator(created.GameId, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(created.GameId, new BeginDirtsideActivationRequest("blue", "alpha"));
        return created.GameId;
    }

    /// <summary>
    /// A platoon whose card carries everything an assault reads: a die and a leadership on the
    /// command marker, chits and a kill threshold on every stand. Every number is invented.
    /// </summary>
    private static AddDirtsidePlatoonRequest Platoon(string id, string name, string side) =>
        DirtsideGameServiceTests.Platoon(id, name, side) with
        {
            QualityDie = "D8",
            LeadershipValue = 2,
            Elements =
            [
                DirtsideGameServiceTests.Vehicle($"{id}-1", $"{name} One") with { AssaultChits = 3, KillThreshold = 4, HasBackupSystems = true },
                DirtsideGameServiceTests.Vehicle($"{id}-2", $"{name} Two") with { AssaultChits = 3, KillThreshold = 4 },
            ],
        };

    /// <summary>A store that keeps rows in memory, so a second service can be built over the first's saves.</summary>
    private sealed class RecordingStore : IMatchStore
    {
        private readonly Dictionary<Guid, string> _rows = [];

        public void Save(Guid matchId, string state) => _rows[matchId] = state;

        public void Remove(Guid matchId) => _rows.Remove(matchId);

        public IReadOnlyList<StoredMatch> LoadAll() =>
            [.. _rows.Select(row => new StoredMatch(row.Key, row.Value))];
    }
}
