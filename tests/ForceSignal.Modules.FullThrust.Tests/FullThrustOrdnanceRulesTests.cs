using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Ordnance;

namespace ForceSignal.Modules.FullThrust.Tests;

public sealed class FullThrustPointDefenseRulesTests
{
    [Theory]
    // One system, one die: nothing below 4, one on a 4 or 5.
    [InlineData(1, new[] { 1 }, 0)]
    [InlineData(1, new[] { 3 }, 0)]
    [InlineData(1, new[] { 4 }, 1)]
    [InlineData(1, new[] { 5 }, 1)]
    public void Resolve_ScoresOneKillOnAFourOrFive(int systems, int[] faces, int expected)
    {
        Assert.Equal(expected, Dice(faces).Resolve(systems, incoming: 6).Kills);
    }

    [Fact]
    public void Resolve_KillsTwoOnASixAndRollsAgain()
    {
        // Six kills two and earns a die, which rolls a 5 for one more: three kills from one system.
        var result = Dice(6, 5).Resolve(systems: 1, incoming: 6);

        Assert.Equal([6, 5], result.Rolls);
        Assert.Equal(3, result.Kills);
    }

    [Fact]
    public void Resolve_ChainsWhileTheSixesLast()
    {
        // Two sixes then a miss: 2 + 2 = 4 kills off a single system.
        var result = Dice(6, 6, 1).Resolve(systems: 1, incoming: 6);

        Assert.Equal([6, 6, 1], result.Rolls);
        Assert.Equal(4, result.Kills);
    }

    [Fact]
    public void Resolve_RollsOneDiePerSystem()
    {
        var result = Dice(4, 5, 2).Resolve(systems: 3, incoming: 6);

        Assert.Equal(3, result.Rolls.Count);
        Assert.Equal(2, result.Kills);
    }

    [Fact]
    public void Resolve_WastesKillsBeyondTheIncomingThreat()
    {
        // Allocation is declared before the dice, so a system that overshoots gains nothing.
        var result = Dice(6, 6, 1).Resolve(systems: 1, incoming: 1);

        Assert.Equal(1, result.Kills);
        Assert.Equal(3, result.Overkill);
    }

    [Fact]
    public void Resolve_WithNoSystemsRollsNothing()
    {
        var result = new FullThrustPointDefenseRules().Resolve(systems: 0, incoming: 6);

        Assert.Empty(result.Rolls);
        Assert.Equal(0, result.Kills);
    }

    [Fact]
    public void Range_IsSixUnits()
    {
        Assert.Equal(6, new FullThrustPointDefenseRules().Range);
    }

    private static FullThrustPointDefenseRules Dice(params int[] faces)
    {
        var index = 0;
        return new FullThrustPointDefenseRules(() => faces[index++ % faces.Length]);
    }
}

public sealed class FullThrustSalvoMissileRulesTests
{
    [Fact]
    public void Resolve_RollsArrivalsThenDamagePerSurvivingMissile()
    {
        // Arrival die 4, no point defence, then damage dice 6, 3, 2, 5 for sixteen points.
        var rules = Dice([4, 6, 3, 2, 5]);

        var result = rules.Resolve(targetPointDefenseSystems: 0);

        Assert.Equal(6, result.MissilesLaunched);
        Assert.Equal(4, result.ArrivalRoll);
        Assert.Equal(4, result.MissilesArriving);
        Assert.Equal(4, result.MissilesSurviving);
        Assert.Equal([6, 3, 2, 5], result.DamageRolls);
        Assert.Equal(16, result.Damage);
    }

    [Fact]
    public void Resolve_LetsPointDefenceThinTheSalvoBeforeItStrikes()
    {
        // Arrival 5; one point defence system rolls a 5 and stops one; four survivors roll 1s.
        var result = Dice([5, 5, 1, 1, 1, 1]).Resolve(targetPointDefenseSystems: 1);

        Assert.Equal(5, result.MissilesArriving);
        Assert.Equal(1, result.PointDefense.Kills);
        Assert.Equal(4, result.MissilesSurviving);
        Assert.Equal(4, result.Damage);
    }

    [Fact]
    public void Resolve_CanBeStoppedDeadByPointDefence()
    {
        // Arrival 1, and a 4 on the single point defence die kills it.
        var result = Dice([1, 4]).Resolve(targetPointDefenseSystems: 1);

        Assert.Equal(0, result.MissilesSurviving);
        Assert.Empty(result.DamageRolls);
        Assert.Equal(0, result.Damage);
    }

    [Fact]
    public void Resolve_ScoresSixPointsForASixBecauseTheFaceIsTheDamage()
    {
        // A single arriving missile rolling a six does six points - far past a beam die's two.
        var result = Dice([1, 6]).Resolve(targetPointDefenseSystems: 0);

        Assert.Equal(1, result.MissilesSurviving);
        Assert.Equal(6, result.Damage);
    }

    [Fact]
    public void SalvoSizeAndRadius_MatchTheRules()
    {
        var rules = new FullThrustSalvoMissileRules();

        Assert.Equal(6, rules.SalvoSize);
        Assert.Equal(6, rules.AttackRadius);
    }

    private static FullThrustSalvoMissileRules Dice(int[] faces)
    {
        var index = 0;
        var die = () => faces[index++ % faces.Length];
        return new FullThrustSalvoMissileRules(die, new FullThrustPointDefenseRules(die));
    }

    [Fact]
    public void PointDefence_StopsChainingEvenWhenTheDiceNeverStopRollingSixes()
    {
        // A six earns another die, so the chain is unbounded by the rules. A die source stuck on
        // six therefore never returned - and it spun inside the match service's lock, which is
        // process-wide, so one such request froze every match on the server. A source like this is
        // not hypothetical: scripted test dice cycle, and fallbacks are settable.
        var rules = new FullThrustPointDefenseRules(() => 6);

        var result = rules.Resolve(systems: 3, incoming: 100);

        Assert.Equal(3 * FullThrustPointDefenseRules.MaxChainLength, result.Rolls.Count);
        Assert.All(result.Rolls, roll => Assert.Equal(6, roll));
    }

    [Fact]
    public void PointDefence_IgnoresAnAbsurdNumberOfSystems()
    {
        var rules = new FullThrustPointDefenseRules(() => 1);

        var result = rules.Resolve(systems: int.MaxValue, incoming: 1);

        Assert.Equal(FullThrustPointDefenseRules.MaxSystems, result.Rolls.Count);
    }
}
