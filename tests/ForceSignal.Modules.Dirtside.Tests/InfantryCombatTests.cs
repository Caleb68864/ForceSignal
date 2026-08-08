using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// Infantry are stands: whole or gone. The two things worth guarding are that stands never pool
/// their chits, and that the specials switch off against infantry and on against a softskin - which
/// are the same field read in opposite directions.
/// </summary>
public sealed class InfantryCombatTests
{
    private static readonly int[] ThreeDrawsOfTwo = [2, 2, 2];
    private static readonly ChitValidity RedOnly = new(ChitColours.Red);
    private static readonly ChitValidity AnyColour = new(ChitColours.All);

    private static DamageChit Red(int value) => DamageChit.Numerical(ChitColour.Red, value);

    private static DamageChit Green(int value) => DamageChit.Numerical(ChitColour.Green, value);

    /// <summary>A stand drawing two in a firefight and three in an assault, killed by four points.</summary>
    private static InfantryStand Stand(string id) => new(id, FirefightChits: 2, AssaultChits: 3, KillThreshold: 4);

    [Theory]
    // Below leadership is ineffective; from leadership up to but not including double is half;
    // double or better is everybody.
    [InlineData(1, FireEffectiveness.Ineffective, 0)]
    [InlineData(2, FireEffectiveness.Partial, 3)]
    [InlineData(3, FireEffectiveness.Partial, 3)]
    [InlineData(4, FireEffectiveness.Full, 5)]
    [InlineData(8, FireEffectiveness.Full, 5)]
    public void TheEffectivenessCheckSplitsAtLeadershipAndAtDouble(
        int roll, FireEffectiveness expected, int expectedFiring)
    {
        var solution = InfantryCombat.SolveFireEffectiveness(QualityDie.D8, leadership: 2, eligibleStands: 5);

        var check = InfantryCombat.RollFireEffectiveness(solution, new FixedDie(roll));

        Assert.Equal(expected, check.Result);
        Assert.Equal(expectedFiring, check.StandsFiring);
    }

    [Fact]
    public void HalfTheStandsRoundsUpSoALoneStandStillShoots()
    {
        var solution = InfantryCombat.SolveFireEffectiveness(QualityDie.D8, leadership: 3, eligibleStands: 1);

        Assert.Equal(1, InfantryCombat.RollFireEffectiveness(solution, new FixedDie(3)).StandsFiring);
    }

    [Fact]
    public void TheBestLedUnitCanNeverBeIneffective()
    {
        // Not a special case in the code: ineffective means rolling below leadership, and nothing
        // rolls below one. It falls out of the comparison, which is the best kind of rule to have.
        var solution = InfantryCombat.SolveFireEffectiveness(QualityDie.D6, leadership: 1, eligibleStands: 4);

        var worst = InfantryCombat.RollFireEffectiveness(solution, new FixedDie(1));

        Assert.NotEqual(FireEffectiveness.Ineffective, worst.Result);
    }

    [Fact]
    public void BeingUnderFireCostsADieStepButNeverFallsOffTheLadder()
    {
        Assert.Equal(
            QualityDie.D8,
            InfantryCombat.SolveFireEffectiveness(QualityDie.D10, 2, 4, underFire: true).Die);

        // Closed, not open: there is no opponent here to hand the overflow to.
        Assert.Equal(
            QualityDie.D4,
            InfantryCombat.SolveFireEffectiveness(QualityDie.D4, 2, 4, underFire: true).Die);
    }

    [Fact]
    public void AnIneffectiveFirefightStillMarksTheTarget()
    {
        // The reason firing at something you cannot hurt is still worth doing.
        var solution = InfantryCombat.SolveFireEffectiveness(QualityDie.D8, leadership: 5, eligibleStands: 4);
        var check = InfantryCombat.RollFireEffectiveness(solution, new FixedDie(1));

        Assert.Equal(FireEffectiveness.Ineffective, check.Result);
        Assert.Equal(0, check.StandsFiring);
        Assert.True(FireEffectivenessCheck.TargetIsUnderFire);
    }

    [Fact]
    public void StandsNeverPoolTheirChits()
    {
        // The whole rule, in one assertion. Three stands drawing two chits each, every chit a Red 2,
        // is three separate draws of 4 against a kill total of 4 - three dead stands. Pooled, it
        // would be one total of 12 measured once, which is a completely different game.
        var stands = new[] { Stand("a"), Stand("b"), Stand("c") };
        var pot = new RecordingPot([.. Enumerable.Repeat(Red(2), 6)]);

        var result = InfantryCombat.ResolveFirefight(
            stands, RedOnly, SmallArmsTarget.Stand("enemy", 4), pot);

        Assert.Equal(3, result.Draws.Count);
        Assert.All(result.Draws, d => Assert.Equal(4, d.Tally.ValidTotal));
        Assert.Equal(3, result.Casualties);

        // Three draws of two, never one draw of six.
        Assert.Equal(ThreeDrawsOfTwo, pot.Requests);
    }

