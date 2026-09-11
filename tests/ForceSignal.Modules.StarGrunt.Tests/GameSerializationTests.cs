using ForceSignal.Modules.StarGrunt.Game;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers writing a game out and reading it back.
/// </summary>
/// <remarks>
/// This is the assertion the immutable design was chosen for. Because a game is a record of
/// immutable collections with no behaviour hanging off it, restore fidelity is <c>Assert.Equal</c>
/// rather than a hand-written comparer that has to be kept in step with the type - and a field
/// added later is covered by these tests without anyone remembering to extend them.
/// </remarks>
public sealed class GameSerializationTests
{
    [Fact]
    public void AFreshGameComesBackTheSame()
    {
        var game = GameFixtures.TwoSquadGame();

        var restored = StarGruntGameSerialization.Restore(StarGruntGameSerialization.Save(game));

        Assert.Equal(game, restored);
    }

    [Fact]
    public void AGameMidActivationComesBackTheSame()
    {
        var game = GameFixtures.Firefight()
            .Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        var restored = StarGruntGameSerialization.Restore(StarGruntGameSerialization.Save(game));

        Assert.Equal(game, restored);
    }

    [Fact]
    public void CasualtiesAndSuppressionSurviveTheTrip()
    {
        var game = GameFixtures.Firefight()
            .Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        var restored = StarGruntGameSerialization.Restore(StarGruntGameSerialization.Save(game));

        Assert.Equal(7, restored.Status(GameFixtures.Bravo).FiguresAlive);
        Assert.Equal(1, restored.Status(GameFixtures.Bravo).SuppressionMarkers);
    }

    [Fact]
    public void ARestoredGameCanBePlayedOn()
    {
        var game = GameFixtures.Firefight()
            .Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        var restored = StarGruntGameSerialization.Restore(StarGruntGameSerialization.Save(game));

        // The frame stack came back with it, so the open activation is still open and closable.
        Assert.True(restored.EndActivation().IsAllowed);
    }

    [Fact]
    public void TheLogComesBackInOrder()
    {
        var game = GameFixtures.Firefight()
            .Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        var restored = StarGruntGameSerialization.Restore(StarGruntGameSerialization.Save(game));

        // Compared as sequences on purpose. Handed two ImmutableArrays, Assert.Equal picks the
        // struct's own equality - which is the reference comparison this project keeps having to
        // work around - and fails on two identical logs.
        Assert.Equal(game.Log.AsEnumerable(), restored.Log.AsEnumerable());
    }

    [Fact]
    public void NonsenseIsNotAGame()
    {
        Assert.Throws<ArgumentException>(() => StarGruntGameSerialization.Restore("{}"));
        Assert.Throws<ArgumentException>(() => StarGruntGameSerialization.Restore("   "));
    }
}
