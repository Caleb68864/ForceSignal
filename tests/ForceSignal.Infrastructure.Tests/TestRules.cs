using System.Collections.Immutable;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Infrastructure.Tests;

/// <summary>
/// A complete rules profile for tests to play against.
/// </summary>
/// <remarks>
/// The numbers here are invented, and deliberately unlike any published game's: an eight-sided die,
/// ten-mu beam bands, three hull rows, a needle that kills on an 8. That is the point of them. The
/// engine ships no numbers of its own, so a test that passes against these proves the code read the
/// profile it was handed rather than remembering something. A fixture copied out of a rulebook would
/// prove neither, and would put the rulebook back in the repository by the back door.
/// </remarks>
internal static class TestRules
{
    /// <summary>The invented profile, complete enough to play a whole match against.</summary>
    internal static RulesProfile Invented { get; } = new(
        Name: "Invented Test Set",
        DieFaces: 8,
        BeamDamage:
        [
            // Unscreened, the top two faces score two and the two below them score one.
            new BeamDamageEntry(8, 0, 2),
            new BeamDamageEntry(7, 0, 2),
            new BeamDamageEntry(6, 0, 1),
            new BeamDamageEntry(5, 0, 1),

            // One level of screening flattens every hit to a single point.
            new BeamDamageEntry(8, 1, 1),
            new BeamDamageEntry(7, 1, 1),
            new BeamDamageEntry(6, 1, 1),

            // Two levels stop everything but the very top face.
            new BeamDamageEntry(8, 2, 1),
        ],
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
        Turnaround:
        [
            new TurnaroundEntry(1, IsGroundedForGame: true, TurnsBeforeRelaunch: 0),
            new TurnaroundEntry(3, IsGroundedForGame: false, TurnsBeforeRelaunch: 2),
            new TurnaroundEntry(8, IsGroundedForGame: false, TurnsBeforeRelaunch: 1),
        ],
        PointDefenseRange: 5,
        PointDefenseKills:
        [
            new PointDefenseEntry(8, 2),
            new PointDefenseEntry(7, 1),
            new PointDefenseEntry(6, 1),
        ],
        PointDefenseChainOnFace: 8,
        MissilesPerSalvo: 4,
        SalvoAttackRadius: 5);

    /// <summary>The same profile with the enhanced needle switched on.</summary>
    internal static RulesProfile WithEnhancedNeedles { get; } =
        Invented with { EnhancedNeedleBeams = true, NeedleBeamRange = 11 };

    /// <summary>The same profile with flight operations following the ship's bays.</summary>
    internal static RulesProfile WithBayRates { get; } =
        Invented with { CarrierRatesFollowBays = true, CarrierTurnaroundRoll = true };

    /// <summary>The same profile with the damage track sized by the hull's class band.</summary>
    internal static RulesProfile WithRowsByClass { get; } =
        Invented with { ThresholdRows = ThresholdRowMode.ByShipClass };
}
