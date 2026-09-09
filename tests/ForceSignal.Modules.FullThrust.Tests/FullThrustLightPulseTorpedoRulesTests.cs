using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;

namespace ForceSignal.Modules.FullThrust.Tests;

/// <summary>
/// Pulse torpedoes, played against <see cref="TestRules.Invented"/>: eight-mu bands, a 3 needed in
/// the closest one, and forty mu of reach. None of those are the engine's.
/// </summary>
public sealed class FullThrustLightPulseTorpedoRulesTests
{
    private static readonly RulesProfile Rules = TestRules.Invented;

    [Theory]
    // The profile's best number in the closest band, then a point worse per further band.
    [InlineData(1, 3)]
    [InlineData(8, 3)]
    [InlineData(9, 4)]
    [InlineData(16, 4)]
    [InlineData(17, 5)]
    [InlineData(24, 5)]
    [InlineData(25, 6)]
    [InlineData(32, 6)]
    [InlineData(33, 7)]
    [InlineData(40, 7)]
    public void TheNumberNeededWorsensEveryBand(int range, int expected) =>
        Assert.Equal(expected, FullThrustLightPulseTorpedoRules.ToHitNumber(range, Rules));

    [Fact]
    public void TheNumberNeededNeverWorsensPastWhatTheDieCanRoll()
    {
        // Far enough out, the ladder would run off the top of the die. It stops at the top face.
        Assert.Equal(Rules.DieFaces, FullThrustLightPulseTorpedoRules.ToHitNumber(500, Rules));
    }

    [Fact]
    public void ADifferentBandWidthMovesTheWholeLadder()
    {
        // Nothing about six, or eight, is written into the engine.
        var wide = Rules with { TorpedoBandWidth = 20 };

        Assert.Equal(3, FullThrustLightPulseTorpedoRules.ToHitNumber(20, wide));
        Assert.Equal(4, FullThrustLightPulseTorpedoRules.ToHitNumber(21, wide));
    }

    [Fact]
    public void AHitTakesTheDamageDieFaceAsDamage()
    {
        // Needs a 4 at 15mu: rolls a 4 to hit, then a 5 for damage.
        var result = Dice(4, 5).Resolve(Solution(range: 15), Rules);

        Assert.True(result.IsHit);
        Assert.Equal(4, result.ToHitNumber);
        Assert.Equal([4, 5], result.DiceRolls);
        Assert.Equal(5, result.Damage);
    }

    [Fact]
    public void AMissRollsNoDamageDie()
    {
        // Needs a 5 at 20mu and rolls a 4.
        var result = Dice(4, 6).Resolve(Solution(range: 20), Rules);

        Assert.False(result.IsHit);
        Assert.Equal(5, result.ToHitNumber);
        Assert.Equal([4], result.DiceRolls);
        Assert.Equal(0, result.Damage);
    }

    [Fact]
    public void ScreensAreIgnoredEntirely()
    {
        // The same roll against every screen level does the same damage: a torpedo punches through.
        foreach (var screens in new[] { 0, 1, 2 })
        {
            var result = Dice(6, 6).Resolve(Solution(range: 10, screens), Rules);

            Assert.Equal(6, result.Damage);
            Assert.Equal(0, result.ScreenReduction);
        }
    }

    [Fact]
    public void NoDiceAreLostToRangeOrWeaponDamage()
    {
        // A launcher fires one shot: range worsens the number needed rather than removing dice, and
        // the attacker's weapon damage does not thin a torpedo salvo either.
        // A 7 is what this profile asks for at 36mu, then a 3 for damage.
        var result = Dice(7, 3).Resolve(
            new FiringSolution(
                Tube(),
                Range: 36,
                TargetScreenRating: 0,
                AttackerWeaponDamage: 3),
            Rules);

        Assert.Equal(1, result.RawDice);
        Assert.Equal(0, result.RangePenalty);
        Assert.Equal(0, result.SystemPenalty);
        Assert.Equal(3, result.Damage);
    }

    [Fact]
    public void FireBeyondTheProfilesReachIsRejected()
    {
        var result = new FullThrustLightPulseTorpedoRules().Validate(
            new FiringSolution(
                new WeaponAttackProfile("Torpedo Tube", 1, 60, [FiringArc.Fore], WeaponKind.PulseTorpedo),
                Range: 41,
                TargetScreenRating: 0,
                AttackerWeaponDamage: 0),
            Rules);

        Assert.False(result.IsValid);
        Assert.Contains("out of range", result.Errors[0]);
    }

    [Fact]
    public void ATorpedoIsHeldToItsArcsAndTheAftBlindSpot()
    {
        var rules = new FullThrustLightPulseTorpedoRules();
        var tube = Tube();

        Assert.False(rules.Validate(new FiringSolution(tube, 10, 0, 0, FiringArc.AftPort), Rules).IsValid);
        Assert.False(rules.Validate(new FiringSolution(tube, 10, 0, 0, FiringArc.Aft), Rules).IsValid);
        Assert.True(rules.Validate(new FiringSolution(tube, 10, 0, 0, FiringArc.Fore), Rules).IsValid);
    }

    private static WeaponAttackProfile Tube() =>
        new("Torpedo Tube", 1, 40, [FiringArc.Fore], WeaponKind.PulseTorpedo);

    private static FiringSolution Solution(int range, int screens = 0) =>
        new(Tube(), range, screens, AttackerWeaponDamage: 0);

    /// <summary>Torpedo rules fed a fixed sequence of die faces, cycling if more are needed.</summary>
    private static FullThrustLightPulseTorpedoRules Dice(params int[] faces)
    {
        var index = 0;
        return new FullThrustLightPulseTorpedoRules(_ => faces[index++ % faces.Length]);
    }
}
