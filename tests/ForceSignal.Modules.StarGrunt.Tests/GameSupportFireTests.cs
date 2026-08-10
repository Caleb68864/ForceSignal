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

        var after = game.Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop), GameFixtures.HitsARifleman());

        Assert.True(after.IsAllowed);
        Assert.Contains(after.Value!.Log, entry => entry.Contains("Squad Support", StringComparison.Ordinal));
    }

    [Fact]
    public void AWeaponFoldedIntoSquadFireCannotAlsoFireOnItsOwn()
    {
        // The trade-off the rules make explicit: weight of fire now, or its own punch later.
        var game = GameFixtures.Firefight()
            .Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop), GameFixtures.HitsARifleman()).Value!;

        var refused = game.Fire(Volley() with { WeaponName = "Squad Support" }, new ScriptedDice(GameFixtures.AKillAndAStop));

        Assert.False(refused.IsAllowed);
        Assert.Contains("already fired", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSmallArmsStillCannotFireTwiceEither()
    {
        var game = GameFixtures.Firefight()
            .Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop), GameFixtures.HitsARifleman()).Value!;

        var refused = game.Fire(Volley(), new ScriptedDice(GameFixtures.AKillAndAStop));

        Assert.False(refused.IsAllowed);
    }

    [Fact]
    public void AWeaponTheUnitDoesNotCarryCannotBeFoldedIn()
    {
        var refused = GameFixtures.Firefight()
            .Fire(Volley("Mortar"), new ScriptedDice(GameFixtures.AKillAndAStop));

        Assert.False(refused.IsAllowed);
        Assert.Contains("Mortar", refused.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyASupportWeaponCanBeFoldedIn()
    {
        // Rifles are the small arms, not a support weapon to add on top of themselves.
        var refused = GameFixtures.Firefight()
            .Fire(Volley("Rifles"), new ScriptedDice(GameFixtures.AKillAndAStop));

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

        var refused = game.Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop));

        Assert.False(refused.IsAllowed);
        Assert.Contains("on its own", refused.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheVolleyIsStillTheSmallArmsVolley()
    {
        // Support weapons add weight of fire, never their own heavier impact: every hit is resolved
        // on the small arms. The volley is named for the rifles, with the support weapon alongside.
        var after = GameFixtures.Firefight()
            .Fire(Volley("Squad Support"), new ScriptedDice(GameFixtures.AKillAndAStop), GameFixtures.HitsARifleman()).Value!;

        Assert.Contains(after.Log, entry =>
            entry.Contains("fired Rifles with Squad Support", StringComparison.Ordinal));
    }

    [Fact]
    public void FiringASupportWeaponAloneUsesItsOwnImpact()
    {
        // The other half of the trade: fired as its own action, the support weapon's punch counts.
        var after = GameFixtures.Firefight()
            .Fire(Volley() with { WeaponName = "Squad Support" }, new ScriptedDice(GameFixtures.AKillAndAStop), GameFixtures.HitsARifleman());

        Assert.True(after.IsAllowed);
    }

    private static FireCommand Volley(params string[] support) =>
        GameFixtures.Volley() with { SupportWeapons = [.. support] };
}
