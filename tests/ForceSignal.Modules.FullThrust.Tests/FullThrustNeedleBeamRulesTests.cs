using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;

namespace ForceSignal.Modules.FullThrust.Tests;

public sealed class FullThrustNeedleBeamRulesTests
{
    [Fact]
    public void Resolve_TakesTheSystemOnASixAndDoesNoHullDamage()
    {
        var result = Dice(6).Resolve(Solution(range: 8));

        Assert.True(result.IsHit);
        Assert.Equal(6, result.ToHitNumber);
        Assert.Equal(0, result.Damage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Resolve_DoesNothingOnAnythingLessThanASix(int face)
    {
        var result = Dice(face).Resolve(Solution(range: 4));

        Assert.False(result.IsHit);
        Assert.Equal(0, result.Damage);
    }

    [Fact]
    public void Resolve_IgnoresScreensBecauseThereIsNoDamageToDegrade()
    {
        foreach (var screens in new[] { 0, 1, 2, 3 })
        {
            var result = Dice(6).Resolve(Solution(range: 4, screens));

            Assert.True(result.IsHit);
            Assert.Equal(0, result.ScreenReduction);
        }
    }

    [Fact]
    public void Validate_RejectsFireBeyondNineUnits()
    {
        var result = new FullThrustNeedleBeamRules().Validate(Solution(range: 10));

        Assert.False(result.IsValid);
        Assert.Contains("out of range", result.Errors[0]);
    }

    [Fact]
    public void Validate_HoldsANeedleToItsArc()
    {
        var rules = new FullThrustNeedleBeamRules();

        Assert.False(rules.Validate(new FiringSolution(Profile(), 6, 0, 0, FiringArc.AftPort)).IsValid);
        Assert.True(rules.Validate(new FiringSolution(Profile(), 6, 0, 0, FiringArc.Fore)).IsValid);
    }

    [Theory]
    // The enhanced needle draws blood from a 5 up: a 5 holes the hull without taking the system,
    // and a 6 does both.
    [InlineData(4, 0, false)]
    [InlineData(5, 1, false)]
    [InlineData(6, 1, true)]
    public void Resolve_UnderTheFleetBookLayerAlsoPutsAPointIntoTheHull(int face, int expectedDamage, bool expectedKill)
    {
        var result = Dice(face).Resolve(Solution(range: 4), RulesProfile.FleetBook);

        Assert.Equal(expectedDamage, result.Damage);
        Assert.Equal(expectedKill, result.IsHit);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void Resolve_UnderTheLightLayerTheSameRollDrawsNoBlood(int face)
    {
        // The same faces through the same resolver, and the only difference is the layer.
        var rules = Dice(face);

        Assert.Equal(0, rules.Resolve(Solution(range: 4), RulesProfile.LightCinematic).Damage);
    }

    [Fact]
    public void Validate_TakesItsReachFromTheLayerWhenTheMountDeclaresNone()
    {
        // A mount with no range of its own falls back to the layer's needle reach, so the same
        // record sheet snipes nine units under one layer and twelve under the other.
        var rules = new FullThrustNeedleBeamRules();
        var unranged = new WeaponAttackProfile("Needle Beam", 1, 0, [FiringArc.Fore], WeaponKind.NeedleBeam);
        var shot = new FiringSolution(unranged, 11, 0, 0);

        Assert.False(rules.Validate(shot, RulesProfile.LightCinematic).IsValid);
        Assert.True(rules.Validate(shot, RulesProfile.FleetBook).IsValid);
    }

    private static WeaponAttackProfile Profile() =>
        new("Needle Beam", 1, 9, [FiringArc.Fore], WeaponKind.NeedleBeam);

    private static FiringSolution Solution(int range, int screens = 0) =>
        new(Profile(), range, screens, AttackerWeaponDamage: 0);

    private static FullThrustNeedleBeamRules Dice(params int[] faces)
    {
        var index = 0;
        return new FullThrustNeedleBeamRules(() => faces[index++ % faces.Length]);
    }
}
