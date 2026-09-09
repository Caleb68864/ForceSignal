using ForceSignal.Application;
using ForceSignal.Application.Ground;
using ForceSignal.Contracts.Ground;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Covers the shell around the game: it holds games, takes commands, and hands back snapshots.
/// </summary>
/// <remarks>
/// The rules are all tested in the StarGrunt module. What is checked here is that a whole turn can
/// be played through this surface, and that a refusal from the game arrives as an error rather than
/// being silently swallowed.
/// </remarks>
public sealed class StarGruntGameServiceTests
{
    // The worked firefight from the module tests: three dice through, two potential hits, one of
    // them a kill - and effective fire, so the target is suppressed. Scripted rather than rolled,
    // because a turn whose shape depends on the dice is a test that passes some days.
    private static readonly int[] AKillAndAStop = [6, 7, 5, 4, 5, 3, 5, 9, 4];

    /// <summary>The same firefight, then a recovery roll that beats leadership 2.</summary>
    private static readonly int[] FirefightThenRecovery = [6, 7, 5, 4, 5, 3, 5, 9, 4, 6];

    [Fact]
    public void AWholeTurnCanBePlayedThroughTheService()
    {
        var service = new StarGruntGameService(new ScriptedQualityDice(AKillAndAStop), null, new ScriptedFigureAllocator());
        var game = Table(service);

        var opened = service.BeginTurn(game);
        Assert.Equal("ChoosingFirstActivator", opened.Phase);
        Assert.Equal("blue", opened.FirstActivationChooser ?? "blue");

        var chosen = service.ChooseFirstActivator(game, new ChooseFirstActivatorRequest("blue", TakeIt: true));
        Assert.Equal("Activating", chosen.Phase);
        Assert.Equal("blue", chosen.ActiveSide);

        service.BeginActivation(game, new BeginStarGruntActivationRequest("blue", "alpha"));
        var fired = service.Fire(game, new StarGruntFireRequest(
            "alpha", "bravo", "Rifles", FirepowerDie: 10, SupportWeapons: [], DistanceInches: 9, Cover: "Soft"));

        Assert.Contains(fired.Log, entry => entry.Contains("Bravo Squad", StringComparison.Ordinal));

        service.EndActivation(game);

        // Bravo has been shot at and is suppressed, so it is not going anywhere: a pinned squad may
        // only watch, talk, reorganise under cover, or try to get its heads back up. That the fire
        // two steps above reaches this decision is the point of playing a turn through rather than
        // testing the commands one at a time.
        service.BeginActivation(game, new BeginStarGruntActivationRequest("red", "bravo"));
        var pinned = Assert.Throws<InvalidOperationException>(() =>
            service.TakeStep(game, new StarGruntStepRequest("Move")));
        Assert.Contains("suppressed", pinned.Message, StringComparison.OrdinalIgnoreCase);

        // Getting its head back up is a command rather than a step, because it rolls. Scripted to
        // fail here, so the squad stays pinned and the turn ends with it still down.
        service.RemoveSuppression(game, new StarGruntUnitActionRequest("bravo"));
        service.EndActivation(game);

        var ended = service.EndTurn(game);
        Assert.Equal("TurnEnded", ended.Phase);
    }

