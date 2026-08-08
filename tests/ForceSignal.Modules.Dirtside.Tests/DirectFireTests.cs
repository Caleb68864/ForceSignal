using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// The whole shot, declaration to chits. Most of what is worth testing here is the seams: that the
/// declaration really is binding, that a mount's barrels share one target roll but not one chit
/// draw, and that the effective range band reaches stage two as well as stage one.
/// </summary>
public sealed class DirectFireTests
{
    private static readonly int[] TwoDrawsOfThree = [3, 3];
    private static readonly int[] TwoDrawsOfOne = [1, 1];
    private static readonly int[] OneDrawOfOne = [1];

    /// <summary>Any colour, face value, specials on: the plainest card there is.</summary>
    private static readonly WeaponValidityCard AnyColour =
        WeaponValidityCard.Flat(new ChitValidity(ChitColours.All));

    private static DamageChit Red(int value) => DamageChit.Numerical(ChitColour.Red, value);

    private static DamageChit Green(int value) => DamageChit.Numerical(ChitColour.Green, value);

    /// <summary>Enhanced sight, signature 3, no posture: firer D8 at medium, target one D8.</summary>
    private static FireDeclaration Declare(
        string firerId = "A",
        string targetId = "T",
        int armour = 2,
        int chitCount = 1,
        int barrels = 1,
        WeaponRangeBand band = WeaponRangeBand.Medium,
        bool firerIsDamaged = false,
        WeaponValidityCard? card = null) => new(
        new FiringElement(firerId, FireControlLevel.Enhanced, IsDamaged: firerIsDamaged),
        new WeaponMount(chitCount, card ?? AnyColour, barrels),
        new TargetElement(targetId, Signature: 3, ArmourValue: armour),
        band);

    [Fact]
    public void AHitRunsStraightIntoAChitDrawAndKeepsTheWholeTrail()
    {
        // Target rolls 2, the single barrel rolls 7, and the chit that comes out is a Red 3 against
        // armour 2. Every one of those numbers is on the result, because a table needs to see why a
        // vehicle just died rather than being told that it did.
        var pot = new RecordingPot(Red(3));

        var shot = DirectFire.Resolve(Declare(), new ScriptedDice(2, 7), pot);

        Assert.True(shot.WasFired);
        Assert.Equal(1, shot.Hits);
        Assert.Equal(7, shot.Attempts[0].FirerRoll);
        Assert.Equal(2, shot.Attempts[0].TargetScore);
        Assert.Equal(QualityDie.D8, shot.Attempts[0].Solution.FirerDie);
        Assert.Equal(NumericalDamage.KnockedOut, shot.Damage[0].Numerical);
        Assert.Equal(Red(3), shot.Damage[0].Draw[0].Chit);
        Assert.True(shot.TargetDestroyed);
    }

    [Fact]
    public void AMissNeverTouchesThePot()
    {
        var pot = new RecordingPot(Red(3));

        var shot = DirectFire.Resolve(Declare(), new ScriptedDice(7, 2), pot);

        Assert.Equal(0, shot.Hits);
        Assert.Empty(shot.Damage);
        Assert.Empty(pot.Requests);
    }

    [Fact]
    public void DeclarationIsBindingSoAShotAtADeadTargetIsWastedRatherThanRePointed()
    {
        // Two shots declared at one element before any dice. The first kills it; the second is spent
        // on a wreck. An engine that let the second look around for something else alive would be
        // playing a much more forgiving game than this one.
        var declarations = new[] { Declare("A"), Declare("B") };
        var pot = new RecordingPot(Red(3));
        var dice = new ScriptedDice(2, 7);

        var volley = DirectFire.Resolve(declarations, dice, pot);

        Assert.True(volley.Shots[0].TargetDestroyed);
        Assert.False(volley.Shots[1].WasFired);
        Assert.Equal(ShotRefusal.TargetAlreadyDestroyed, volley.Shots[1].Refusal);
        Assert.Contains("binding", volley.Shots[1].Reason!, StringComparison.OrdinalIgnoreCase);

        // Not merely marked wasted: no die and no chit were spent on it either.
        Assert.Equal(0, dice.Remaining);
        Assert.Equal(OneDrawOfOne, pot.Requests);
        Assert.Single(volley.Wasted);
        Assert.Equal("T", Assert.Single(volley.Destroyed));
    }

