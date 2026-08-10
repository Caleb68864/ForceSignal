using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers what the game says a unit may do.
/// </summary>
/// <remarks>
/// <para>
/// This exists so no client ever works it out for itself. The Full Thrust side grew its own copy of
/// the firing rules, that copy was incomplete, and the console spent months offering shots the
/// server refused; two rounds of work went into removing it. There is no StarGrunt client yet, and
/// the way to keep it that way is to answer the question before anybody is tempted to.
/// </para>
/// <para>
/// The load-bearing assertion in this file is that the projection and the command return the
/// <em>same words</em>. That can only hold while there is one implementation.
/// </para>
/// </remarks>
public sealed class GameLegalityTests
{
    [Fact]
    public void ActivationLegalityGivesTheSameAnswerTheCommandWouldGive()
    {
        var game = GameFixtures.TwoSquadGame().BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!;

        var legality = game.LegalityFor(GameFixtures.Bravo);
        var refused = game.BeginActivation(GameFixtures.Red, GameFixtures.Bravo);

        Assert.False(legality.CanActivate);
        Assert.False(refused.IsAllowed);
        Assert.Equal(refused.Reason, legality.ActivationBlocker);
    }

    [Fact]
    public void TheUnitWhoseGoItIsMayActivate()
    {
        var game = GameFixtures.TwoSquadGame().BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!;

        var legality = game.LegalityFor(GameFixtures.Alpha);

        Assert.True(legality.CanActivate);
        Assert.Null(legality.ActivationBlocker);
    }

    [Fact]
    public void WeaponLegalityGivesTheSameAnswerFiringWouldGive()
    {
        // Rifles have already fired this activation, so a second volley is refused.
        var game = GameFixtures.Firefight()
            .Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop)).Value!;

        var legality = game.LegalityFor(GameFixtures.Alpha);
        var refused = game.Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop));

        var rifles = legality.Weapons.Single(weapon => weapon.Name == "Rifles");
        Assert.False(rifles.CanFire);
        Assert.Equal(refused.Reason, rifles.Blocker);
    }

    [Fact]
    public void AWeaponThatHasNotFiredYetIsOffered()
    {
        var legality = GameFixtures.Firefight().LegalityFor(GameFixtures.Alpha);

        Assert.All(legality.Weapons, weapon => Assert.True(weapon.CanFire));
    }

    [Fact]
    public void AWipedOutUnitIsOfferedNoWeapons()
    {
        var game = GameFixtures.Firefight()
            .WithStatus(GameFixtures.Alpha, status => status with { FiguresAlive = 0 });

        var legality = game.LegalityFor(GameFixtures.Alpha);

        Assert.All(legality.Weapons, weapon => Assert.False(weapon.CanFire));
    }

    [Fact]
    public void AWipedOutUnitCannotBeActivated()
    {
        // From a turn nobody has started activating, so the refusal is about the casualties rather
        // than about a frame that happens to be open.
        var game = GameFixtures.TwoSquadGame().BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .WithStatus(GameFixtures.Alpha, status => status with { FiguresAlive = 0 });

        var legality = game.LegalityFor(GameFixtures.Alpha);
        var refused = game.BeginActivation(GameFixtures.Blue, GameFixtures.Alpha);

        Assert.False(legality.CanActivate);
        Assert.False(refused.IsAllowed);
        Assert.Equal(refused.Reason, legality.ActivationBlocker);
    }

    [Fact]
    public void LegalityIsReportedForEveryUnitOnTheTable()
    {
        var game = GameFixtures.TwoSquadGame();

        var all = game.Legality();

        Assert.Equal(2, all.Count);
        Assert.Contains(GameFixtures.Alpha, all.Keys);
        Assert.Contains(GameFixtures.Bravo, all.Keys);
    }

    [Fact]
    public void AskingAboutAUnitThatIsNotThereIsAnError()
    {
        Assert.Throws<ArgumentException>(() => GameFixtures.TwoSquadGame().LegalityFor(new UnitId("ghost")));
    }

    [Fact]
    public void AskingChangesNothing()
    {
        var game = GameFixtures.Firefight();

        _ = game.Legality();

        Assert.Equal(GameFixtures.Firefight(), game);
    }
}
