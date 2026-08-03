using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Damage;

namespace ForceSignal.Modules.FullThrust.Tests;

public sealed class FullThrustDamageControlRulesTests
{
    [Theory]
    // One party repairs on a 6; each extra on the same job lowers the number by one, stopping at 4+.
    [InlineData(1, 6)]
    [InlineData(2, 5)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(0, 6)]
    public void NeededFor_ImprovesWithEachPartyOnTheJob(int parties, int expected)
    {
        Assert.Equal(expected, new FullThrustDamageControlRules().NeededFor(parties));
    }

    [Fact]
    public void Resolve_BringsASystemBackWhenTheRollMeetsTheNumber()
    {
        var job = new RepairJob(ShipSystemKind.FireControl, null, 2);

        var attempt = Dice(5).Resolve(job);

        Assert.Equal(5, attempt.Needed);
        Assert.Equal(5, attempt.Roll);
        Assert.True(attempt.IsRepaired);
    }

    [Fact]
    public void Resolve_LeavesTheSystemDownWhenTheRollFallsShort()
    {
        var attempt = Dice(4).Resolve(new RepairJob(ShipSystemKind.FireControl, null, 1));

        Assert.Equal(6, attempt.Needed);
        Assert.False(attempt.IsRepaired);
    }

    [Fact]
    public void Resolve_RollsOnceForTheWholeJobHoweverManyPartiesAreOnIt()
    {
        // Three parties do not roll three dice: they make one roll on a better number.
        var rolls = 0;
        var rules = new FullThrustDamageControlRules(() => { rolls++; return 4; });

        var attempt = rules.Resolve(new RepairJob(ShipSystemKind.Drive, null, 3));

        Assert.Equal(1, rolls);
        Assert.True(attempt.IsRepaired);
    }

    private static FullThrustDamageControlRules Dice(params int[] faces)
    {
        var index = 0;
        return new FullThrustDamageControlRules(() => faces[index++ % faces.Length]);
    }
}