    [Fact]
    public void AShotAtSomeoneElseIsUntouchedByTheDeathOfAnotherTarget()
    {
        // The waste rule is about the element that was designated, not about the volley as a whole.
        var declarations = new[] { Declare("A", "T1"), Declare("B", "T2") };
        var pot = new RecordingPot(Red(3), Red(3));

        var volley = DirectFire.Resolve(declarations, new ScriptedDice(2, 7, 2, 7), pot);

        Assert.All(volley.Shots, s => Assert.True(s.WasFired));
        Assert.Equal(2, volley.Destroyed.Count);
    }

    [Fact]
    public void ATwinMountRollsOneExtraDiePerBarrelAgainstASingleTargetRoll()
    {
        // The target throws once and every barrel is checked against that one score. Rerolling the
        // target per barrel would turn the mount into two independent duels and wash out exactly the
        // streakiness that makes a twin turret feel like one.
        var pot = new RecordingPot(Red(0), Red(0));

        var shot = DirectFire.Resolve(Declare(barrels: 2), new ScriptedDice(5, 7, 3), pot);

        Assert.Equal(2, shot.Attempts.Count);
        Assert.All(shot.Attempts, a => Assert.Equal(5, a.TargetScore));
        Assert.True(shot.Attempts[0].IsHit);
        Assert.False(shot.Attempts[1].IsHit);
        Assert.Equal(1, shot.Hits);
        Assert.Single(shot.Damage);
    }

    [Fact]
    public void EveryBarrelThatHitsGetsItsOwnIndependentDraw()
    {
        // Two hits are two draws of the weapon's own size, never one draw of twice it. The pot makes
        // this true by construction, but it is the property most likely to be "optimised" away by
        // somebody batching the draws, so it is asserted here rather than assumed.
        var pot = new RecordingPot(Red(1), Red(1), Red(1), Red(1), Red(1), Red(1));

        var shot = DirectFire.Resolve(Declare(chitCount: 3, barrels: 2), new ScriptedDice(2, 7, 7), pot);

        Assert.Equal(2, shot.Hits);
        Assert.Equal(TwoDrawsOfThree, pot.Requests);
        Assert.Equal(2, shot.Damage.Count);
    }

    [Fact]
    public void ThePotIsWholeAgainBetweenTwoBarrelsOfTheSameMount()
    {
        // The same scarce chit can come out for both barrels, because the second barrel draws from a
        // full pot rather than from what the first one left behind.
        var pot = new ChitPot(
            ChitPotComposition.Of([DamageChit.Of(ChitSpecial.Boom), Red(0), Red(0)]),
            _ => 0);

        var shot = DirectFire.Resolve(Declare(chitCount: 3, barrels: 2), new ScriptedDice(2, 7, 7), pot);

        Assert.Equal(2, shot.Damage.Count);
        Assert.All(shot.Damage, d => Assert.True(d.CatastrophicKill));
    }

    [Fact]
    public void BothBarrelsResolveEvenWhenTheFirstOneAlreadyKilledTheTarget()
    {
        // A mount fires together, so its barrels are simultaneous. The binding-declaration waste rule
        // is about separately declared shots; applying it inside one mount would have the second
        // barrel of a twin turret behaving as though it had waited to see what the first did.
        var pot = new RecordingPot(Red(3), Red(3));

        var shot = DirectFire.Resolve(Declare(barrels: 2, armour: 1), new ScriptedDice(2, 7, 8), pot);

        Assert.Equal(2, shot.Damage.Count);
        Assert.All(shot.Damage, d => Assert.Equal(NumericalDamage.KnockedOut, d.Numerical));
        Assert.Equal(TwoDrawsOfOne, pot.Requests);
    }

    [Fact]
    public void ADamagedFirersCloseShotReallyResolvesAsMedium()
    {
        var shot = DirectFire.Resolve(
            Declare(band: WeaponRangeBand.Close, firerIsDamaged: true),
            new ScriptedDice(2, 7),
            new RecordingPot(Red(3)));

        Assert.Equal(WeaponRangeBand.Close, shot.Band.Measured);
        Assert.Equal(WeaponRangeBand.Medium, shot.Band.Resolved);
        Assert.True(shot.Band.WasDegraded);

        // Stage one really used it: an enhanced sight is D10 close and D8 medium.
        Assert.Equal(QualityDie.D8, shot.Attempts[0].Solution.FirerDie);
    }

