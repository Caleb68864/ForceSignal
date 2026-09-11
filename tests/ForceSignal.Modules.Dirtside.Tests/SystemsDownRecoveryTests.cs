using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// The only self-repair in the game. Stateless on purpose: an attempt is a function of two
/// activation numbers, the design, and one die, which is what makes it safe to retry for as long as
/// the game lasts and safe to reuse for a vehicle that goes down more than once.
/// </summary>
/// <remarks>
/// The die and the number it has to reach used to be written into the resolver. They are the
/// players', so they come off <see cref="TestDieTables.Invented"/> here - a D8 reaching a 7, or a 4
/// with backup systems - and are deliberately not the pair they replaced.
/// </remarks>
public sealed class SystemsDownRecoveryTests
{
    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 4)]
    public void BackupSystemsTurnALongShotIntoAnEvenChance(bool hasBackup, int expected) =>
        Assert.Equal(expected, SystemsDownRecovery.Required(TestDieTables.Invented, hasBackup));

    [Fact]
    public void AProfileWithNoRepairRollRefusesTheAttemptAndBurnsNoDie()
    {
        // No fallback and no roll. A crew whose players have not entered this row are told so, and
        // the die is untouched, so a replay has nothing extra to reproduce.
        var die = new FixedDie(8);

        var attempt = SystemsDownRecovery.Attempt(
            TestDieTables.WithNoRepairRoll, damagedOnActivation: 1, currentActivation: 2, die);

        Assert.False(attempt.WasAttempted);
        Assert.False(attempt.Cleared);
        Assert.Equal(0, die.Rolls);
        Assert.Contains("rules profile", attempt.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void HalfAProfileIsStillNoProfileForThisRoll()
    {
        // A profile carrying the die but not the number it has to reach is a half-entered row, and a
        // half-entered row is a mistake rather than a choice.
        var dieOnly = TestDieTables.WithNoRepairRoll with { SystemsDownRecoveryDie = QualityDie.D8 };

        Assert.Null(SystemsDownRecovery.Required(dieOnly, hasBackupSystems: false));
        Assert.False(SystemsDownRecovery.Attempt(dieOnly, 1, 2, new FixedDie(8)).WasAttempted);
    }

    [Theory]
    // Reach it, not beat it: this is one of the few rolls in the family that is not the
    // exceed-don't-match comparison.
    [InlineData(6, false, false)]
    [InlineData(7, false, true)]
    [InlineData(3, true, false)]
    [InlineData(4, true, true)]
    [InlineData(7, true, true)]
    public void TheMarkerComesOffOnReachingTheNumber(int roll, bool hasBackup, bool expected)
    {
        var attempt = SystemsDownRecovery.Attempt(
            TestDieTables.Invented,
            damagedOnActivation: 1,
            currentActivation: 2,
            new FixedDie(roll),
            hasBackupSystems: hasBackup);

        Assert.True(attempt.WasAttempted);
        Assert.Equal(roll, attempt.Roll);
        Assert.Equal(expected, attempt.Cleared);
    }

    [Fact]
    public void RepairsCannotStartOnTheActivationTheDamageHappened()
    {
        // Strictly after. Allowing it on the same activation would make Systems Down something a
        // lucky vehicle shrugs off before it has cost it anything at all.
        Assert.False(SystemsDownRecovery.CanAttempt(damagedOnActivation: 3, currentActivation: 3));
        Assert.True(SystemsDownRecovery.CanAttempt(damagedOnActivation: 3, currentActivation: 4));
    }

    [Fact]
    public void ARefusedAttemptDoesNotBurnADie()
    {
        // Refused rather than rolled and failed, because a rolled failure would consume a die that a
        // replay would then have to reproduce for a roll that never happened.
        var die = new FixedDie(7);

        var attempt = SystemsDownRecovery.Attempt(
            TestDieTables.Invented, damagedOnActivation: 3, currentActivation: 3, die);

        Assert.False(attempt.WasAttempted);
        Assert.False(attempt.Cleared);
        Assert.Equal(0, attempt.Roll);
        Assert.Equal(0, die.Rolls);
        Assert.NotNull(attempt.Reason);
    }

    [Fact]
    public void ACrewThatHasBailedOutRepairsNothing()
    {
        var attempt = SystemsDownRecovery.Attempt(
            TestDieTables.Invented,
            damagedOnActivation: 1,
            currentActivation: 9,
            new FixedDie(7),
            crewAboard: false);

        Assert.False(attempt.WasAttempted);
        Assert.False(attempt.Cleared);
        Assert.Contains("bailed out", attempt.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailingIsRetryableForAsLongAsTheGameLasts()
    {
        // Six failures then a success, with nothing carried between them but the activation number.
        var die = new SequencedDie(1, 2, 3, 4, 5, 1, 7);
        var cleared = false;

        for (var activation = 2; activation <= 8 && !cleared; activation++)
        {
            cleared = SystemsDownRecovery.Attempt(TestDieTables.Invented, 1, activation, die).Cleared;
        }

        Assert.True(cleared);
        Assert.Equal(7, die.Rolls);
    }

    [Fact]
    public void TheSameVehicleMayGoDownAgainAndRecoverAgain()
    {
        // Nothing is remembered between failures, so there is no "already repaired" flag to reset.
        var die = new FixedDie(7);

        Assert.True(SystemsDownRecovery.Attempt(TestDieTables.Invented, 1, 2, die).Cleared);
        Assert.True(SystemsDownRecovery.Attempt(TestDieTables.Invented, 5, 6, die).Cleared);
    }

    private sealed class FixedDie(int roll) : IQualityDiceRoller
    {
        public int Rolls { get; private set; }

        public int Roll(QualityDie die)
        {
            Rolls++;
            return roll;
        }
    }

    private sealed class SequencedDie(params int[] rolls) : IQualityDiceRoller
    {
        private readonly Queue<int> _rolls = new(rolls);

        public int Rolls { get; private set; }

        public int Roll(QualityDie die)
        {
            Rolls++;
            return _rolls.Count > 0
                ? _rolls.Dequeue()
                : throw new InvalidOperationException($"The script ran out while rolling a {die}.");
        }
    }
}
