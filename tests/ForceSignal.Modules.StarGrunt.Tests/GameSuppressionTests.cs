using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers getting a pinned unit's head back up.
/// </summary>
/// <remarks>
/// Without this the game stops. Fire puts suppression on, a suppressed unit may not move or fire,
/// and until now nothing anywhere took a marker off - so the first squad shot at was pinned for the
/// rest of the game. See gap 1 in `stargrunt-fidelity-gaps.md`.
/// </remarks>
public sealed class GameSuppressionTests
{
    [Fact]
    public void ASuccessfulAttemptTakesOneMarkerOff()
    {
        // Leadership value 2, so the quality die has to beat 2. A 6 does.
        var game = Pinned(markers: 2);

        var after = game.RemoveSuppression(GameFixtures.Bravo, new ScriptedDice(6)).Value!;

        Assert.Equal(1, after.Status(GameFixtures.Bravo).SuppressionMarkers);
    }

    [Fact]
    public void AFailedAttemptStillCostsTheAction()
    {
        var game = Pinned(markers: 2);

        var after = game.RemoveSuppression(GameFixtures.Bravo, new ScriptedDice(1)).Value!;

        Assert.Equal(2, after.Status(GameFixtures.Bravo).SuppressionMarkers);
        // The action is spent either way: trying and failing is how a pinned unit loses its turn.
        Assert.Contains(after.Session.CurrentFrame!.Steps, step => step.Kind.Contains("RemoveSuppression", StringComparison.Ordinal));
    }

    [Fact]
    public void OneActionTakesOneMarkerAtBest()
    {
        var game = Pinned(markers: 3);

        var after = game.RemoveSuppression(GameFixtures.Bravo, new ScriptedDice(6)).Value!;

        Assert.Equal(2, after.Status(GameFixtures.Bravo).SuppressionMarkers);
    }

    [Fact]
    public void TheRollIsRecordedForTheTableToSee()
    {
        var game = Pinned(markers: 1);

        var after = game.RemoveSuppression(GameFixtures.Bravo, new ScriptedDice(6)).Value!;

        Assert.Contains(after.Log, entry => entry.Contains("Bravo Squad", StringComparison.Ordinal));
    }

    [Fact]
    public void AUnitThatIsNotPinnedHasNothingToShakeOff()
    {
        var game = Activated(GameFixtures.Bravo);

        var refused = game.RemoveSuppression(GameFixtures.Bravo, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
        Assert.Contains("not suppressed", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItCannotBeDoneOutsideAnActivation()
    {
        var game = GameFixtures.TwoSquadGame()
            .WithStatus(GameFixtures.Bravo, status => status with { SuppressionMarkers = 2 });

        var refused = game.RemoveSuppression(GameFixtures.Bravo, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void ClearingSuppressionLetsTheUnitMoveAgain()
    {
        // The whole point: a pinned unit that shakes the last marker off is back in the game.
        var game = Pinned(markers: 1).RemoveSuppression(GameFixtures.Bravo, new ScriptedDice(6)).Value!;

        Assert.Equal(0, game.Status(GameFixtures.Bravo).SuppressionMarkers);
        Assert.True(game.TakeStep(StarGruntSteps.Move()).IsAllowed);
    }

    [Fact]
    public void AUnitStillPinnedAfterTryingCannotMove()
    {
        var game = Pinned(markers: 1).RemoveSuppression(GameFixtures.Bravo, new ScriptedDice(1)).Value!;

        var refused = game.TakeStep(StarGruntSteps.Move());

        Assert.False(refused.IsAllowed);
        Assert.Contains("suppressed", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Bravo activated, and carrying the markers named.</summary>
    private static StarGruntGame Pinned(int markers) =>
        Activated(GameFixtures.Bravo).WithStatus(GameFixtures.Bravo, status => status with { SuppressionMarkers = markers });

    private static StarGruntGame Activated(UnitId unit) =>
        GameFixtures.TwoSquadGame()
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Red, takeIt: true).Value!
            .BeginActivation(GameFixtures.Red, unit).Value!;
}