    [Fact]
    public void TheDegradedBandReachesValidityAndNotJustTheToHitDie()
    {
        // The trap. For a weapon whose colours narrow with range, a DMG marker on the firer changes
        // what its hits can do, not merely how often it lands them - so validity has to be read at
        // the band the shot resolved at. Card: everything counts close, red only at medium. The chit
        // drawn is green, so the two bands give opposite answers and nothing else differs.
        var card = new WeaponValidityCard(
            new ChitValidity(ChitColours.All),
            new ChitValidity(ChitColours.Red),
            ChitValidity.Ineffective);

        var healthy = DirectFire.Resolve(
            Declare(band: WeaponRangeBand.Close, armour: 1, card: card),
            new ScriptedDice(1, 8),
            new RecordingPot(Green(3)));

        var damaged = DirectFire.Resolve(
            Declare(band: WeaponRangeBand.Close, armour: 1, firerIsDamaged: true, card: card),
            new ScriptedDice(1, 8),
            new RecordingPot(Green(3)));

        Assert.True(healthy.Attempts[0].IsHit);
        Assert.True(damaged.Attempts[0].IsHit);

        Assert.Equal(NumericalDamage.KnockedOut, healthy.Damage[0].Numerical);
        Assert.Equal(NumericalDamage.None, damaged.Damage[0].Numerical);

        // And the green chit was drawn either way. It burned its slot at medium rather than being
        // filtered out of the draw.
        Assert.Single(damaged.Damage[0].Draw);
        Assert.True(damaged.Damage[0].Draw[0].WastedTheSlot);
    }

    [Fact]
    public void ADamagedFirerCannotTakeALongShotAtAll()
    {
        var dice = new ScriptedDice(2, 7);
        var pot = new RecordingPot(Red(3));

        var shot = DirectFire.Resolve(
            Declare(band: WeaponRangeBand.Long, firerIsDamaged: true), dice, pot);

        Assert.False(shot.WasFired);
        Assert.Equal(ShotRefusal.BandOutOfReach, shot.Refusal);
        Assert.False(shot.Band.CanFire);
        Assert.Empty(shot.Attempts);
        Assert.Empty(pot.Requests);
        Assert.Equal(2, dice.Remaining);
    }

    [Fact]
    public void AShotWithNoDieLeftIsRefusedRatherThanResolvedAsAMiss()
    {
        // Stage one's own refusal, carried through the pipeline instead of being swallowed.
        var declaration = new FireDeclaration(
            new FiringElement("A", FireControlLevel.Basic, MovedOverHalf: true),
            new WeaponMount(1, AnyColour),
            new TargetElement("T", 3, 2),
            WeaponRangeBand.Long);

        var shot = DirectFire.Resolve(declaration, new ScriptedDice(), new RecordingPot());

        Assert.Equal(ShotRefusal.NoDieLeft, shot.Refusal);
        Assert.Contains("no die left", shot.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFirerWhoseOwnSystemsGoDownTakesNoFurtherDeclaredShots()
    {
        // The chit that rewrites its own shot also ends the firer's part in the volley.
        var declarations = new[] { Declare("A", "T1"), Declare("A", "T2") };
        var pot = new RecordingPot(DamageChit.Of(ChitSpecial.SystemsDownFirer), Red(3));

        var volley = DirectFire.Resolve(declarations, new ScriptedDice(2, 7, 2, 7), pot);

        Assert.True(volley.Shots[0].FirerSystemsDown);
        Assert.True(volley.Shots[0].Damage[0].ShotNeverHappened);
        Assert.False(volley.Shots[1].WasFired);
        Assert.Equal(ShotRefusal.FirerSystemsDown, volley.Shots[1].Refusal);
        Assert.Empty(volley.Destroyed);
    }

    [Fact]
    public void AnEmptyVolleyIsAVolley() =>
        Assert.Empty(DirectFire.Resolve(
            Array.Empty<FireDeclaration>(), new ScriptedDice(), new RecordingPot()).Shots);

    [Fact]
    public void ANonsenseShotIsRefusedRatherThanGuessedAt()
    {
        var pot = new RecordingPot();
        var dice = new ScriptedDice();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DirectFire.Resolve(Declare(barrels: 0), dice, pot));
        Assert.Throws<ArgumentNullException>(() => DirectFire.Resolve((FireDeclaration)null!, dice, pot));
        Assert.Throws<ArgumentNullException>(() => DirectFire.Resolve(Declare(), null!, pot));
        Assert.Throws<ArgumentNullException>(() => DirectFire.Resolve(Declare(), dice, null!));
        Assert.Throws<ArgumentNullException>(() =>
            DirectFire.Resolve((IEnumerable<FireDeclaration>)null!, dice, pot));
    }

    private sealed class ScriptedDice(params int[] rolls) : IQualityDiceRoller
    {
        private readonly Queue<int> _rolls = new(rolls);

        public int Remaining => _rolls.Count;

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
