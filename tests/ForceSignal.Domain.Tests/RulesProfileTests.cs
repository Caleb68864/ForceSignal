using System.Collections.Immutable;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Domain.Tests;

/// <summary>
/// The rules profile is where every number a Full Thrust match is played against now lives, supplied
/// by the players. These tests cover the three things it owes its callers: saying what is missing,
/// reading its own tables, and comparing by contents.
/// </summary>
public sealed class RulesProfileTests
{
    [Fact]
    public void AnEmptyProfileIsBlankRatherThanTypical()
    {
        // Nothing here is a starting suggestion. A default that happened to be somebody's published
        // numbers would put the rulebook back in this repository, which is the whole point of the
        // profile existing.
        var empty = RulesProfile.Empty;

        Assert.Equal(string.Empty, empty.Name);
        Assert.Equal(0, empty.DieFaces);
        Assert.Empty(empty.BeamDamage);
        Assert.Equal(0, empty.BeamRangeBandWidth);
        Assert.Equal(0, empty.ThresholdRowCount);
    }

    [Fact]
    public void AnEmptyProfileCannotBePlayedAgainstAndSaysWhy()
    {
        var empty = RulesProfile.Empty;

        Assert.False(empty.IsPlayable);
        Assert.NotEmpty(empty.Validate());
    }

    [Fact]
    public void ACompleteProfileIsPlayable()
    {
        Assert.True(Complete().IsPlayable);
        Assert.Empty(Complete().Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AProfileWithoutANameIsRefused(string name) =>
        Assert.NotEmpty((Complete() with { Name = name }).Validate());

    [Fact]
    public void AProfileWithNoBeamTableIsRefused() =>
        Assert.NotEmpty((Complete() with { BeamDamage = [] }).Validate());

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-1)]
    public void ABeamEntryNamingAFaceTheDieDoesNotHaveIsRefused(int face)
    {
        // A wrongly keyed import once produced a table of face-0 entries that the profile accepted
        // without a word, so every beam rolled for nothing and nobody was told why. A face the die
        // cannot show is a mistake in the table, not a choice.
        var profile = Complete() with { BeamDamage = [.. Complete().BeamDamage, new BeamDamageEntry(face, 0, 1)] };

        Assert.Contains(profile.Validate(), gap => gap.Contains("face", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void APointDefenceOrTurnaroundEntryOffTheDieIsRefused()
    {
        var pointDefence = Complete() with { PointDefenseKills = [new PointDefenseEntry(0, 1)] };
        var turnaround = Complete() with { CarrierTurnaroundRoll = true, Turnaround = [new TurnaroundEntry(99, false, 1)] };

        Assert.NotEmpty(pointDefence.Validate());
        Assert.NotEmpty(turnaround.Validate());
    }

    [Fact]
    public void AProfileWithASingleRowHullTrackIsRefused()
    {
        // One row is the whole hull, so nothing would ever be checked before the ship died.
        Assert.NotEmpty((Complete() with { ThresholdRowCount = 1 }).Validate());
    }

    [Fact]
    public void SectionsNobodyFilledInAreLeftAloneRatherThanDemanded()
    {
        // A table with no salvo missiles in it should not have to describe them. Only a section that
        // is half entered is a mistake; an empty one is a choice.
        var noOrdnance = Complete() with
        {
            MissilesPerSalvo = 0,
            SalvoAttackRadius = 0,
            PointDefenseRange = 0,
            PointDefenseKills = [],
        };

        Assert.True(noOrdnance.IsPlayable);
    }

    [Fact]
    public void AHalfEnteredSectionIsRefused()
    {
        // A salvo size with no attack radius is somebody stopping half way, not a deliberate omission.
        Assert.NotEmpty((Complete() with { SalvoAttackRadius = 0 }).Validate());
    }

    [Fact]
    public void AnEnhancedNeedleWithoutItsRollIsRefused() =>
        Assert.NotEmpty((Complete() with { EnhancedNeedleBeams = true, NeedleHullDamageRoll = 0 }).Validate());

    [Fact]
    public void ABeamFaceTheProfileDoesNotMentionScoresNothing()
    {
        // A player only enters the faces that do something, so the rest have to read as zero rather
        // than as missing.
        Assert.Equal(0, Complete().BeamDamageFor(dieFace: 2, screenLevel: 0));
        Assert.Equal(2, Complete().BeamDamageFor(dieFace: 8, screenLevel: 0));
    }

    [Fact]
    public void AScreenLevelAboveTheCeilingIsReadAsTheCeiling()
    {
        var profile = Complete();

        Assert.Equal(profile.BeamDamageFor(8, profile.MaxScreenLevel), profile.BeamDamageFor(8, 99));
    }

    [Fact]
    public void APointDefenceFaceTheProfileDoesNotMentionShootsNothingDown()
    {
        Assert.Equal(0, Complete().PointDefenseKillsFor(1));
        Assert.Equal(2, Complete().PointDefenseKillsFor(8));
    }

    [Fact]
    public void ATurnaroundFaceTheProfileDoesNotMentionIsTheKindestReading()
    {
        // An unmentioned face should not ground a group by accident, so it lands ready next turn.
        var unmentioned = Complete().TurnaroundFor(5);

        Assert.False(unmentioned.IsGroundedForGame);
        Assert.Equal(1, unmentioned.TurnsBeforeRelaunch);
    }

    [Fact]
    public void TwoProfilesBuiltFromTheSameNumbersAreEqual()
    {
        // The tables are immutable arrays, which compare by reference - so two profiles typed in
        // identically would come out unequal without the comparison this type writes by hand. That
        // has bitten this codebase before, in a test that passed for the wrong reason.
        Assert.Equal(Complete(), Complete());
        Assert.Equal(Complete().GetHashCode(), Complete().GetHashCode());
    }

    [Fact]
    public void TwoProfilesDifferingOnlyInsideATableAreNotEqual()
    {
        var softer = Complete() with { BeamDamage = [new BeamDamageEntry(8, 0, 1)] };

        Assert.NotEqual(Complete(), softer);
    }

    /// <summary>A complete profile of invented numbers, matching nobody's published game.</summary>
    private static RulesProfile Complete() => new(
        Name: "Invented Test Set",
        DieFaces: 8,
        BeamDamage: [new BeamDamageEntry(8, 0, 2), new BeamDamageEntry(7, 0, 1), new BeamDamageEntry(8, 1, 1)],
        BeamRangeBandWidth: 10,
        MaxScreenLevel: 2,
        TorpedoMaximumRange: 40,
        TorpedoBandWidth: 8,
        TorpedoBestToHit: 3,
        NeedleBeamRange: 7,
        NeedleSystemKillRoll: 8,
        EnhancedNeedleBeams: false,
        NeedleHullDamageRoll: 7,
        ThresholdRows: ThresholdRowMode.FixedRows,
        ThresholdRowCount: 3,
        EscortRowCount: 2,
        CruiserRowCount: 3,
        MaxPartiesPerJob: 2,
        RepairRollWithOneParty: 7,
        RepairBestRoll: 6,
        FighterMoveAllowance: 15,
        CarrierRatesFollowBays: false,
        TrueCarrierAllowance: 3,
        OtherShipAllowance: 1,
        CarrierTurnaroundRoll: false,
        Turnaround: [new TurnaroundEntry(1, true, 0)],
        PointDefenseRange: 5,
        PointDefenseKills: [new PointDefenseEntry(8, 2), new PointDefenseEntry(7, 1)],
        PointDefenseChainOnFace: 8,
        MissilesPerSalvo: 4,
        SalvoAttackRadius: 5);
}
