using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Ordnance;

namespace ForceSignal.Modules.FullThrust.Tests;

/// <summary>
/// Point defence, played against <see cref="TestRules.Invented"/>: the top face kills two and chains,
/// the two below it kill one, and the turrets reach five mu.
/// </summary>
public sealed class FullThrustPointDefenseRulesTests
{
    private static readonly RulesProfile Rules = TestRules.Invented;

    [Theory]
    // One system, one die, read straight off the profile's table.
    [InlineData(new[] { 1 }, 0)]
    [InlineData(new[] { 5 }, 0)]
    [InlineData(new[] { 6 }, 1)]
    [InlineData(new[] { 7 }, 1)]
    public void EachFaceShootsDownWhatTheProfileSaysItDoes(int[] faces, int expected) =>
        Assert.Equal(expected, Dice(faces).Resolve(systems: 1, incoming: 6, Rules).Kills);

    [Fact]
    public void TheChainingFaceScoresAndEarnsAnotherDie()
    {
        // The 8 kills two and earns a die, which rolls a 7 for one more: three kills off one system.
        var result = Dice([8, 7]).Resolve(systems: 1, incoming: 6, Rules);

        Assert.Equal([8, 7], result.Rolls);
        Assert.Equal(3, result.Kills);
    }

    [Fact]
    public void TheChainRunsWhileTheChainingFaceKeepsComingUp()
    {
        // Two chaining faces then a miss: 2 + 2 = 4 kills off a single system.
        var result = Dice([8, 8, 1]).Resolve(systems: 1, incoming: 6, Rules);

        Assert.Equal([8, 8, 1], result.Rolls);
        Assert.Equal(4, result.Kills);
    }

    [Fact]
    public void AProfileWithNoChainingFaceRollsOneDiePerSystemAndStops()
    {
        var noChain = Rules with { PointDefenseChainOnFace = 0 };

        var result = Dice([8, 8, 8]).Resolve(systems: 1, incoming: 6, noChain);

        Assert.Single(result.Rolls);
        Assert.Equal(2, result.Kills);
    }

    [Fact]
    public void OneDieIsRolledPerSystem()
    {
        var result = Dice([6, 7, 2]).Resolve(systems: 3, incoming: 6, Rules);

        Assert.Equal(3, result.Rolls.Count);
        Assert.Equal(2, result.Kills);
    }

    [Fact]
    public void KillsBeyondTheIncomingThreatAreWasted()
    {
        // Allocation is declared before the dice, so a system that overshoots gains nothing.
        var result = Dice([8, 8, 1]).Resolve(systems: 1, incoming: 1, Rules);

        Assert.Equal(1, result.Kills);
        Assert.Equal(3, result.Overkill);
    }

    [Fact]
    public void NoSystemsRollsNothing()
    {
        var result = new FullThrustPointDefenseRules().Resolve(systems: 0, incoming: 6, Rules);

        Assert.Empty(result.Rolls);
        Assert.Equal(0, result.Kills);
    }

    [Fact]
    public void TheReachComesFromTheProfile() =>
        Assert.Equal(Rules.PointDefenseRange, new FullThrustPointDefenseRules().RangeFor(Rules));

    private static FullThrustPointDefenseRules Dice(int[] faces)
    {
        var index = 0;
        return new FullThrustPointDefenseRules(_ => faces[index++ % faces.Length]);
    }
}

/// <summary>
/// Salvo missiles, played against <see cref="TestRules.Invented"/>: four missiles to a salvo and a
/// five-mu attack radius.
/// </summary>
public sealed class FullThrustSalvoMissileRulesTests
{
    private static readonly RulesProfile Rules = TestRules.Invented;

