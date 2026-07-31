using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;

namespace ForceSignal.Modules.FullThrust.Tests;

public sealed class FullThrustLightFiringRulesTests
{
    private readonly FullThrustLightFiringRules _rules = new();

    [Fact]
    public void Resolve_ReducesDamageByRangeScreensAndSystemDamage()
    {
        var result = _rules.Resolve(new FiringSolution(
            new WeaponAttackProfile("Class-4 Beam", 4, 36, FiringArc.Fore),
            Range: 13,
            TargetScreenRating: 1,
            AttackerWeaponDamage: 1));

        Assert.Equal(4, result.RawDice);
        Assert.Equal(2, result.RangePenalty);
        Assert.Equal(1, result.ScreenReduction);
        Assert.Equal(1, result.SystemPenalty);
        Assert.Equal(0, result.Damage);
    }

    [Fact]
    public void Resolve_AppliesCloseRangeDamageAfterScreens()
    {
        var result = _rules.Resolve(new FiringSolution(
            new WeaponAttackProfile("Class-3 Beam", 3, 24, FiringArc.Fore),
            Range: 6,
            TargetScreenRating: 1,
            AttackerWeaponDamage: 0));

        Assert.Equal(2, result.Damage);
    }

    [Fact]
    public void Validate_RejectsOutOfRangeFire()
    {
        var result = _rules.Validate(new FiringSolution(
            new WeaponAttackProfile("Needle Beam", 1, 12, FiringArc.Fore),
            Range: 13,
            TargetScreenRating: 0,
            AttackerWeaponDamage: 0));

        Assert.False(result.IsValid);
        Assert.Contains("out of range", result.Errors[0]);
    }
}