    [Fact]
    public void ANearMissOnEveryStandKillsNobody()
    {
        // The other half of the same rule. Three stands each falling one point short is three
        // failures, not one success - pooling would turn a scattering of near misses into a kill.
        var stands = new[] { Stand("a"), Stand("b"), Stand("c") };
        var pot = new RecordingPot([.. Enumerable.Repeat(Red(1), 6)]);

        var result = InfantryCombat.ResolveFirefight(
            stands, RedOnly, SmallArmsTarget.Stand("enemy", 4), pot);

        Assert.All(result.Draws, d => Assert.Equal(2, d.Tally.ValidTotal));
        Assert.Equal(0, result.Casualties);
    }

    [Theory]
    // Reaching the total is enough. The opposite convention to vehicle armour, where equalling it is
    // only a DAMAGED result - infantry have no damaged state to land on.
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public void ReachingTheKillTotalIsEnough(int total, bool expected) =>
        Assert.Equal(expected, InfantryCombat.IsDestroyed(total, killThreshold: 4));

    [Fact]
    public void InvalidChitsBurnTheirSlotHereToo()
    {
        // The same mechanism as vehicle damage, because it is literally the same draw code.
        var pot = new RecordingPot(Green(3), Red(1));

        var result = InfantryCombat.ResolveDraw(
            "a", 2, RedOnly, SmallArmsTarget.Stand("enemy", 4), pot);

        Assert.Equal(2, result.Tally.Draw.Count);
        Assert.Equal(1, result.Tally.WastedSlots);
        Assert.Equal(1, result.Tally.ValidTotal);
        Assert.False(result.Destroyed);
    }

    [Fact]
    public void SpecialsDoNothingAtAllAgainstInfantry()
    {
        // A stand cannot be immobilised or have its electronics knocked out. Letting the specials
        // through would kill stands the numbers never touched.
        var pot = new RecordingPot(DamageChit.Of(ChitSpecial.Boom), DamageChit.Of(ChitSpecial.Mobility));

        var result = InfantryCombat.ResolveDraw(
            "a", 2, AnyColour, SmallArmsTarget.Stand("enemy", 4), pot);

        Assert.False(result.Destroyed);
        Assert.False(result.CatastrophicKill);
        Assert.False(result.Immobilised);
        Assert.True(result.Tally.Draw.All(d => d.WastedTheSlot));
    }

    [Fact]
    public void SpecialsDoCountAgainstASoftskin()
    {
        // The one route by which rifle fire wrecks a vehicle: a truck can go up from small arms that
        // could never total enough points to matter.
        var pot = new RecordingPot(Red(0), DamageChit.Of(ChitSpecial.Boom));

        var result = InfantryCombat.ResolveDraw(
            "a", 2, AnyColour, SmallArmsTarget.Softskin("truck", 5), pot);

        Assert.False(result.Destroyed);
        Assert.True(result.CatastrophicKill);
        Assert.True(result.HadAnyEffect);
    }

    [Fact]
    public void TheSpecialsFlagIsTakenFromTheTargetAndNotFromTheCallersCard()
    {
        // Both directions overridden, so a card filled in from the wrong row cannot quietly produce
        // either failure. Neither of them would look like a bug from the outside.
        var specialsOn = new ChitValidity(ChitColours.All, SpecialsCount: true);
        var specialsOff = new ChitValidity(ChitColours.All, SpecialsCount: false);

        Assert.False(InfantryCombat.Against(specialsOn, SmallArmsTarget.Stand("s", 4)).SpecialsCount);
        Assert.True(InfantryCombat.Against(specialsOff, SmallArmsTarget.Softskin("t", 5)).SpecialsCount);
    }

    [Fact]
    public void ASoftskinKilledByTheNumbersIgnoresFurtherSpecials()
    {
        var pot = new RecordingPot(Red(3), Red(2), DamageChit.Of(ChitSpecial.Mobility));

        var result = InfantryCombat.ResolveDraw(
            "a", 3, AnyColour, SmallArmsTarget.Softskin("truck", 5), pot);

        Assert.True(result.Destroyed);
        Assert.False(result.Immobilised);
    }

    [Fact]
    public void TheAntiVehicleRocketKeepsItsSpecials()
    {
        // Numerically two chits will rarely out-total real armour. The specials are the whole point
        // of the weapon, and switching them off would leave it looking as though it worked.
        var card = InfantryCombat.AntiVehicleRocket(new ChitValidity(ChitColours.Red));

        Assert.True(card.SpecialsCount);
        Assert.Equal(ChitColours.Red, card.ValidColours);

        var outcome = DamageResolution.Resolve(
            2, card, armourValue: 9, new RecordingPot(Red(1), DamageChit.Of(ChitSpecial.Mobility)));

        Assert.Equal(NumericalDamage.None, outcome.Numerical);
        Assert.True(outcome.Immobilised);
    }

