using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Damage;

namespace ForceSignal.Modules.FullThrust.Tests;

/// <summary>
/// Damage control, played against <see cref="TestRules.Invented"/>: one party repairs on a 7, a
/// second brings that to 6, and two is as many as can crowd a job.
/// </summary>
public sealed class FullThrustDamageControlRulesTests
{
    private static readonly RulesProfile Rules = TestRules.Invented;

    [Theory]
    // Each extra party on the same job lowers the number by one, stopping at the profile's floor.
    [InlineData(1, 7)]
    [InlineData(2, 6)]
    // More parties than the job can hold are no better than filling it.
    [InlineData(3, 6)]
    // A job with nobody on it is still read as one party rather than dividing by nothing.
    [InlineData(0, 7)]
    public void MoreHandsMakeTheNumberEasierUntilTheFloor(int parties, int expected) =>
        Assert.Equal(expected, new FullThrustDamageControlRules().NeededFor(parties, Rules));

    [Fact]
    public void ADeeperCrowdLimitKeepsImprovingTheNumber()
    {
        // Nothing about three parties, or two, is written into the engine.
        var roomy = Rules with { MaxPartiesPerJob = 4, RepairBestRoll = 3 };

        Assert.Equal(7, new FullThrustDamageControlRules().NeededFor(1, roomy));
        Assert.Equal(4, new FullThrustDamageControlRules().NeededFor(4, roomy));
    }

    [Fact]
    public void ASystemComesBackWhenTheRollMeetsTheNumber()
    {
        var attempt = Dice(6).Resolve(new RepairJob(ShipSystemKind.FireControl, null, 2), Rules);

        Assert.Equal(6, attempt.Needed);
        Assert.Equal(6, attempt.Roll);
        Assert.True(attempt.IsRepaired);
    }

    [Fact]
    public void ASystemStaysDownWhenTheRollFallsShort()
    {
        var attempt = Dice(6).Resolve(new RepairJob(ShipSystemKind.FireControl, null, 1), Rules);

        Assert.Equal(7, attempt.Needed);
        Assert.False(attempt.IsRepaired);
    }

    [Fact]
    public void OneRollIsMadeForTheWholeJobHoweverManyPartiesAreOnIt()
    {
        // Two parties do not roll two dice: they make one roll on a better number.
        var rolls = 0;
        var rules = new FullThrustDamageControlRules(_ => { rolls++; return 6; });

        var attempt = rules.Resolve(new RepairJob(ShipSystemKind.Drive, null, 2), Rules);

        Assert.Equal(1, rolls);
        Assert.True(attempt.IsRepaired);
    }

    private static FullThrustDamageControlRules Dice(params int[] faces)
    {
        var index = 0;
        return new FullThrustDamageControlRules(_ => faces[index++ % faces.Length]);
    }
}
