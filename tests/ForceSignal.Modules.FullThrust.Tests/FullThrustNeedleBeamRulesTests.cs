using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;

namespace ForceSignal.Modules.FullThrust.Tests;

/// <summary>
/// Needle beams, played against <see cref="TestRules.Invented"/>: a kill on an 8, seven mu of reach,
/// and an enhanced variant that draws blood on a 7. All three are the profile's.
/// </summary>
public sealed class FullThrustNeedleBeamRulesTests
{
    private static readonly RulesProfile Rules = TestRules.Invented;

    [Fact]
    public void TheKillRollTakesTheSystemAndDoesNoHullDamage()
    {
        var result = Dice(8).Resolve(Solution(range: 6), Rules);

        Assert.True(result.IsHit);
        Assert.Equal(8, result.ToHitNumber);
        Assert.Equal(0, result.Damage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void AnythingUnderTheKillRollDoesNothing(int face)
    {
        var result = Dice(face).Resolve(Solution(range: 4), Rules);

        Assert.False(result.IsHit);
        Assert.Equal(0, result.Damage);
    }

    [Fact]
    public void ADifferentKillRollIsObeyed()
    {
        // The engine has no opinion about which face kills a system.
        var easy = Rules with { NeedleSystemKillRoll = 5 };

        Assert.True(Dice(5).Resolve(Solution(range: 4), easy).IsHit);
        Assert.False(Dice(4).Resolve(Solution(range: 4), easy).IsHit);
    }

    [Fact]
    public void ScreensAreIgnoredBecauseThereIsNoDamageToDegrade()
    {
        foreach (var screens in new[] { 0, 1, 2 })
        {
            var result = Dice(8).Resolve(Solution(range: 4, screens), Rules);

            Assert.True(result.IsHit);
            Assert.Equal(0, result.ScreenReduction);
        }
    }

    [Fact]
    public void FireBeyondTheMountsRangeIsRejected()
    {
        var result = new FullThrustNeedleBeamRules().Validate(Solution(range: 8), Rules);

        Assert.False(result.IsValid);
        Assert.Contains("out of range", result.Errors[0]);
    }

    [Fact]
    public void ANeedleIsHeldToItsArc()
    {
        var rules = new FullThrustNeedleBeamRules();

        Assert.False(rules.Validate(new FiringSolution(Mount(), 6, 0, 0, FiringArc.AftPort), Rules).IsValid);
        Assert.True(rules.Validate(new FiringSolution(Mount(), 6, 0, 0, FiringArc.Fore), Rules).IsValid);
    }

    [Theory]
    // An enhanced needle draws blood from its own roll up: a 7 holes the hull without taking the
    // system, and the kill roll does both.
    [InlineData(6, 0, false)]
    [InlineData(7, 1, false)]
    [InlineData(8, 1, true)]
    public void AnEnhancedNeedleAlsoPutsAPointIntoTheHull(int face, int expectedDamage, bool expectedKill)
    {
        var result = Dice(face).Resolve(Solution(range: 4), TestRules.WithEnhancedNeedles);

        Assert.Equal(expectedDamage, result.Damage);
        Assert.Equal(expectedKill, result.IsHit);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void APlainNeedleDrawsNoBloodOnTheSameFaces(int face)
    {
        // The same faces through the same resolver, and the only difference is the profile.
        var rules = Dice(face);

        Assert.Equal(0, rules.Resolve(Solution(range: 4), Rules).Damage);
    }

    [Fact]
    public void ReachComesFromTheProfileWhenTheMountDeclaresNone()
    {
        // A mount with no range of its own falls back to the profile's needle reach, so the same
        // record sheet snipes seven units under one profile and eleven under the other.
        var rules = new FullThrustNeedleBeamRules();
        var unranged = new WeaponAttackProfile("Needle Beam", 1, 0, [FiringArc.Fore], WeaponKind.NeedleBeam);
        var shot = new FiringSolution(unranged, 9, 0, 0);

        Assert.False(rules.Validate(shot, Rules).IsValid);
        Assert.True(rules.Validate(shot, TestRules.WithEnhancedNeedles).IsValid);
    }

    private static WeaponAttackProfile Mount() =>
        new("Needle Beam", 1, 7, [FiringArc.Fore], WeaponKind.NeedleBeam);

    private static FiringSolution Solution(int range, int screens = 0) =>
        new(Mount(), range, screens, AttackerWeaponDamage: 0);

    private static FullThrustNeedleBeamRules Dice(params int[] faces)
    {
        var index = 0;
        return new FullThrustNeedleBeamRules(() => faces[index++ % faces.Length]);
    }
}
