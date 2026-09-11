using ForceSignal.Modules.StarGrunt.Game;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers where a volley's casualties land.
/// </summary>
/// <remarks>
/// Gaps 4, 5 and 6. Hits are allocated randomly across the squad's figures; a figure taking two
/// wound results in one resolution is dead; a wounded figure is a casualty rather than a fighter;
/// and the squad leader going down hands the unit a suppression marker.
/// </remarks>
public sealed class GameCasualtyTests
{
    // The worked firefight scores two potential hits: one stopped, one kill.
    private static readonly int[] OneKill = GameFixtures.AKillAndAStop;

    [Fact]
    public void AKilledFigureLeavesTheUnitAltogether()
    {
        var after = Fire(OneKill, allocation: [3]);

        Assert.Equal(7, after.Status(GameFixtures.Bravo).FiguresAlive);
        Assert.Equal(0, after.Status(GameFixtures.Bravo).FiguresWounded);
    }

    [Fact]
    public void AWoundedFigureStopsFightingButStaysWithTheUnit()
    {
        // Two potential hits, both scoring wounds, landing on different figures: two casualties,
        // neither dead. A wounded trooper is carried, not counted among the rifles.
        var after = Fire(TwoWoundsOnDifferentFigures, allocation: [2, 5]);

        var status = after.Status(GameFixtures.Bravo);
        Assert.Equal(6, status.FiguresAlive);
        Assert.Equal(2, status.FiguresWounded);
    }

    [Fact]
    public void TwoWoundsOnOneFigureInOneResolutionKillIt()
    {
        // The same two wounds, both allocated to the same trooper.
        var after = Fire(TwoWoundsOnDifferentFigures, allocation: [2, 2]);

        var status = after.Status(GameFixtures.Bravo);
        Assert.Equal(7, status.FiguresAlive);
        Assert.Equal(0, status.FiguresWounded);
    }

    [Fact]
    public void WoundsFromSeparateVolleysDoNotPairIntoADeath()
    {
        // The rule is scoped to one resolution: a trooper wounded last turn is a casualty already,
        // and a fresh wound elsewhere cannot reach back and finish him.
        var first = Fire(TwoWoundsOnDifferentFigures, allocation: [2, 5]);
        var status = first.Status(GameFixtures.Bravo);

        Assert.Equal(6, status.FiguresAlive);
        Assert.Equal(2, status.FiguresWounded);
    }

    [Fact]
    public void TheSquadLeaderGoingDownSuppressesTheUnit()
    {
        // Errata: a wounded or killed squad leader hands the squad a suppression marker. The leader
        // is the first figure, so a hit allocated there is a hit on him.
        var after = Fire(OneKill, allocation: [0]);

        Assert.Equal(2, after.Status(GameFixtures.Bravo).SuppressionMarkers);
    }

    [Fact]
    public void ACasualtyElsewhereSuppressesOnlyOnce()
    {
        // Effective fire always suppresses. Losing the leader is what adds the second marker.
        var after = Fire(OneKill, allocation: [4]);

        Assert.Equal(1, after.Status(GameFixtures.Bravo).SuppressionMarkers);
    }

    [Fact]
    public void TheUnitRecordsThatItsLeaderIsDown()
    {
        var after = Fire(OneKill, allocation: [0]);

        Assert.True(after.Status(GameFixtures.Bravo).IsLeaderDown);
    }

    [Fact]
    public void CasualtiesCannotExceedTheFiguresThereAreToLose()
    {
        var game = GameFixtures.Firefight()
            .WithStatus(GameFixtures.Bravo, status => status with { FiguresAlive = 1, FiguresWounded = 0 });

        var after = game.Fire(GameFixtures.Volley(), new ScriptedDice(OneKill), TestRangeTable.Invented, new ScriptedAllocator(0)).Value!;

        var status = after.Status(GameFixtures.Bravo);
        Assert.Equal(0, status.FiguresAlive);
        Assert.True(status.IsWipedOut);
    }

    [Fact]
    public void WhoWasHitIsRecordedForTheTable()
    {
        var after = Fire(OneKill, allocation: [0]);

        Assert.Contains(after.Log, entry => entry.Contains("leader", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Two potential hits that both wound: impact beats armour but not by double.</summary>
    private static readonly int[] TwoWoundsOnDifferentFigures = [6, 7, 5, 4, 5, 5, 4, 5, 4];

    private static StarGruntGame Fire(int[] dice, int[] allocation) =>
        GameFixtures.Firefight()
            .Fire(GameFixtures.Volley(), new ScriptedDice(dice), TestRangeTable.Invented, new ScriptedAllocator(allocation))
            .Value!;
}
