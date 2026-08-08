using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// Stage two turns a hit into a result. Almost everything interesting here is about what happens to
/// the chits a weapon is <em>not</em> allowed to count, because that is the only thing separating one
/// weapon from another.
/// </summary>
public sealed class DamageResolutionTests
{
    private static readonly int[] OneHandOfThree = [3];
    private static readonly int[] HandsOfOneFiveAndSix = [1, 5, 6];
    private static readonly ChitValidity RedOnly = new(ChitColours.Red);
    private static readonly ChitValidity AnyColour = new(ChitColours.All);

    private static DamageChit Red(int value) => DamageChit.Numerical(ChitColour.Red, value);

    private static DamageChit Green(int value) => DamageChit.Numerical(ChitColour.Green, value);

    private static DamageChit Yellow(int value) => DamageChit.Numerical(ChitColour.Yellow, value);

    [Fact]
    public void AnInvalidChitBurnsItsSlotInsteadOfBeingRedrawn()
    {
        // The mechanism. Three chits are drawn and two of them are colours this weapon cannot count,
        // so the shot is left with one chit's worth of damage rather than three. Had the engine
        // filtered before counting - discarding the greens and pulling replacements - the total
        // would have been the full three chits and this weapon would be indistinguishable from one
        // that counts every colour.
        var pot = new ScriptedPot(Green(3), Red(1), Green(3));

        var outcome = DamageResolution.Resolve(3, RedOnly, armourValue: 4, pot);

        Assert.Equal(3, outcome.Draw.Count);
        Assert.Equal(1, outcome.ValidTotal);
        Assert.Equal(2, outcome.Draw.Count(d => d.WastedTheSlot));
        Assert.Equal(NumericalDamage.None, outcome.Numerical);
        Assert.Equal(OneHandOfThree, pot.Requests);
    }

    [Fact]
    public void TwoWeaponsDrawingTheSameChitsDifferOnlyByWhatTheyMayCount()
    {
        // Same draw, same count, same armour. The entire spread between the two weapons comes from
        // throwing draws away.
        var picky = DamageResolution.Resolve(3, RedOnly, 4, new ScriptedPot(Green(3), Red(1), Yellow(2)));
        var permissive = DamageResolution.Resolve(3, AnyColour, 4, new ScriptedPot(Green(3), Red(1), Yellow(2)));

        Assert.Equal(NumericalDamage.None, picky.Numerical);
        Assert.Equal(NumericalDamage.KnockedOut, permissive.Numerical);
    }

    [Fact]
    public void AWeaponWithNoValidColourStillDrawsAndStillFindsSpecials()
    {
        // "No valid colours" is not "ineffective". This weapon can do nothing with the numbers and
        // still immobilises the target, because specials are not colour-gated.
        var pot = new ScriptedPot(Green(3), DamageChit.Of(ChitSpecial.Mobility));

        var outcome = DamageResolution.Resolve(2, new ChitValidity(ChitColours.None), armourValue: 1, pot);

        Assert.Equal(0, outcome.ValidTotal);
        Assert.Equal(NumericalDamage.None, outcome.Numerical);
        Assert.True(outcome.Immobilised);
    }

    [Fact]
    public void AnIneffectiveWeaponDrawsNothingAtAll()
    {
        // The short-circuit has to happen before a chit leaves the pot. A pot that is nothing but
        // Booms proves it: if a single chit were drawn, the target would be gone.
        var pot = new ScriptedPot(DamageChit.Of(ChitSpecial.Boom), DamageChit.Of(ChitSpecial.Boom));

        var outcome = DamageResolution.Resolve(2, ChitValidity.Ineffective, armourValue: 1, pot);

        Assert.Empty(outcome.Draw);
        Assert.Empty(pot.Requests);
        Assert.False(outcome.CatastrophicKill);
        Assert.False(outcome.Immobilised);
        Assert.False(outcome.TargetSystemsDown);
        Assert.False(outcome.FirerSystemsDown);
        Assert.True(outcome.TargetUnharmed);
    }

