using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.StarGrunt.Combat;
using ForceSignal.Modules.StarGrunt.Game;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers folding a squad's support weapons into its small-arms fire.
/// </summary>
/// <remarks>
/// Gap 10. The support flag was recorded and never read, and the dice were a free-form list the
/// player typed per shot - which meant the app could not know a weapon had been used, and so could
/// not enforce the rule that a weapon folded into squad fire may not also fire separately that
/// activation.
/// </remarks>
public sealed class GameSupportFireTests
{
    [Fact]
    public void ASupportWeaponAddsItsOwnDieToTheVolley()
    {
        // Quality, small arms and the SAW: three dice against the target's one.
        var game = GameFixtures.Firefight();

        var after = game.Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.True(after.IsAllowed);
        Assert.Contains(after.Value!.Log, entry => entry.Contains("Squad Support", StringComparison.Ordinal));
    }

    [Fact]
    public void AWeaponFoldedIntoSquadFireCannotAlsoFireOnItsOwn()
    {
        // The trade-off the rules make explicit: weight of fire now, or its own punch later.
        var game = GameFixtures.Firefight()
            .Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        var refused = game.Fire(Volley() with { WeaponName = "Squad Support" }, new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented);

        Assert.False(refused.IsAllowed);
        Assert.Contains("already fired", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSmallArmsStillCannotFireTwiceEither()
    {
        var game = GameFixtures.Firefight()
            .Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        var refused = game.Fire(Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented);

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void AWeaponTheUnitDoesNotCarryCannotBeFoldedIn()
    {
        var refused = GameFixtures.Firefight()
            .Fire(Volley("Mortar"), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented);

        Assert.False(refused.IsAllowed);
        Assert.Contains("Mortar", refused.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyASupportWeaponCanBeFoldedIn()
    {
        // Rifles are the small arms, not a support weapon to add on top of themselves.
        var refused = GameFixtures.Firefight()
            .Fire(Volley("Rifles"), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented);

        Assert.False(refused.IsAllowed);
        Assert.Contains("support weapon", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AWeaponMarkedAsNeverJoiningSquadFireIsRefused()
    {
        // Some man-portable weapons always need an action of their own, whatever else is firing.
        var game = GameFixtures.Firefight()
            .WithUnitEdit(GameFixtures.Alpha, unit => unit with
            {
                Weapons = [.. unit.Weapons.Select(weapon => weapon.Name == "Squad Support"
                    ? weapon with { NeverJoinsSquadFire = true }
                    : weapon)],
            });

        var refused = game.Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented);

        Assert.False(refused.IsAllowed);
        Assert.Contains("on its own", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheVolleyIsStillTheSmallArmsVolley()
    {
        // Support weapons add weight of fire, never their own heavier impact: every hit is resolved
        // on the small arms. The volley is named for the rifles, with the support weapon alongside.
        var after = GameFixtures.Firefight()
            .Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman()).Value!;

        Assert.Contains(after.Log, entry =>
            entry.Contains("fired Rifles with Squad Support", StringComparison.Ordinal));
    }

    [Fact]
    public void FiringASupportWeaponAloneUsesItsOwnImpact()
    {
        // The other half of the trade: fired as its own action, the support weapon's punch counts.
        var after = GameFixtures.Firefight()
            .Fire(Volley() with { WeaponName = "Squad Support" }, new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.True(after.IsAllowed);
    }

    [Fact]
    public void ASupportWeaponWhoseCardGaveNoDieIsRefusedByName()
    {
        // A support weapon with no firepower die used to get a D6, because the profile defaulted to
        // one - a die rating this app wrote onto a weapon somebody else's record card describes.
        // With the default gone there are two wrong answers and one right one: inventing a die
        // again, or quietly leaving the weapon out of the roll while saying the volley included it.
        // It is refused, and the refusal names the weapon so the player knows which card to fill in.
        var game = GameFixtures.Firefight()
            .WithUnitEdit(GameFixtures.Alpha, unit => unit with
            {
                Weapons = [.. unit.Weapons.Select(weapon => weapon.Name == "Squad Support"
                    ? weapon with { SupportFirepowerDie = null }
                    : weapon)],
            });

        var refused = game.Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented);

        Assert.False(refused.IsAllowed);
        Assert.Contains("Squad Support", refused.Reason!, StringComparison.Ordinal);
        Assert.Contains("does not say what die", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASupportWeaponWhoseCardDidGiveADieStillJoinsTheVolley()
    {
        // The control that must be accepted, and it is the one that matters here: refusing a weapon
        // for having no die is only right if a weapon that has one is still let through. The
        // fixture's Squad Support carries a D8 off its card.
        var game = GameFixtures.Firefight();

        // Reached-the-subject: the weapon really does carry a die, so the pass below is the guard
        // letting a good weapon through rather than the guard not being reached.
        Assert.Equal(
            QualityDie.D8,
            game.Units[GameFixtures.Alpha]
                .Weapons.Single(weapon => weapon.Name == "Squad Support").SupportFirepowerDie);

        var after = game.Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.True(after.IsAllowed);
        Assert.Contains(after.Value!.Log, entry => entry.Contains("fired Rifles with Squad Support", StringComparison.Ordinal));
    }

    [Fact]
    public void AWeaponWithNoSupportDieCanStillFireOnItsOwn()
    {
        // The second control. The die a weapon adds to somebody else's volley has nothing to do with
        // its own shot, so a card that leaves it out must not cost the weapon its own action.
        var game = GameFixtures.Firefight()
            .WithUnitEdit(GameFixtures.Alpha, unit => unit with
            {
                Weapons = [.. unit.Weapons.Select(weapon => weapon.Name == "Squad Support"
                    ? weapon with { SupportFirepowerDie = null }
                    : weapon)],
            });

        var after = game.Fire(
            Volley() with { WeaponName = "Squad Support" },
            new ScriptedDice(GameFixtures.AKillAndAStop),
            TestRangeTable.Invented,
            GameFixtures.HitsARifleman());

        Assert.True(after.IsAllowed);
    }

    private static FireCommand Volley(params string[] support) =>
        GameFixtures.Volley() with { SupportWeapons = [.. support] };
}
