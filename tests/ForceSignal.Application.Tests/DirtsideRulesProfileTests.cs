using ForceSignal.Application.Ground;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Ground;
using ForceSignal.TestSupport;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Covers the die tables being the players' and belonging to one game.
/// </summary>
/// <remarks>
/// <para>
/// The three tables in <c>HitResolution</c> were the largest thing this app was still shipping that
/// it had no right to: a fire-control level was worth a D6, a D8 or a D10, a posture a D6 through a
/// D12, and a signature indexed the quality ladder. The whole combat model rested on them.
/// </para>
/// <para>
/// The behaviour worth pinning is not that a supplied table is read - that is ordinary - but what
/// happens when one is not. There is no fallback, and there deliberately is not: the shot is refused
/// and the refusal names the row. These tests are mostly about the refusal being survivable, which
/// is where this project has twice gone wrong in the other direction.
/// </para>
/// </remarks>
public sealed class DirtsideRulesProfileTests
{
    [Fact]
    public void AGameWithNoProfileRefusesItsFirstShotAndSaysWhichRowItWants()
    {
        var service = new DirtsideGameService(new ScriptedQualityDice(1, 8));
        var game = Activated(service, profile: null);

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close")));

        // The row, not "your profile is incomplete". A table reading this knows which line of their
        // own rulebook to go and type in.
        Assert.Contains("Basic fire control", refused.Message, StringComparison.Ordinal);
        Assert.Contains("rules profile", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRefusedShotIsNotChargedToTheElement()
    {
        // The control that must be accepted, and the reason the check sits before the step is taken.
        // Every other refusal in Fire costs the element its combat action, because every other
        // refusal is something the player could have known. This one is a gap in what they typed,
        // and taking their shot for it would be charging a table for this app's own policy.
        var service = new DirtsideGameService(new ScriptedQualityDice(1, 8));
        var game = Activated(service, profile: null);

        Assert.Throws<InvalidOperationException>(() =>
            service.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close")));

        var alphaOne = DirtsideGameServiceTests.Element(service.GetSnapshot(game), "alpha", "alpha-1");
        Assert.False(alphaOne.HasTakenCombatAction);
        Assert.False(alphaOne.HasChosen);
    }

    [Fact]
    public void TheSameShotGoesThroughOnceTheRowsAreThere()
    {
        // The other half of the pair: the refusal really is about the missing rows and not about
        // anything else in the declaration, so the identical shot resolves against a profile.
        var service = new DirtsideGameService(new ScriptedQualityDice(1, 8));
        var game = Activated(service, DirtsideTestProfile.Invented);

        var fired = service.Fire(
            game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close"));

        Assert.True(DirtsideGameServiceTests.Element(fired, "alpha", "alpha-1").HasTakenCombatAction);
    }

    [Fact]
    public void APartlyFilledProfileSettlesTheShotsItHasRowsFor()
    {
        // The over-strict trap, which this project has paid for twice: a table whose vehicles are
        // all basic-gunnery and signature 3 never reads the other rows, and refusing their game
        // until they had entered a superior sight would be inventing a requirement in place of a
        // number. Two rows, and the shot goes.
        var service = new DirtsideGameService(new ScriptedQualityDice(1, 8));
        var game = Activated(service, new DirtsideRulesProfileDto(
            FireControl: [new DirtsideDieRowDto("Basic", "D6")],
            Signature: [new DirtsideDieRowDto("3", "D8")]));

        var fired = service.Fire(
            game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close"));

        Assert.True(DirtsideGameServiceTests.Element(fired, "alpha", "alpha-1").HasTakenCombatAction);
    }

    [Fact]
    public void ASnapshotReadsBackTheRowsThePlayersEntered()
    {
        var service = new DirtsideGameService();
        var created = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9"));

        var profile = created.Snapshot.Profile;

        Assert.NotNull(profile);
        Assert.NotNull(profile.FireControl);
        Assert.Equal(
            [("Basic", "D4"), ("Enhanced", "D8"), ("Superior", "D12")],
            profile.FireControl.Select(row => (row.Key, row.Die)));
        Assert.Equal("D8", profile.SystemsDownRecoveryDie);
        Assert.Equal(7, profile.SystemsDownRecoveryRoll);
    }

    [Fact]
    public void AGameWithNoProfileSaysSoOnEverySnapshotRatherThanOnlyWhenItRefuses()
    {
        // So a table finds out before a model is in somebody's hand. Empty tables rather than a null
        // profile: "nobody entered any" is a real answer and one a screen can render.
        var created = new DirtsideGameService().CreateGame(new CreateDirtsideGameRequest("Ridge 9"));

        Assert.NotNull(created.Snapshot.Profile);
        Assert.Empty(created.Snapshot.Profile.FireControl!);
        Assert.Empty(created.Snapshot.Profile.Signature!);
        Assert.Null(created.Snapshot.Profile.SystemsDownRecoveryDie);
    }

    [Fact]
    public void AProfileSurvivesARestart()
    {
        var store = new MemoryStore();
        var first = new DirtsideGameService(new ScriptedQualityDice(1, 8), store);
        var game = first.CreateGame(DirtsideTestProfile.CreateGame("Ridge 9")).GameId;

        // A second service over the same store is what a restart looks like.
        var restarted = new DirtsideGameService(new ScriptedQualityDice(1, 8), store);

        Assert.Equal(
            [("Basic", "D4"), ("Enhanced", "D8"), ("Superior", "D12")],
            restarted.GetSnapshot(game).Profile!.FireControl!.Select(row => (row.Key, row.Die)));
    }

    [Fact]
    public void AGameStoredBeforeProfilesExistedStillOpens()
    {
        // The one that would have bitten. GroundGameRecord skips a row it cannot read rather than
        // migrating it, so a *required* settings field would have retired every stored Dirtside game
        // on the machine in silence. The field is optional inside the already-optional settings
        // blob, so a row from before it existed loads - as a game whose first shot asks for a row.
        var store = new MemoryStore();
        var before = new DirtsideGameService(null, store);
        var game = before.CreateGame(new CreateDirtsideGameRequest("Ridge 9")).GameId;

        var restarted = new DirtsideGameService(null, store);

        Assert.Empty(restarted.SkippedSaves);
        Assert.NotNull(restarted.GetSnapshot(game));
        Assert.Empty(restarted.GetSnapshot(game).Profile!.FireControl!);
    }

    [Theory]
    [InlineData("None", "D6")]
    [InlineData("Prone", "D6")]
    public void APostureRowTheEngineHasNoDieShiftForIsRefusedByName(string key, string die)
    {
        // None is a target doing nothing, which throws no second die at all, so a row for it would
        // be a die nothing ever rolls. Refused rather than quietly dropped, because a player who
        // typed it wants to know it did nothing.
        var refused = Assert.Throws<InvalidOperationException>(() =>
            new DirtsideGameService().CreateGame(new CreateDirtsideGameRequest(
                "Ridge 9",
                null,
                new DirtsideRulesProfileDto(Posture: [new DirtsideDieRowDto(key, die)]))));

        Assert.Contains(key, refused.Message, StringComparison.Ordinal);
        Assert.Contains("SoftCover", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("three")]
    public void ASignatureOffTheCardIsRefusedRatherThanClamped(string key)
    {
        var refused = Assert.Throws<InvalidOperationException>(() =>
            new DirtsideGameService().CreateGame(new CreateDirtsideGameRequest(
                "Ridge 9",
                null,
                new DirtsideRulesProfileDto(Signature: [new DirtsideDieRowDto(key, "D6")]))));

        Assert.Contains("signature", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADieThisAppDoesNotKnowIsRefusedWithTheListItWouldHaveTaken()
    {
        var refused = Assert.Throws<InvalidOperationException>(() =>
            new DirtsideGameService().CreateGame(new CreateDirtsideGameRequest(
                "Ridge 9",
                null,
                new DirtsideRulesProfileDto(FireControl: [new DirtsideDieRowDto("Basic", "D20")]))));

        Assert.Contains("D20", refused.Message, StringComparison.Ordinal);
        Assert.Contains("D12", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATableWithMoreRowsThanThisServerWillHoldIsRefused()
    {
        // The same reasoning as every other ceiling here: the row count arrives off an
        // unauthenticated create route and becomes the size of a dictionary. The number the mapping
        // stops at is its own; this sends an amount no rulebook has and asserts that something
        // stops it, rather than restating the ceiling and pinning the two together for no reason.
        var rows = Enumerable
            .Range(0, 5000)
            .Select(_ => new DirtsideDieRowDto("Basic", "D6"))
            .ToArray();

        var refused = Assert.Throws<InvalidOperationException>(() =>
            new DirtsideGameService().CreateGame(new CreateDirtsideGameRequest(
                "Ridge 9", null, new DirtsideRulesProfileDto(FireControl: rows))));

        Assert.Contains("more than this server will hold", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Two platoons on the table with Alpha activated, playing on the given tables.</summary>
    private static Guid Activated(DirtsideGameService service, DirtsideRulesProfileDto? profile)
    {
        var created = service.CreateGame(new CreateDirtsideGameRequest("Ridge 9", null, profile));
        service.AddPlatoon(created.GameId, DirtsideGameServiceTests.Platoon("alpha", "Alpha Troop", "blue"));
        service.AddPlatoon(created.GameId, DirtsideGameServiceTests.Platoon("bravo", "Bravo Troop", "red"));
        service.BeginTurn(created.GameId);
        service.ChooseFirstActivator(created.GameId, new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(created.GameId, new BeginDirtsideActivationRequest("blue", "alpha"));
        return created.GameId;
    }
}