    [Fact]
    public void AnIneffectiveWeaponIsAResultRatherThanARefusal()
    {
        // Unlike stage one refusing to roll a die that does not exist, the round here really did
        // connect and really did nothing, and the after-action log wants to be able to say so.
        var solution = DamageResolution.Solve(4, ChitValidity.Ineffective, armourValue: 2);

        Assert.False(solution.DrawsChits);
        Assert.NotNull(solution.Reason);
        Assert.True(DamageResolution.Resolve(solution, new ScriptedPot()).TargetUnharmed);
    }

    [Theory]
    [InlineData(3, NumericalDamage.None)]
    [InlineData(4, NumericalDamage.Damaged)]
    [InlineData(5, NumericalDamage.KnockedOut)]
    public void TheThreeOutcomesSitOnTheirExactBoundaries(int total, NumericalDamage expected)
    {
        // Equal is a hit, not a bounce. That boundary is the whole texture of the game's armour
        // values, and a "greater than or equal" shortcut anywhere would erase the DAMAGED result.
        Assert.Equal(expected, DamageResolution.Compare(total, armourValue: 4));

        var outcome = DamageResolution.Resolve(2, AnyColour, 4, new ScriptedPot(Red(total), Red(0)));
        Assert.Equal(expected, outcome.Numerical);
    }

    [Fact]
    public void AZeroChitIsAValidDrawRatherThanAWastedOne()
    {
        var outcome = DamageResolution.Resolve(2, RedOnly, 1, new ScriptedPot(Red(0), Green(0)));

        var zero = outcome.Draw[0];
        Assert.True(zero.Counts);
        Assert.True(zero.IsValidZero);
        Assert.False(zero.WastedTheSlot);

        // The green adds nothing either, but for an entirely different reason.
        Assert.False(outcome.Draw[1].Counts);
        Assert.False(outcome.Draw[1].IsValidZero);
    }

    [Fact]
    public void ASpecialFiresEvenWhenTheNumbersBounceOff()
    {
        var outcome = DamageResolution.Resolve(
            2, AnyColour, armourValue: 9, new ScriptedPot(Red(1), DamageChit.Of(ChitSpecial.Mobility)));

        Assert.Equal(NumericalDamage.None, outcome.Numerical);
        Assert.True(outcome.Immobilised);
        Assert.False(outcome.TargetDestroyed);
    }

    [Fact]
    public void AKnockOutSuppressesEverySpecialBesideIt()
    {
        // Nothing left for a special to do: the vehicle is already gone.
        var pot = new ScriptedPot(
            Red(3),
            DamageChit.Of(ChitSpecial.Mobility),
            DamageChit.Of(ChitSpecial.SystemsDownTarget),
            DamageChit.Of(ChitSpecial.Boom));

        var outcome = DamageResolution.Resolve(4, AnyColour, armourValue: 2, pot);

        Assert.Equal(NumericalDamage.KnockedOut, outcome.Numerical);
        Assert.False(outcome.Immobilised);
        Assert.False(outcome.TargetSystemsDown);
        Assert.False(outcome.CatastrophicKill);
        Assert.True(outcome.TargetDestroyed);
    }

    [Fact]
    public void MerelyBeingDamagedDoesNotSuppressASpecial()
    {
        // The boundary either side of the suppression rule: exact armour is DAMAGED, and DAMAGED is
        // not gone, so the Mobility chit still lands.
        var pot = new ScriptedPot(Red(3), DamageChit.Of(ChitSpecial.Mobility));

        var outcome = DamageResolution.Resolve(2, AnyColour, armourValue: 3, pot);

        Assert.Equal(NumericalDamage.Damaged, outcome.Numerical);
        Assert.True(outcome.Immobilised);
        Assert.True(outcome.TargetDamaged);
    }

    [Fact]
    public void ABoomDestroysWhateverTheNumbersSaid()
    {
        var outcome = DamageResolution.Resolve(
            2, AnyColour, armourValue: 12, new ScriptedPot(Red(0), DamageChit.Of(ChitSpecial.Boom)));

        Assert.Equal(NumericalDamage.None, outcome.Numerical);
        Assert.True(outcome.CatastrophicKill);
        Assert.True(outcome.TargetDestroyed);
    }

