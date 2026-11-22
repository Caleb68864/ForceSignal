using ForceSignal.Application.Ground;
using ForceSignal.Contracts.Ground;
using ForceSignal.TestSupport;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A nerve test with no threat level is refused by name on the server too (playtest M8). Left out of
/// a request the level used to bind to 0 and roll as threat 0.
/// </summary>
public sealed class ThreatLevelEnteredTests
{
    [Fact]
    public void AStarGruntConfidenceTestWithNoThreatLevelIsRefused_AndOneWithZeroIsNot()
    {
        var service = new StarGruntGameService();
        var game = service.CreateGame(new CreateStarGruntGameRequest("Hill 43", StarGruntTestProfile.LeadershipValuesOnly)).GameId;
        service.AddUnit(game, new AddStarGruntUnitRequest("alpha", "Alpha", "blue", "Squad", 8, 3, [new StarGruntFigureDto(8)], [new StarGruntWeaponDto("Rifles", 8)]));

        var refused = Assert.Throws<InvalidOperationException>(() => service.TakeConfidenceTest(game, new StarGruntConfidenceTestRequest("alpha", null)));
        Assert.Contains("No threat level was entered", refused.Message, StringComparison.Ordinal);
        Assert.Contains(service.TakeConfidenceTest(game, new StarGruntConfidenceTestRequest("alpha", 0)).Log, entry => entry.Contains("Alpha", StringComparison.Ordinal));
    }

    [Fact]
    public void ADirtsideNerveTestWithNoThreatLevelIsRefused()
    {
        var service = new DirtsideGameService();
        var game = service.CreateGame(new CreateDirtsideGameRequest("Ridge", null, DirtsideTestProfile.Invented)).GameId;
        service.AddPlatoon(game, DirtsideGameServiceTests.Platoon("alpha", "Alpha Troop", "blue") with { QualityDie = "D8", LeadershipValue = 3 });

        Assert.Throws<InvalidOperationException>(() => service.TakeConfidenceTest(game, new DirtsideNerveTestRequest("alpha", null)));
        Assert.Throws<InvalidOperationException>(() => service.TakeReactionTest(game, new DirtsideNerveTestRequest("alpha", null)));
    }
}
