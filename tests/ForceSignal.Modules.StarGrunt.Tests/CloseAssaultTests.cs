using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.StarGrunt.Assault;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Covers close assault: the nerve to charge, the nerve to stand, and the business at arm's length.
/// </summary>
/// <remarks>
/// Gap 9, and the last of the audit. The rulebook's own worked example runs here as a fixture,
/// because the open shift crossing over onto two opponents at once is unusual enough to be worth
/// pinning to a case somebody has already worked out by hand.
/// </remarks>
public sealed class CloseAssaultTests
{
    [Theory]
    [InlineData(ConfidenceLevel.Confident)]
    [InlineData(ConfidenceLevel.Steady)]
    [InlineData(ConfidenceLevel.Shaken)]
    public void AUnitWithAnyNerveLeftCanBeOrderedToCharge(ConfidenceLevel confidence) =>
        Assert.True(CloseAssault.CanCharge(confidence));

    [Theory]
    [InlineData(ConfidenceLevel.Broken)]
    [InlineData(ConfidenceLevel.Routed)]
    public void AUnitThatHasLostItsNerveWillNotCharge(ConfidenceLevel confidence) =>
        Assert.False(CloseAssault.CanCharge(confidence));

    [Fact]
    public void PowerArmourCountsDoubleInTheOdds()
    {
        Assert.Equal(8, CloseAssault.Strength(figures: 8));
        Assert.Equal(10, CloseAssault.Strength(figures: 8, powerArmoured: 2));
    }

    [Theory]
    // Odds round down, and an even fight still asks something of the defenders.
    [InlineData(8, 8, 1)]
    [InlineData(9, 8, 1)]
    [InlineData(16, 8, 2)]
    [InlineData(25, 8, 3)]
    public void StandingFirmGetsHarderAsTheOddsWorsen(int attacker, int defender, int expected) =>
        Assert.Equal(expected, CloseAssault.StandThreat(attacker, defender));

    [Fact]
    public void TerrorDoublesWhateverTheOddsProduced()
    {
        Assert.Equal(4, CloseAssault.StandThreat(16, 8, terror: true));
        Assert.Equal(2, CloseAssault.StandThreat(8, 8, terror: true));
    }

    [Fact]
    public void TheHigherRollDownsTheOtherMan()
    {
        var exchange = CloseAssault.Fight(
            new Combatant(QualityDie.D8),
            new Combatant(QualityDie.D8),
            new ScriptedDice(6, 3));

        Assert.True(exchange.DefenderDown);
        Assert.False(exchange.AttackerDown);
    }

    [Fact]
    public void ATieSettlesNothingAndBothFightOn()
    {
        var exchange = CloseAssault.Fight(
            new Combatant(QualityDie.D8),
            new Combatant(QualityDie.D8),
            new ScriptedDice(5, 5));

        Assert.True(exchange.Tied);
        Assert.False(exchange.AttackerDown);
        Assert.False(exchange.DefenderDown);
    }

    [Fact]
    public void TheRulebooksShotgunAgainstPowerArmourComesOutAsWritten()
    {
        // A Regular whose weapon is worth two shifts (D8 to D12) against a Veteran in power armour
        // (D10). The Regular rolls 10; the trooper rolls 3, doubled to 6. The bigger die wins.
        var exchange = CloseAssault.Fight(
            new Combatant(QualityDie.D8, WeaponShift: 2),
            new Combatant(QualityDie.D10, PowerArmour: true),
            new ScriptedDice(10, 3));

        Assert.Equal(10, exchange.AttackerScore);
        Assert.Equal(6, exchange.DefenderScore);
        Assert.True(exchange.DefenderDown);
    }

    [Fact]
    public void AShiftPastTheTopOfTheLadderComesOffTheOtherMansDie()
    {
        // The open shift: a flamer already at the top of the ladder cannot climb further, so the
        // leftover steps are taken off his opponent instead. Asserted on the dice each man ends up
        // throwing rather than on the rolls, because that is where the rule actually lands.
        var exchange = CloseAssault.Fight(
            new Combatant(QualityDie.D12, WeaponShift: 2),
            new Combatant(QualityDie.D8),
            new ScriptedDice(12, 4));

        Assert.Equal(QualityDie.D12, exchange.AttackerDie);
        Assert.Equal(QualityDie.D4, exchange.DefenderDie);
    }

    [Fact]
    public void CoverHelpsADefenderOnlyWhileHeStillHasIt()
    {
        // In the first round a defender in cover throws a bigger die; from the second round on,
        // once the attackers are in among them, he throws his own.
        var sheltered = CloseAssault.Fight(
            new Combatant(QualityDie.D8),
            new Combatant(QualityDie.D8, InCoverThisRound: true),
            new ScriptedDice(6, 8));
        var exposed = CloseAssault.Fight(
            new Combatant(QualityDie.D8),
            new Combatant(QualityDie.D8),
            new ScriptedDice(6, 8));

        Assert.Equal(QualityDie.D10, sheltered.DefenderDie);
        Assert.Equal(QualityDie.D8, exposed.DefenderDie);
    }

    [Theory]
    // The bands are the player's; what is pinned here is that they read low-to-high, worst first.
    [InlineData(1, DownedFate.Dead)]
    [InlineData(2, DownedFate.Dead)]
    [InlineData(3, DownedFate.Wounded)]
    [InlineData(4, DownedFate.Wounded)]
    [InlineData(5, DownedFate.Stunned)]
    [InlineData(6, DownedFate.Stunned)]
    public void ADownedFigureIsReadAgainstTheBandsThePlayerSupplies(int roll, DownedFate expected) =>
        Assert.Equal(expected, CloseAssault.Fate(roll, deadUpTo: 2, woundedUpTo: 4));

    [Fact]
    public void BandsOfADifferentShapeAreReadJustTheSame()
    {
        // Nothing here assumes a D6 or the usual thirds: a kinder table is read as written.
        Assert.Equal(DownedFate.Dead, CloseAssault.Fate(1, deadUpTo: 1, woundedUpTo: 3));
        Assert.Equal(DownedFate.Wounded, CloseAssault.Fate(3, deadUpTo: 1, woundedUpTo: 3));
        Assert.Equal(DownedFate.Stunned, CloseAssault.Fate(4, deadUpTo: 1, woundedUpTo: 3));
    }

    [Fact]
    public void TheSideThatCameOffWorstTestsItsNerveFirst()
    {
        var (attackerFirst, attackerThreat, defenderThreat) = CloseAssault.BetweenRounds(3, 1);

        Assert.True(attackerFirst);
        Assert.Equal(3, attackerThreat);
        Assert.Equal(1, defenderThreat);
    }

    [Fact]
    public void EvenLossesLeaveTheDefenderTestingFirst()
    {
        var (attackerFirst, _, _) = CloseAssault.BetweenRounds(2, 2);

        Assert.False(attackerFirst);
    }
}
