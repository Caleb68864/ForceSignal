using System.Globalization;
using ForceSignal.Application.Ground;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A stored StarGrunt game whose units never said what their Leadership Value was.
/// </summary>
/// <remarks>
/// <para>
/// <c>UnitDefinition.LeadershipValue</c> opened on <c>= 2</c>. A default on a property that is
/// deserialised is not a C# convenience: it is what a JSON caller who entered nothing gets, and the
/// Leadership Value is the number every morale roll in the game is measured against. StarGrunt's
/// units carried a <c>LeadershipDie</c> before they carried a value, so a document from that era has
/// no such property at all.
/// </para>
/// <para>
/// Fixed the way the range page was rather than the way <c>QualityDie</c> was. A missing quality die
/// refuses the blob, because there is no unentered rung on the ladder to stand on; a missing
/// Leadership Value refuses the <em>action</em>, so the game opens, plays, and says by name what it
/// wants the moment something reads it. Retiring a stored game costs a table their evening.
/// </para>
/// </remarks>
public sealed class StarGruntLeadershipTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGameStoredBeforeLeadershipWasRatedStillOpensAndThenSaysWhatItWants(bool absent)
    {
        // Written by hand the way the service wrote it - format 1, a token, the module's document -
        // rather than by the service under test, which could only ever write the new shape.
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.Seed(id, OldRow(Unrated(absent)));

        var service = new StarGruntGameService(new ScriptedQualityDice([1]), store, new ScriptedFigureAllocator());

        // It opened: nothing was skipped, and both squads are on the table.
        Assert.Empty(service.SkippedSaves);
        Assert.Equal(2, service.GetSnapshot(id).Units.Count);

        // And it says so on every snapshot rather than only when it refuses, which is what lets a
        // screen show the gap before somebody rolls. A 2 here is the defect itself: a rating nobody
        // gave, reported to the table as fact.
        Assert.Null(service.GetSnapshot(id).Units.Single(unit => unit.Id == "alpha").LeadershipValue);

        // It plays, and the first roll that reads the card is refused by name.
        service.BeginTurn(id);
        service.ChooseFirstActivator(id, new ChooseFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(id, new BeginStarGruntActivationRequest("blue", "alpha"));

        var refused = Assert.Throws<InvalidOperationException>(
            () => service.TakeConfidenceTest(id, new StarGruntConfidenceTestRequest("alpha", 2)));

        Assert.Contains("Leadership Value", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Alpha Squad", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoredGameWhoseCardsDoSayStillTakesItsNerveTest()
    {
        // The accept control. Without it a guard that refused every stored game would pass the two
        // cases above and be called a fix.
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.Seed(id, OldRow(StarGruntGameSerialization.Save(TwoSquads())));

        var service = new StarGruntGameService(new ScriptedQualityDice([6]), store, new ScriptedFigureAllocator());

        Assert.Empty(service.SkippedSaves);
        Assert.Equal(2, service.GetSnapshot(id).Units.Single(unit => unit.Id == "alpha").LeadershipValue);

        service.BeginTurn(id);
        service.ChooseFirstActivator(id, new ChooseFirstActivatorRequest("blue", TakeIt: true));
        service.BeginActivation(id, new BeginStarGruntActivationRequest("blue", "alpha"));

        var after = service.TakeConfidenceTest(id, new StarGruntConfidenceTestRequest("alpha", 2));

        Assert.Contains("tested its nerve", after.Log[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void AUnitAddedOverTheWireStillHasToNameItsLeadershipValue()
    {
        // The other door, and it is already shut: the add-unit request carries the value as a plain
        // number, so a body that omits it arrives as a zero and is refused. The nullable definition
        // must not quietly reopen it - a unit typed in at the table has a card in front of it.
        var service = new StarGruntGameService();
        var game = service.CreateGame(new CreateStarGruntGameRequest("Hill 43")).GameId;

        var refused = Assert.Throws<InvalidOperationException>(() => service.AddUnit(game, new AddStarGruntUnitRequest(
            "alpha",
            "Alpha Squad",
            "blue",
            "Squad",
            QualityDie: 8,
            LeadershipValue: 0,
            Figures: [new StarGruntFigureDto(6)],
            Weapons: [new StarGruntWeaponDto("Rifles", 10)])));

        Assert.Contains("Leadership Value", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Two squads, saved, with the Leadership Values taken out of the bytes.</summary>
    /// <param name="absent">True to drop the property, false to leave it there and null.</param>
    private static string Unrated(bool absent)
    {
        var saved = StarGruntGameSerialization.Save(TwoSquads());
        var rewritten = absent
            ? saved.Replace("\"LeadershipValue\":2,", string.Empty, StringComparison.Ordinal)
            : saved.Replace("\"LeadershipValue\":2", "\"LeadershipValue\":null", StringComparison.Ordinal);

        // Reached-the-subject: a rewrite that matched nothing would leave this testing an ordinary
        // stored game and passing for the wrong reason.
        Assert.NotEqual(saved, rewritten);
        return rewritten;
    }

    private static StarGruntGame TwoSquads() =>
        StarGruntGame.Create("Hill 43")
            .WithUnit(Squad("alpha", "Alpha Squad", "blue"))
            .WithUnit(Squad("bravo", "Bravo Squad", "red"));

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

    /// <summary>A stored row in the shape this service writes: format 1, a token, the document.</summary>
    private static string OldRow(string document) =>
        string.Concat(
            "{\"formatVersion\":1,\"token\":\"old-token\",\"lastActivity\":\"",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            "\",\"game\":",
            document,
            "}");
}
