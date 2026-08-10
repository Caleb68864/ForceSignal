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

    [Fact]
    public void AWholeTurnCanBePlayedThroughTheService()
    {
        var service = new StarGruntGameService(new ScriptedQualityDice(AKillAndAStop));
        var game = Table(service);

        var opened = service.BeginTurn(game);
        Assert.Equal("ChoosingFirstActivator", opened.Phase);
        Assert.Equal("blue", opened.FirstActivationChooser ?? "blue");

        var chosen = service.ChooseFirstActivator(game, new ChooseFirstActivatorRequest("blue", TakeIt: true));
        Assert.Equal("Activating", chosen.Phase);
        Assert.Equal("blue", chosen.ActiveSide);

        service.BeginActivation(game, new BeginStarGruntActivationRequest("blue", "alpha"));
        var fired = service.Fire(game, new StarGruntFireRequest(
            "alpha", "bravo", "Rifles", FirepowerDie: 10, SupportDice: [8], DistanceInches: 9, Cover: "Soft"));

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

        service.TakeStep(game, new StarGruntStepRequest("RemoveSuppression"));
        service.EndActivation(game);

        var ended = service.EndTurn(game);
        Assert.Equal("TurnEnded", ended.Phase);
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
        Assert.All(alpha.WeaponLegality, weapon => Assert.False(weapon.CanFire));
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

        Assert.Throws<InvalidOperationException>(() => service.GetSnapshot(Guid.NewGuid()));
    }

    [Fact]
    public void ADieThatIsNotOnTheLadderIsRefused()
    {
        var service = new StarGruntGameService();
        var created = service.CreateGame(new CreateStarGruntGameRequest("Hill 43"));

        Assert.Throws<InvalidOperationException>(() => service.AddUnit(created.GameId, new AddStarGruntUnitRequest(
            "alpha", "Alpha Squad", "blue", "Squad", QualityDie: 7, LeadershipDie: 8, Figures: [], Weapons: [])));
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
        LeadershipDie: 8,
        Figures: [.. Enumerable.Repeat(new StarGruntFigureDto(6), 8)],
        Weapons:
        [
            new StarGruntWeaponDto("Rifles", 10),
            new StarGruntWeaponDto("Squad Support", 10, IsSupport: true),
        ]);
}
