using ForceSignal.Application.Ground;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.TestSupport;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Covers the shell around the Dirtside game: it holds games, takes commands, and hands back
/// snapshots.
/// </summary>
/// <remarks>
/// The rules are all tested in the Dirtside module. What is checked here is that a whole turn can
/// be played through this surface, that a refusal from the game arrives as an error rather than
/// being silently swallowed, and that the snapshot says what a screen needs to say.
/// </remarks>
public sealed class DirtsideGameServiceTests
{
    [Fact]
    public void AWholeTurnCanBePlayedThroughTheService()
    {
        // The target rolls first and the barrel second, so a 1 against an 8 is a solid hit; three
        // chits of eight against three points of armour is far past what the vehicle can take.
        // Scripted rather than rolled, because a turn whose shape depends on the dice is a test
        // that passes some days.
        var service = new DirtsideGameService(
            new ScriptedQualityDice(1, 8),
            null,
            ScriptedChitPot.Handing(DamageChit.Numerical(ChitColour.Red, 8)));
        var game = Table(service);

        var opened = service.BeginTurn(game);
        Assert.Equal("ChoosingFirstActivator", opened.Phase);

        var chosen = service.ChooseFirstActivator(game, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));
        Assert.Equal("Activating", chosen.Phase);
        Assert.Equal("blue", chosen.ActiveSide);

        var activated = service.BeginActivation(game, new BeginDirtsideActivationRequest("blue", "alpha"));
        Assert.Equal("alpha", activated.ActivatingUnitId);

        var fired = service.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close"));
        var bravoOne = fired.Units.Single(unit => unit.Id == "bravo").Elements.Single(element => element.Id == "bravo-1");
        Assert.True(bravoOne.IsDestroyed);
        Assert.Contains(fired.Log, entry => entry.Contains("knocked out", StringComparison.Ordinal));

        // The activation cannot close until every element has said what it is doing, and the
        // snapshot names who is still to choose - that is what drives the screen's buttons.
        Assert.False(fired.CanEndActivation);
        Assert.Contains("alpha-2", fired.ElementsStillToChoose);

        var stood = service.StandDown(game, new DirtsideStandDownRequest("alpha-2"));
        Assert.True(stood.CanEndActivation);
        service.EndActivation(game);

        service.BeginActivation(game, new BeginDirtsideActivationRequest("red", "bravo"));
        service.StandDown(game, new DirtsideStandDownRequest("bravo-2"));
        service.EndActivation(game);