    [Fact]
    public void ArrivalsAreRolledThenDamagePerSurvivingMissile()
    {
        // Arrival die 4, no point defence, then damage dice 6, 3, 2, 5 for sixteen points.
        var result = Dice([4, 6, 3, 2, 5]).Resolve(targetPointDefenseSystems: 0, Rules);

        Assert.Equal(4, result.MissilesLaunched);
        Assert.Equal(4, result.ArrivalRoll);
        Assert.Equal(4, result.MissilesArriving);
        Assert.Equal(4, result.MissilesSurviving);
        Assert.Equal([6, 3, 2, 5], result.DamageRolls);
        Assert.Equal(16, result.Damage);
    }

    [Fact]
    public void NoMoreMissilesArriveThanTheSalvoHolds()
    {
        // The arrival die can roll higher than the salvo is big; the salvo is the ceiling.
        var result = Dice([8, 1, 1, 1, 1]).Resolve(targetPointDefenseSystems: 0, Rules);

        Assert.Equal(8, result.ArrivalRoll);
        Assert.Equal(4, result.MissilesArriving);
    }

    [Fact]
    public void PointDefenceThinsTheSalvoBeforeItStrikes()
    {
        // Arrival 5, held to the salvo's four; one turret rolls a 7 and stops one; three get through.
        var result = Dice([5, 7, 1, 1, 1]).Resolve(targetPointDefenseSystems: 1, Rules);

        Assert.Equal(4, result.MissilesArriving);
        Assert.Equal(1, result.PointDefense.Kills);
        Assert.Equal(3, result.MissilesSurviving);
        Assert.Equal(3, result.Damage);
    }

    [Fact]
    public void ASalvoCanBeStoppedDeadByPointDefence()
    {
        // Arrival 1, and a 7 on the single point defence die kills it.
        var result = Dice([1, 7]).Resolve(targetPointDefenseSystems: 1, Rules);

        Assert.Equal(0, result.MissilesSurviving);
        Assert.Empty(result.DamageRolls);
        Assert.Equal(0, result.Damage);
    }

    [Fact]
    public void EachMissilesFaceIsItsDamage()
    {
        // A single arriving missile rolling the top face does eight points - far past a beam die.
        var result = Dice([1, 8]).Resolve(targetPointDefenseSystems: 0, Rules);

        Assert.Equal(1, result.MissilesSurviving);
        Assert.Equal(8, result.Damage);
    }

    [Fact]
    public void TheSalvoSizeAndRadiusComeFromTheProfile()
    {
        var rules = new FullThrustSalvoMissileRules();

        Assert.Equal(Rules.MissilesPerSalvo, rules.SalvoSizeFor(Rules));
        Assert.Equal(Rules.SalvoAttackRadius, rules.AttackRadiusFor(Rules));
    }

    private static FullThrustSalvoMissileRules Dice(int[] faces)
    {
        var index = 0;
        var die = (int _) => faces[index++ % faces.Length];
        return new FullThrustSalvoMissileRules(die, new FullThrustPointDefenseRules(die));
    }

    [Fact]
    public void PointDefenceStopsChainingEvenWhenTheDiceNeverStopRolling()
    {
        // The chaining face earns another die, so the chain is unbounded by the rules. A die source
        // stuck on that face therefore never returned - and it spun inside the match service's lock,
        // which is process-wide, so one such request froze every match on the server. A source like
        // this is not hypothetical: scripted test dice cycle, and fallbacks are settable.
        var rules = new FullThrustPointDefenseRules(_ => 8);

        var result = rules.Resolve(systems: 3, incoming: 100, Rules);

        Assert.Equal(3 * FullThrustPointDefenseRules.MaxChainLength, result.Rolls.Count);
        Assert.All(result.Rolls, roll => Assert.Equal(8, roll));
    }

    [Fact]
    public void PointDefenceIgnoresAnAbsurdNumberOfSystems()
    {
        var rules = new FullThrustPointDefenseRules(_ => 1);

        var result = rules.Resolve(systems: int.MaxValue, incoming: 1, Rules);

        Assert.Equal(FullThrustPointDefenseRules.MaxSystems, result.Rolls.Count);
    }
}
