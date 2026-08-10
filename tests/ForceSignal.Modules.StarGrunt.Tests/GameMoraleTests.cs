using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Morale;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers the morale half of the game: confidence tests, rallying and reorganising.
/// </summary>
/// <remarks>
/// Gaps 2 and 3. All three engines were built and tested and never called, so confidence was a
/// decoration on the unit card and rally and reorganise were buttons that spent an action and did
/// nothing.
/// </remarks>
public sealed class GameMoraleTests
{
    // A Regular squad rolls a D8 against leadership 2 plus whatever threat is declared.

    [Fact]
    public void PassingATestLeavesConfidenceWhereItWas()
    {
        // Threat 2 against leadership 2 is a score of 4; a 6 beats it.
        var after = GameFixtures.TwoSquadGame()
            .TakeConfidenceTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(6)).Value!;

        Assert.Equal(ConfidenceLevel.Confident, after.Status(GameFixtures.Alpha).Confidence);
    }

    [Fact]
    public void FailingATestCostsOneLevel()
    {
        // A 4 against a score of 4 is a miss - the roll has to beat it, not match it.
        var after = GameFixtures.TwoSquadGame()
            .TakeConfidenceTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(4)).Value!;

        Assert.Equal(ConfidenceLevel.Steady, after.Status(GameFixtures.Alpha).Confidence);
    }

    [Fact]
    public void FailingBadlyCostsTwo()
    {
        // Half of 4 or less is a bad failure.
        var after = GameFixtures.TwoSquadGame()
            .TakeConfidenceTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(2)).Value!;

        Assert.Equal(ConfidenceLevel.Shaken, after.Status(GameFixtures.Alpha).Confidence);
    }

    [Fact]
    public void ATestIsNotAnActionAndNeedsNoActivation()
    {
        // Taken immediately when something happens, to a unit that has not gone yet and may never
        // go this turn. Binding it to an activation would be the wrong shape entirely.
        var game = GameFixtures.TwoSquadGame();

        var after = game.TakeConfidenceTest(GameFixtures.Bravo, threatLevel: 3, new ScriptedDice(1));

        Assert.True(after.IsAllowed);
        Assert.Null(after.Value!.Session.CurrentFrame);
    }

    [Fact]
    public void AUnitCanBeTestedMoreThanOnceInATurn()
    {
        var game = GameFixtures.TwoSquadGame()
            .TakeConfidenceTest(GameFixtures.Alpha, 2, new ScriptedDice(4)).Value!
            .TakeConfidenceTest(GameFixtures.Alpha, 2, new ScriptedDice(4)).Value!;

        Assert.Equal(ConfidenceLevel.Shaken, game.Status(GameFixtures.Alpha).Confidence);
    }

    [Fact]
    public void TheTestIsRecordedWithWhatItNeededAndWhatItRolled()
    {
        var after = GameFixtures.TwoSquadGame()
            .TakeConfidenceTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(4)).Value!;

        Assert.Contains(after.Log, entry =>
            entry.Contains("Alpha Squad", StringComparison.Ordinal) && entry.Contains("Steady", StringComparison.Ordinal));
    }

    [Fact]
    public void ANegativeThreatIsNotAThreat()
    {
        var refused = GameFixtures.TwoSquadGame()
            .TakeConfidenceTest(GameFixtures.Alpha, threatLevel: -1, new ScriptedDice(4));

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void RallyingLiftsTheUnitOneLevel()
    {
        // Score to beat is both leadership values summed: 2 + 2 = 4, so a 6 succeeds.
        var game = Shaken(Charlie);

        var after = game.Rally(GameFixtures.Alpha, Charlie, new ScriptedDice(6)).Value!;

        Assert.Equal(ConfidenceLevel.Steady, after.Status(Charlie).Confidence);
    }

    [Fact]
    public void AFailedRallyStillCostsTheRallyingUnitItsAction()
    {
        var game = Shaken(Charlie);

        var after = game.Rally(GameFixtures.Alpha, Charlie, new ScriptedDice(1)).Value!;

        Assert.Equal(ConfidenceLevel.Shaken, after.Status(Charlie).Confidence);
        Assert.Contains(after.Session.CurrentFrame!.Steps, step => step.Kind.Contains("Rally", StringComparison.Ordinal));
    }

    [Fact]
    public void OneRallyLiftsOneLevelAtMost()
    {
        var game = Shaken(Charlie, ConfidenceLevel.Broken);

        var after = game.Rally(GameFixtures.Alpha, Charlie, new ScriptedDice(6)).Value!;

        Assert.Equal(ConfidenceLevel.Shaken, after.Status(Charlie).Confidence);
    }

    [Fact]
    public void APeerCannotRallyAPeer()
    {
        // Two squads cannot talk each other round; rallying comes from a level above.
        var game = GameFixtures.TwoSquadGame()
            .WithUnit(GameFixtures.Squad(Charlie, "Charlie Squad", GameFixtures.Blue))
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Red, takeIt: false).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!
            .WithStatus(Charlie, status => status with { Confidence = ConfidenceLevel.Shaken });

        var refused = game.Rally(GameFixtures.Alpha, Charlie, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
        Assert.Contains("senior", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AUnitCannotRallyItself()
    {
        var game = Shaken(GameFixtures.Alpha);

        var refused = game.Rally(GameFixtures.Alpha, GameFixtures.Alpha, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
        Assert.Contains("itself", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AUnitAtFullConfidenceHasNothingToRally()
    {
        var game = Commanding();

        var refused = game.Rally(GameFixtures.Alpha, Charlie, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void FatigueCapsHowFarAUnitCanBeRalliedBack()
    {
        // A tired unit starts Steady and can never be lifted past it, however well it rolls. From
        // Shaken it can come back one level - to the cap - and no further.
        var game = Commanding()
            .WithUnitEdit(Charlie, unit => unit with { Fatigue = FatigueLevel.Tired })
            .WithStatus(Charlie, status => status with { Confidence = ConfidenceLevel.Shaken });

        var after = game.Rally(GameFixtures.Alpha, Charlie, new ScriptedDice(6)).Value!;
        Assert.Equal(ConfidenceLevel.Steady, after.Status(Charlie).Confidence);

        // At the cap there is nothing left to rally, so the commander is not allowed to waste an
        // action finding that out.
        var refused = after.Rally(GameFixtures.Alpha, Charlie, new ScriptedDice(6));
        Assert.False(refused.IsAllowed);
        Assert.Contains("fatigue", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReorganisingPutsTheUnitBackInOrder()
    {
        var game = Activated(GameFixtures.Alpha)
            .WithStatus(GameFixtures.Alpha, status => status with { IsDisorganised = true });

        var after = game.Reorganise(GameFixtures.Alpha).Value!;

        Assert.False(after.Status(GameFixtures.Alpha).IsDisorganised);
    }

    [Fact]
    public void AUnitInGoodOrderHasNothingToReorganise()
    {
        var refused = Activated(GameFixtures.Alpha).Reorganise(GameFixtures.Alpha);

        Assert.False(refused.IsAllowed);
        Assert.Contains("good order", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReorganisingCostsAnAction()
    {
        var game = Activated(GameFixtures.Alpha)
            .WithStatus(GameFixtures.Alpha, status => status with { IsDisorganised = true });

        var after = game.Reorganise(GameFixtures.Alpha).Value!;

        Assert.Contains(after.Session.CurrentFrame!.Steps, step => step.Kind.Contains("Reorganise", StringComparison.Ordinal));
    }

    /// <summary>Alpha promoted to platoon command and activated, with the named unit wavering.</summary>
    private static StarGruntGame Shaken(UnitId unit, ConfidenceLevel level = ConfidenceLevel.Shaken) =>
        Commanding().WithStatus(unit, status => status with { Confidence = level });

    /// <summary>
    /// Alpha as a platoon commander over Charlie, both blue, with Alpha activated. Rallying comes
    /// from above, so a table of two peer squads cannot demonstrate it at all.
    /// </summary>
    private static StarGruntGame Commanding() =>
        GameFixtures.TwoSquadGame()
            .WithUnit(GameFixtures.Squad(Charlie, "Charlie Squad", GameFixtures.Blue))
            .WithUnitEdit(GameFixtures.Alpha, unit => unit with { Level = CommandLevel.Platoon })
            .BeginTurn().Value!
            // Blue now has two units, so the choice belongs to Red, who hands it over.
            .ChooseFirstActivator(GameFixtures.Red, takeIt: false).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;

    private static readonly UnitId Charlie = new("charlie");

    private static StarGruntGame Activated(UnitId unit) =>
        GameFixtures.TwoSquadGame()
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, unit).Value!;
}