    [Theory]
    // Immobilised or blinded but intact: the passengers simply get out.
    [InlineData(NumericalDamage.None, 0)]
    [InlineData(NumericalDamage.Damaged, 6)]
    [InlineData(NumericalDamage.KnockedOut, 3)]
    public void RidersFareByHowBadlyTheirTransportWasHit(NumericalDamage damage, int expectedLostOn)
    {
        var outcome = Outcome(damage);

        var casualties = InfantryCombat.ResolveRiders(outcome, riderStands: 3, new FixedDie(6));

        Assert.Equal(expectedLostOn, casualties.LostOn);
        Assert.Equal(expectedLostOn == 0 ? 0 : 3, casualties.Lost);
    }

    [Fact]
    public void ADamagedTransportOnlyShakesOutTheUnluckiest()
    {
        var casualties = InfantryCombat.ResolveRiders(
            Outcome(NumericalDamage.Damaged), riderStands: 4, new SequencedDie(6, 5, 1, 6));

        Assert.Equal(2, casualties.Lost);
        Assert.False(casualties.AllKilled);
    }

    [Fact]
    public void AKnockedOutTransportIsFarWorseForTheSameRolls()
    {
        var casualties = InfantryCombat.ResolveRiders(
            Outcome(NumericalDamage.KnockedOut), riderStands: 4, new SequencedDie(6, 5, 1, 6));

        Assert.Equal(3, casualties.Lost);
    }

    [Fact]
    public void ACatastrophicKillTakesEveryoneWithoutARoll()
    {
        var die = new FixedDie(1);

        var casualties = InfantryCombat.ResolveRiders(
            Outcome(NumericalDamage.None, boom: true), riderStands: 4, die);

        Assert.Equal(4, casualties.Lost);
        Assert.True(casualties.AllKilled);
        Assert.Empty(casualties.Rolls);
        Assert.Equal(0, die.Rolls);
    }

    [Fact]
    public void ACrashKillsEveryoneWhateverTheTransportsDamageState()
    {
        var casualties = InfantryCombat.ResolveRiders(
            Outcome(NumericalDamage.None), riderStands: 2, new FixedDie(1), transportCrashed: true);

        Assert.Equal(2, casualties.Lost);
        Assert.True(casualties.AllKilled);
    }

    [Fact]
    public void AShotThatNeverHappenedCostsTheRidersNothing()
    {
        var outcome = DamageResolution.Resolve(
            1,
            new ChitValidity(ChitColours.All),
            armourValue: 1,
            new RecordingPot(DamageChit.Of(ChitSpecial.SystemsDownFirer)));

        var casualties = InfantryCombat.ResolveRiders(outcome, riderStands: 3, new FixedDie(6));

        Assert.True(outcome.ShotNeverHappened);
        Assert.Equal(0, casualties.Lost);
    }

    [Fact]
    public void ANonsenseCheckIsRefusedRatherThanGuessedAt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InfantryCombat.SolveFireEffectiveness(QualityDie.D8, -1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InfantryCombat.SolveFireEffectiveness(QualityDie.D8, 2, -1));
        Assert.Throws<ArgumentNullException>(() =>
            InfantryCombat.ResolveDraw("a", 1, null!, SmallArmsTarget.Stand("t", 4), new RecordingPot()));
    }

    private static DamageOutcome Outcome(NumericalDamage numerical, bool boom = false) => new(
        Array.Empty<DrawnChit>(),
        ValidTotal: 0,
        ArmourValue: 0,
        numerical,
        Immobilised: false,
        TargetSystemsDown: false,
        FirerSystemsDown: false,
        boom,
        ShotNeverHappened: false);

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

        public int Roll(QualityDie die) => _rolls.Count > 0
            ? _rolls.Dequeue()
            : throw new InvalidOperationException($"The script ran out while rolling a {die}.");
    }

    private sealed class RecordingPot(params DamageChit[] chits) : IChitPot
    {
        private readonly Queue<DamageChit> _chits = new(chits);
        private readonly List<int> _requests = [];

        public IReadOnlyList<int> Requests => _requests;

        public IReadOnlyList<DamageChit> Draw(int count)
        {
            _requests.Add(count);
            return [.. Enumerable.Range(0, count).Select(_ => _chits.Count > 0
                ? _chits.Dequeue()
                : throw new InvalidOperationException("The scripted pot ran dry."))];
        }
    }
}
