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