    [Fact]
    public void APinnedUnitCanGetItsHeadBackUpAndFightOn()
    {
        // Gap 1: before this existed, fire pinned a unit permanently and the game stopped after
        // first contact. A whole-turn test did not show it, because it takes a second activation.
        // Scripted so the volley suppresses and the recovery roll then beats leadership 2.
        var service = new StarGruntGameService(new ScriptedQualityDice(FirefightThenRecovery), null, new ScriptedFigureAllocator());
        var game = Table(service);
        service.BeginTurn(game);
        service.ChooseFirstActivator(game, new ChooseFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(game, new BeginStarGruntActivationRequest("blue", "alpha"));
        service.Fire(game, new StarGruntFireRequest(
            "alpha", "bravo", "Rifles", FirepowerDie: 10, SupportWeapons: [], DistanceInches: 9, Cover: "Soft"));
        service.EndActivation(game);

        service.BeginActivation(game, new BeginStarGruntActivationRequest("red", "bravo"));
        Assert.Equal(1, service.GetSnapshot(game).Units.Single(u => u.Id == "bravo").SuppressionMarkers);

        var recovered = service.RemoveSuppression(game, new StarGruntUnitActionRequest("bravo"));

        Assert.Equal(0, recovered.Units.Single(u => u.Id == "bravo").SuppressionMarkers);
        // And it is genuinely back in the fight rather than merely showing a zero.
        Assert.Null(Record.Exception(() => service.TakeStep(game, new StarGruntStepRequest("Move"))));
    }

    [Fact]
    public void RemoveSuppressionCannotBeSentThroughTheGenericStepRoute()
    {
        // It rolls a die, and that route has no die source - the same reason firing has its own.
        var service = new StarGruntGameService();
        var game = Table(service);
        service.BeginTurn(game);
        service.ChooseFirstActivator(game, new ChooseFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(game, new BeginStarGruntActivationRequest("blue", "alpha"));

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.TakeStep(game, new StarGruntStepRequest("RemoveSuppression")));

        Assert.Contains("command of its own", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASnapshotCarriesTheLegalityOfEveryUnit()
    {
        var service = new StarGruntGameService();
        var game = Table(service);
        service.BeginTurn(game);
        var snapshot = service.ChooseFirstActivator(game, new ChooseFirstActivatorRequest("blue", TakeIt: true));

        var alpha = snapshot.Units.Single(unit => unit.Id == "alpha");
        var bravo = snapshot.Units.Single(unit => unit.Id == "bravo");

        Assert.True(alpha.CanActivate);
        Assert.False(bravo.CanActivate);
        Assert.False(string.IsNullOrWhiteSpace(bravo.ActivationBlocker));
        Assert.NotEmpty(alpha.WeaponLegality);
        Assert.All(alpha.WeaponLegality, weapon => Assert.False(weapon.CanFire));
    }

    [Fact]
    public void ASnapshotCarriesTheArmourDiceThePlayerEntered()
    {
        // The roster is the player's own data and the snapshot is the only way back to it. Leaving
        // it off meant a client writing the force out to a file had nowhere to read an armour die
        // from, so it wrote a number of its own - a D12 squad came back on D6 in the player's file.
        var service = new StarGruntGameService();
        var created = service.CreateGame(new CreateStarGruntGameRequest("Hill 43"));
        service.AddUnit(created.GameId, Squad("alpha", "Alpha Squad", "blue") with
        {
            Figures = [new StarGruntFigureDto(12), new StarGruntFigureDto(4), new StarGruntFigureDto(12)],
        });

        var alpha = service.GetSnapshot(created.GameId).Units.Single(unit => unit.Id == "alpha");

        // Mixed on purpose: a squad may mix armour, which is why figures are listed rather than
        // counted, so one die per unit would not carry the card either.
        Assert.Equal([12, 4, 12], alpha.Figures.Select(figure => figure.ArmourDie));
        Assert.Equal(alpha.FullStrength, alpha.Figures.Count);
    }

    [Fact]
    public void ARefusedCommandComesBackAsAnError()
    {
        var service = new StarGruntGameService();
        var game = Table(service);
        service.BeginTurn(game);
        service.ChooseFirstActivator(game, new ChooseFirstActivatorRequest("blue", TakeIt: true));

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.BeginActivation(game, new BeginStarGruntActivationRequest("red", "bravo")));

        Assert.False(string.IsNullOrWhiteSpace(refused.Message));
    }

    [Fact]
    public void ARefusedCommandLeavesTheGameWhereItWas()
    {
        var service = new StarGruntGameService();
        var game = Table(service);
        service.BeginTurn(game);
        var before = service.ChooseFirstActivator(game, new ChooseFirstActivatorRequest("blue", TakeIt: true));

        Assert.Throws<InvalidOperationException>(() =>
            service.BeginActivation(game, new BeginStarGruntActivationRequest("red", "bravo")));

        var after = service.GetSnapshot(game);
        Assert.Equal(before.Version, after.Version);
    }

    [Fact]
    public void EveryChangeBumpsTheVersion()
    {
        var service = new StarGruntGameService();
        var game = Table(service);

        var first = service.GetSnapshot(game).Version;
        var second = service.BeginTurn(game).Version;

        Assert.True(second > first);
    }

    [Fact]
    public void AGameNobodyCreatedIsNotFound()
    {
        var service = new StarGruntGameService();

        Assert.Throws<NotFoundException>(() => service.GetSnapshot(Guid.NewGuid()));
    }

    [Fact]
    public void ADieThatIsNotOnTheLadderIsRefused()
    {
        var service = new StarGruntGameService();
        var created = service.CreateGame(new CreateStarGruntGameRequest("Hill 43"));

        Assert.Throws<InvalidOperationException>(() => service.AddUnit(created.GameId, new AddStarGruntUnitRequest(
            "alpha", "Alpha Squad", "blue", "Squad", QualityDie: 7, LeadershipValue: 2, Figures: [], Weapons: [])));
    }

    [Fact]
    public void TwoUnitsCannotShareAnId()
    {
        var service = new StarGruntGameService();
        var game = Table(service);

        Assert.Throws<InvalidOperationException>(() => service.AddUnit(game, Squad("alpha", "Duplicate", "blue")));
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
        Weapons:
        [
            new StarGruntWeaponDto("Rifles", 10),
            new StarGruntWeaponDto("Squad Support", 10, IsSupport: true),
        ]);
}
