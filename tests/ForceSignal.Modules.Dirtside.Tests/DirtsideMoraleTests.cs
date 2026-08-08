using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.GroundCombat.Morale;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// Dirtside's half of morale: the two-column effects table and the Under Fire flag. The ladder and
/// the test itself are shared and tested in the ground-combat module.
/// </summary>
public sealed class DirtsideMoraleTests
{
    private const DirtsideUnitKind Foot = DirtsideUnitKind.DismountedInfantry;
    private const DirtsideUnitKind Armour = DirtsideUnitKind.Armour;

    [Theory]
    [InlineData(ConfidenceLevel.Confident)]
    [InlineData(ConfidenceLevel.Steady)]
    public void NothingIsRestrictedAtTheTopOfTheLadderInEitherColumn(ConfidenceLevel level)
    {
        Assert.Equal(ConfidenceRestriction.None, DirtsideConfidence.Restrictions(level, Foot));
        Assert.Equal(ConfidenceRestriction.None, DirtsideConfidence.Restrictions(level, Armour));
    }

    [Fact]
    public void TheTwoColumnsAreNotTheSameTableWithOneSofter()
    {
        // This is why no effects table went down into the shared layer. Armour balks first - a shaken
        // crew will not charge and needs talking out of cover - and then one rung lower it reverses,
        // with broken infantry still fighting from where it stands while broken armour turns for
        // home. Neither column is the other's milder version.
        var shakenFoot = DirtsideConfidence.Restrictions(ConfidenceLevel.Shaken, Foot);
        var shakenArmour = DirtsideConfidence.Restrictions(ConfidenceLevel.Shaken, Armour);

        Assert.Equal(ConfidenceRestriction.ReactionTestToAdvance, shakenFoot);
        Assert.True(shakenArmour.HasFlag(ConfidenceRestriction.MayNotCloseAssault));
        Assert.True(shakenArmour.HasFlag(ConfidenceRestriction.RoutedIfCloseAssaulted));

        var brokenFoot = DirtsideConfidence.Restrictions(ConfidenceLevel.Broken, Foot);
        var brokenArmour = DirtsideConfidence.Restrictions(ConfidenceLevel.Broken, Armour);

        Assert.False(brokenFoot.HasFlag(ConfidenceRestriction.MustWithdrawToBaseline));
        Assert.True(brokenArmour.HasFlag(ConfidenceRestriction.MustWithdrawToBaseline));
        Assert.True(brokenArmour.HasFlag(ConfidenceRestriction.ReturnFireOnly));
    }

    [Fact]
    public void ShakenInfantryIsRestrictedThroughTheTestSystemRatherThanByABan()
    {
        Assert.True(DirtsideConfidence.NeedsReactionTestToAdvance(ConfidenceLevel.Shaken, Foot));
        Assert.False(DirtsideConfidence.RefusesToAdvance(ConfidenceLevel.Shaken, Foot));
        Assert.True(DirtsideConfidence.MayPickItsOwnTarget(ConfidenceLevel.Shaken, Foot));
    }

    [Fact]
    public void BrokenArmourStillShootsBackButPicksNothingOfItsOwn()
    {
        Assert.False(DirtsideConfidence.MayPickItsOwnTarget(ConfidenceLevel.Broken, Armour));
        Assert.False(DirtsideConfidence.HasStoppedFiring(ConfidenceLevel.Broken, Armour));
    }

    [Theory]
    [InlineData(Foot)]
    [InlineData(Armour)]
    public void RoutedIsTheSameEndForBoth(DirtsideUnitKind kind)
    {
        Assert.True(DirtsideConfidence.HasStoppedFiring(ConfidenceLevel.Routed, kind));
        Assert.True(DirtsideConfidence.RefusesToAdvance(ConfidenceLevel.Routed, kind));
        Assert.True(DirtsideConfidence
            .Restrictions(ConfidenceLevel.Routed, kind)
            .HasFlag(ConfidenceRestriction.MustWithdrawToBaseline));
    }

    [Fact]
    public void ShakenArmourThatIsChargedBreaksWithoutTesting()
    {
        // A crew whose nerve is already going does not fight men climbing onto the hull.
        Assert.Equal(
            ConfidenceLevel.Routed, DirtsideConfidence.OnCloseAssaulted(ConfidenceLevel.Shaken, Armour));

        // Foot at the same rung stands and takes its test like anybody else.
        Assert.Equal(
            ConfidenceLevel.Shaken, DirtsideConfidence.OnCloseAssaulted(ConfidenceLevel.Shaken, Foot));
        Assert.Equal(
            ConfidenceLevel.Steady, DirtsideConfidence.OnCloseAssaulted(ConfidenceLevel.Steady, Armour));
    }

    [Theory]
    [InlineData(ConfidenceLevel.Broken)]
    [InlineData(ConfidenceLevel.Routed)]
    public void NobodyWhoseNerveHasGoneWillCloseAssault(ConfidenceLevel level)
    {
        Assert.True(DirtsideConfidence
            .Restrictions(level, Foot)
            .HasFlag(ConfidenceRestriction.MayNotCloseAssault));
        Assert.True(DirtsideConfidence
            .Restrictions(level, Armour)
            .HasFlag(ConfidenceRestriction.MayNotCloseAssault));
    }

    [Fact]
    public void MenAreMarkedByBeingShotAtAndVehiclesOnlyByBeingHit()
    {
        Assert.True(UnderFire.MarksTarget(Foot, damagedAnElement: false));
        Assert.False(UnderFire.MarksTarget(Armour, damagedAnElement: false));
        Assert.True(UnderFire.MarksTarget(Armour, damagedAnElement: true));
    }

    [Fact]
    public void TheMarkerIsBinaryAndLapsesWithTheUnitsOwnActivation()
    {
        // Nothing like the other game's stacking suppression: one flag, cleared by the unit's own
        // activation ending, and carried into the next turn by a unit that had already gone.
        Assert.True(UnderFire.OwesReactionTestToMove(isMarked: true));
        Assert.False(UnderFire.OwesReactionTestToMove(isMarked: false));

        Assert.False(UnderFire.After(isMarked: true, itsOwnActivationEnded: true));
        Assert.True(UnderFire.After(isMarked: true, itsOwnActivationEnded: false));
    }
}
