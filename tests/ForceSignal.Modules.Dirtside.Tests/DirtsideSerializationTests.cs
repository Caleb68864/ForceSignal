using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Game;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// A game surviving being written out and read back.
/// </summary>
/// <remarks>
/// These are equality assertions rather than field-by-field comparisons, which is the whole reason
/// the game is a value. That only works because the definitions and statuses compare structurally -
/// they did not at first, and the equality test in the game suite is what caught it.
/// </remarks>
public sealed class DirtsideSerializationTests
{
    [Fact]
    public void AFreshTableSurvivesTheRoundTrip()
    {
        var game = GameFixtures.TwoPlatoonGame();

        Assert.Equal(game, DirtsideGameSerialization.Restore(DirtsideGameSerialization.Save(game)));
    }

    [Fact]
    public void AGamePutDownMidActivationComesBackMidActivation()
    {
        // The reason the session is taken as saved rather than rebuilt from the roster: rebuilding
        // would throw the open frame away, and a game is most likely to be saved exactly here.
        var game = GameFixtures.Activated()
            .MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: true).Value!;

        var restored = DirtsideGameSerialization.Restore(DirtsideGameSerialization.Save(game));

        Assert.Equal(game, restored);
        Assert.NotNull(restored.Session.CurrentFrame);
        Assert.Equal(GameFixtures.Alpha, restored.Session.CurrentFrame!.Unit);
    }

    [Fact]
    public void WhatTheChitsDidToEachElementComesBackWithIt()
    {
        // The per-element status is a dictionary inside a dictionary, which is the part of this file
        // that could quietly lose something. A destroyed vehicle that came back alive would be a bad
        // way to find that out.
        var game = GameFixtures.Activated()
            .Fire(
                new FireCommand(
                    GameFixtures.Alpha, GameFixtures.AlphaOne, "Main Gun",
                    GameFixtures.Bravo, GameFixtures.BravoOne, WeaponRangeBand.Close),
                new ScriptedDice(1, 8),
                new ScriptedChitPot(DamageChit.Numerical(ChitColour.Red, 8))).Value!;

        var restored = DirtsideGameSerialization.Restore(DirtsideGameSerialization.Save(game));

        Assert.True(restored.Status(GameFixtures.Bravo).Element(GameFixtures.BravoOne).IsDestroyed);
        Assert.Equal(game, restored);
    }

    [Fact]
    public void TheLogComesBackInOrder()
    {
        var game = GameFixtures.Activated()
            .MoveElement(GameFixtures.AlphaOne, overHalfItsMovement: false).Value!
            .StandDown(GameFixtures.AlphaTwo).Value!;

        var restored = DirtsideGameSerialization.Restore(DirtsideGameSerialization.Save(game));

        // Compared as sequences on purpose. An ImmutableArray is a struct that compares by reference,
        // so Assert.Equal on two of them passes or fails for reasons that have nothing to do with
        // what is in them - which is exactly how this assertion failed when it was first written.
        Assert.Equal(game.Log.AsEnumerable(), restored.Log.AsEnumerable());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SomethingThatIsNotAGameIsRefused(string saved) =>
        Assert.Throws<ArgumentException>(() => DirtsideGameSerialization.Restore(saved));

    [Fact]
    public void RubbishIsRefusedWithSomethingReadable()
    {
        var error = Assert.Throws<ArgumentException>(() => DirtsideGameSerialization.Restore("{not json"));

        Assert.Contains("not a saved game", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WellFormedJsonThatIsNotAGameIsRefused() =>
        Assert.Throws<ArgumentException>(() => DirtsideGameSerialization.Restore("{}"));
}