    [Fact]
    public void TheFirersOwnSystemsGoingDownThrowsAwayTheWholeShot()
    {
        // Everything else in the same draw goes with it. The shot did not happen, so the damage it
        // would have done did not happen either - and the marker lands on the firer instead.
        var pot = new ScriptedPot(
            Red(3),
            DamageChit.Of(ChitSpecial.Mobility),
            DamageChit.Of(ChitSpecial.SystemsDownFirer));

        var outcome = DamageResolution.Resolve(3, AnyColour, armourValue: 1, pot);

        Assert.True(outcome.ShotNeverHappened);
        Assert.True(outcome.FirerSystemsDown);
        Assert.Equal(0, outcome.ValidTotal);
        Assert.Equal(NumericalDamage.None, outcome.Numerical);
        Assert.False(outcome.Immobilised);
        Assert.True(outcome.TargetUnharmed);

        // The chits are still on the record, because the log wants to show why the shot evaporated.
        Assert.Equal(3, outcome.Draw.Count);
    }

    [Fact]
    public void AKnockOutCannotSuppressTheFirersSystemsGoingDown()
    {
        // The one ordering that has to be this way round: a knock-out that never occurred cannot
        // suppress anything, so this is checked against the draw rather than against the survivors.
        var pot = new ScriptedPot(Red(3), Red(3), DamageChit.Of(ChitSpecial.SystemsDownFirer));

        var outcome = DamageResolution.Resolve(3, AnyColour, armourValue: 2, pot);

        Assert.True(outcome.ShotNeverHappened);
        Assert.True(outcome.FirerSystemsDown);
        Assert.False(outcome.TargetDestroyed);
    }

    [Fact]
    public void ATargetTheSpecialsDoNotApplyToIgnoresThemAndStillLosesTheSlot()
    {
        // Infantry. The special is drawn, does nothing at all, and is not replaced.
        var validity = new ChitValidity(ChitColours.Yellow, SpecialsCount: false);
        var pot = new ScriptedPot(DamageChit.Of(ChitSpecial.Boom), Yellow(2));

        var outcome = DamageResolution.Resolve(2, validity, armourValue: 2, pot);

        Assert.False(outcome.CatastrophicKill);
        Assert.True(outcome.Draw[0].WastedTheSlot);
        Assert.Equal(NumericalDamage.Damaged, outcome.Numerical);
    }

    [Fact]
    public void TheFirersSystemsChitDoesNothingAgainstATargetSpecialsDoNotApplyTo()
    {
        var validity = new ChitValidity(ChitColours.All, SpecialsCount: false);
        var pot = new ScriptedPot(Red(3), DamageChit.Of(ChitSpecial.SystemsDownFirer));

        var outcome = DamageResolution.Resolve(2, validity, armourValue: 3, pot);

        Assert.False(outcome.ShotNeverHappened);
        Assert.False(outcome.FirerSystemsDown);
        Assert.Equal(NumericalDamage.Damaged, outcome.Numerical);
    }

    [Fact]
    public void TheChitCountIsWhateverTheCallerSaysItIs()
    {
        // Nothing here derives the count from a weapon: the base rule ties it to size class, but
        // launchers, flat-rated weapons and multi-tube artillery all set it some other way.
        var pot = new ScriptedPot([.. Enumerable.Repeat(Red(1), 12)]);

        DamageResolution.Resolve(1, RedOnly, 1, pot);
        DamageResolution.Resolve(5, RedOnly, 1, pot);
        DamageResolution.Resolve(6, RedOnly, 1, pot);

        Assert.Equal(HandsOfOneFiveAndSix, pot.Requests);
    }

    [Fact]
    public void ANonsenseSolveIsRefusedRatherThanGuessedAt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DamageResolution.Solve(-1, AnyColour, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => DamageResolution.Solve(3, AnyColour, -1));
        Assert.Throws<ArgumentNullException>(() => DamageResolution.Solve(3, null!, 3));
        Assert.Throws<ArgumentNullException>(() => DamageResolution.Resolve(3, AnyColour, 3, null!));
    }

    /// <summary>A pot that hands out a scripted hand, and remembers what it was asked for.</summary>
    private sealed class ScriptedPot(params DamageChit[] chits) : IChitPot
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