        var ended = service.EndTurn(game);
        Assert.Equal("TurnEnded", ended.Phase);
    }

    [Fact]
    public void AnElementThatMovesAndSwitchesItsSensorsOnHasSpentBothItsGoes()
    {
        var service = new DirtsideGameService();
        var game = Activated(service);

        var moved = service.MoveElement(game, new MoveDirtsideElementRequest("alpha-1", OverHalfItsMovement: true));
        var alphaOne = Element(moved, "alpha", "alpha-1");
        Assert.True(alphaOne.HasMoved);
        Assert.True(alphaOne.MovedOverHalf);
        Assert.False(alphaOne.HasTakenCombatAction);

        var sensing = service.SetAreaDefenceSensors(game, new DirtsideSensorsRequest("alpha-1", Live: true));
        alphaOne = Element(sensing, "alpha", "alpha-1");
        Assert.True(alphaOne.AreaDefenceSensorsLive);
        Assert.True(alphaOne.HasTakenCombatAction);
    }

    [Fact]
    public void ADamagedElementReportsTheMovementItActuallyHasLeft()
    {
        // DamagedEffects.Movement was written, tested and applied to nothing: the card's number went
        // into the roster and came back out of the snapshot unchanged, so a screen beside a damaged
        // vehicle showed the movement it had when it was whole. The halving is the engine's; the 12
        // being halved is the fixture's, which is to say the player's.
        // Scripted end to end so the vehicle really is damaged rather than damaged on a good day.
        // The target rolls first and the barrel second, so 1 against 8 is a solid hit; the fixture's
        // armour is 3 and its main gun draws three chits, so three red ones worth 1 total exactly 3,
        // which is the value that marks DMG rather than knocking the vehicle out.
        var service = new DirtsideGameService(
            new ScriptedQualityDice(1, 8),
            null,
            ScriptedChitPot.Handing(DamageChit.Numerical(ChitColour.Red, 1)));
        var game = Activated(service);

        Assert.Equal(12, Element(service.GetSnapshot(game), "bravo", "bravo-1").Movement);

        var damaged = service.Fire(game, new DirtsideFireRequest(
            "alpha-1", "Main Gun", "bravo", "bravo-1", "Close", WillMoveOverHalf: false));
        var target = Element(damaged, "bravo", "bravo-1");

        Assert.True(target.IsDamaged);
        Assert.Equal(6, target.Movement);
    }

    [Fact]
    public void ARefusedCommandComesBackAsAnError()
    {
        var service = new DirtsideGameService();
        var game = Table(service);
        service.BeginTurn(game);
        service.ChooseFirstActivator(game, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.BeginActivation(game, new BeginDirtsideActivationRequest("red", "bravo")));

        Assert.False(string.IsNullOrWhiteSpace(refused.Message));
    }

    [Fact]
    public void ARefusedCommandLeavesTheGameWhereItWas()
    {
        var service = new DirtsideGameService();
        var game = Table(service);
        service.BeginTurn(game);
        var before = service.ChooseFirstActivator(game, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));

        Assert.Throws<InvalidOperationException>(() =>
            service.BeginActivation(game, new BeginDirtsideActivationRequest("red", "bravo")));

        Assert.Equal(before.Version, service.GetSnapshot(game).Version);
    }

    [Fact]
    public void EndingAnActivationBeforeEveryElementHasChosenIsRefusedByName()
    {
        var service = new DirtsideGameService();
        var game = Activated(service);

        var refused = Assert.Throws<InvalidOperationException>(() => service.EndActivation(game));

        // The refusal says who is holding things up, by name, and so does the snapshot, in the
        // same words.
        Assert.Contains("Alpha Troop One", refused.Message, StringComparison.Ordinal);
        Assert.Equal(refused.Message, service.GetSnapshot(game).WhyActivationCannotEnd);
    }

    [Fact]
    public void FiringWithNothingActivatedIsRefused()
    {
        var service = new DirtsideGameService();
        var game = Table(service);

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close")));

        Assert.Contains("Nothing is activated", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABandThatIsNotOnTheTapeIsRefused()
    {
        var service = new DirtsideGameService();
        var game = Activated(service);

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "PointBlank")));

        Assert.Contains("range band", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryChangeBumpsTheVersion()
    {
        var service = new DirtsideGameService();
        var game = Table(service);

        var first = service.GetSnapshot(game).Version;
        var second = service.BeginTurn(game).Version;

        Assert.True(second > first);
    }

    [Fact]
    public void AGameNobodyCreatedIsNotFound()
    {
        var service = new DirtsideGameService();

        Assert.Throws<NotFoundException>(() => service.GetSnapshot(Guid.NewGuid()));
    }

    [Fact]
    public void TwoPlatoonsCannotShareAnId()
    {
        var service = new DirtsideGameService();
        var game = Table(service);

        Assert.Throws<InvalidOperationException>(() => service.AddPlatoon(game, Platoon("alpha", "Duplicate", "blue")));
    }

    [Fact]
    public void AKindThatIsNotOnTheConfidenceTableIsRefused()
    {
        var service = new DirtsideGameService();
        var created = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge"));

        Assert.Throws<InvalidOperationException>(() => service.AddPlatoon(created.GameId,
            Platoon("alpha", "Alpha Troop", "blue") with { Kind = "Cavalry" }));
    }

    [Fact]
    public void ASnapshotCarriesWhetherEachPlatoonCouldActivateAndWhyNot()
    {
        var service = new DirtsideGameService();
        var game = Table(service);
        service.BeginTurn(game);
        var snapshot = service.ChooseFirstActivator(game, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));

        var alpha = snapshot.Units.Single(unit => unit.Id == "alpha");
        var bravo = snapshot.Units.Single(unit => unit.Id == "bravo");

        Assert.True(alpha.CanActivate);
        Assert.False(bravo.CanActivate);
        Assert.False(string.IsNullOrWhiteSpace(bravo.WhyItCannotActivate));
    }

    /// <summary>Two platoons on the table and Alpha's activation open.</summary>
    internal static Guid Activated(DirtsideGameService service)
    {
        var game = Table(service);
        service.BeginTurn(game);
        service.ChooseFirstActivator(game, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(game, new BeginDirtsideActivationRequest("blue", "alpha"));
        return game;
    }

    internal static Guid Table(DirtsideGameService service)
    {
        var created = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9"));
        service.AddPlatoon(created.GameId, Platoon("alpha", "Alpha Troop", "blue"));
        service.AddPlatoon(created.GameId, Platoon("bravo", "Bravo Troop", "red"));
        return created.GameId;
    }

    internal static DirtsideElementStateDto Element(DirtsideSnapshotDto snapshot, string unit, string element) =>
        snapshot.Units.Single(u => u.Id == unit).Elements.Single(e => e.Id == element);

    /// <summary>
    /// A platoon of two vehicles with a gun that can hurt anything at any range. Every number is
    /// invented, for the same reason the Full Thrust fixtures are.
    /// </summary>
    internal static AddDirtsidePlatoonRequest Platoon(string id, string name, string side) => new(
        id,
        name,
        side,
        "Armour",
        IsCybertank: false,
        Elements:
        [
            Vehicle($"{id}-1", $"{name} One"),
            Vehicle($"{id}-2", $"{name} Two"),
        ]);

    internal static DirtsideElementDto Vehicle(string id, string name) => new(
        id,
        name,
        "Basic",
        Signature: 3,
        ArmourValue: 3,
        Movement: 12,
        Weapons: [MainGun]);

    internal static DirtsideWeaponDto MainGun { get; } = new(
        "Main Gun",
        ChitCount: 3,
        Close: new DirtsideValidityDto("All", "FaceValue"),
        Medium: new DirtsideValidityDto("All", "FaceValue"),
        Long: new DirtsideValidityDto("All", "FaceValue"));
}
