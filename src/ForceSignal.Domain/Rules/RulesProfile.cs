using System.Collections.Immutable;

namespace ForceSignal.Domain.Rules;

/// <summary>
/// How a hull's damage track is divided into rows, which decides how many threshold checks a ship
/// faces on its way to being destroyed.
/// </summary>
public enum ThresholdRowMode
{
    /// <summary>The same number of rows for every hull, whatever its size.</summary>
    FixedRows,

    /// <summary>Rows by size band, so a smaller hull faces fewer checks before it dies.</summary>
    ByShipClass
}

/// <summary>What one beam die scores against one level of screening.</summary>
/// <param name="DieFace">The face rolled.</param>
/// <param name="ScreenLevel">The target's screen level.</param>
/// <param name="Damage">Points that face scores through that screening.</param>
public sealed record BeamDamageEntry(int DieFace, int ScreenLevel, int Damage);

/// <summary>What one point-defence die shoots down.</summary>
/// <param name="DieFace">The face rolled.</param>
/// <param name="Kills">How many incoming it accounts for.</param>
public sealed record PointDefenseEntry(int DieFace, int Kills);

/// <summary>What a landed fighter group's turnaround roll means for it.</summary>
/// <param name="DieFace">The face rolled.</param>
/// <param name="IsGroundedForGame">True when the group does not fly again this game.</param>
/// <param name="TurnsBeforeRelaunch">Turns the deck crews need before it can go out again.</param>
public sealed record TurnaroundEntry(int DieFace, bool IsGroundedForGame, int TurnsBeforeRelaunch);

/// <summary>
/// Every number a Full Thrust match is played against, gathered in one place and supplied by the
/// player rather than shipped with this app.
/// </summary>
/// <remarks>
/// This type used to carry two ready-made profiles with the published numbers written into them,
/// and the rest of the numbers were scattered through the rule classes as constants. Both were the
/// same mistake: the engine owns the <em>procedures</em> - what is rolled against what, what order
/// things happen in, what a screen does to a die - and the player owns every <em>number</em> those
/// procedures read, off their own rulebook and record cards.
///
/// So there is no default here and deliberately no <c>Parse</c> that conjures one from a name. A
/// match carries a profile its players filled in, or it cannot be played. <see cref="Empty"/> is
/// blank rather than typical, and <see cref="Validate"/> says what is still missing.
/// </remarks>
/// <param name="Name">What the players call this set of numbers.</param>
/// <param name="DieFaces">Faces on the die the whole game is rolled with.</param>
/// <param name="BeamDamage">What a beam die scores at each face and screen level.</param>
/// <param name="BeamRangeBandWidth">Range band a beam loses a die per, in mu.</param>
/// <param name="MaxScreenLevel">Highest screen level a ship may carry.</param>
/// <param name="TorpedoMaximumRange">Longest reach of a pulse torpedo, in mu.</param>
/// <param name="TorpedoBandWidth">Range band a torpedo's to-hit number worsens per, in mu.</param>
/// <param name="TorpedoBestToHit">To-hit number in the closest band.</param>
/// <param name="NeedleBeamRange">Reach of a needle beam that declares no range of its own, in mu.</param>
/// <param name="NeedleSystemKillRoll">Roll that knocks the nominated system out.</param>
/// <param name="EnhancedNeedleBeams">True when a needle also puts a point into the hull.</param>
/// <param name="NeedleHullDamageRoll">Roll an enhanced needle needs to draw blood.</param>
/// <param name="ThresholdRows">Whether the damage track is a fixed number of rows or sized by band.</param>
/// <param name="ThresholdRowCount">Rows in the track when it is a fixed number.</param>
/// <param name="EscortRowCount">Rows an escort's track is drawn in, when sized by band.</param>
/// <param name="CruiserRowCount">Rows a cruiser's track is drawn in, when sized by band.</param>
/// <param name="MaxPartiesPerJob">Damage control parties that can usefully crowd one job.</param>
/// <param name="RepairRollWithOneParty">Roll one party needs to bring a system back.</param>
/// <param name="RepairBestRoll">Best roll any number of parties can bring the job down to.</param>
/// <param name="FighterMoveAllowance">How far a fighter group flies in a turn, in mu.</param>
/// <param name="CarrierRatesFollowBays">True when flight operations follow the ship's bays.</param>
/// <param name="TrueCarrierAllowance">Groups a true carrier works in a turn, when they do not.</param>
/// <param name="OtherShipAllowance">Groups any other ship with a bay works, when they do not.</param>
/// <param name="CarrierTurnaroundRoll">True when a landed group rolls before it may go out again.</param>
/// <param name="Turnaround">What each turnaround face means.</param>
/// <param name="PointDefenseRange">How far point defence reaches, in mu.</param>
/// <param name="PointDefenseKills">What each point-defence face shoots down.</param>
/// <param name="PointDefenseChainOnFace">Face that earns another die, or zero when none does.</param>
/// <param name="MissilesPerSalvo">Missiles in one salvo.</param>
/// <param name="SalvoAttackRadius">How close a target must be to the point of aim, in mu.</param>
public sealed record RulesProfile(
    string Name,
    int DieFaces,
    ImmutableArray<BeamDamageEntry> BeamDamage,
    int BeamRangeBandWidth,
    int MaxScreenLevel,
    int TorpedoMaximumRange,
    int TorpedoBandWidth,
    int TorpedoBestToHit,
    int NeedleBeamRange,
    int NeedleSystemKillRoll,
    bool EnhancedNeedleBeams,
    int NeedleHullDamageRoll,
    ThresholdRowMode ThresholdRows,
    int ThresholdRowCount,
    int EscortRowCount,
    int CruiserRowCount,
    int MaxPartiesPerJob,
    int RepairRollWithOneParty,
    int RepairBestRoll,
    int FighterMoveAllowance,
    bool CarrierRatesFollowBays,
    int TrueCarrierAllowance,
    int OtherShipAllowance,
    bool CarrierTurnaroundRoll,
    ImmutableArray<TurnaroundEntry> Turnaround,
    int PointDefenseRange,
    ImmutableArray<PointDefenseEntry> PointDefenseKills,
    int PointDefenseChainOnFace,
    int MissilesPerSalvo,
    int SalvoAttackRadius)
{
    /// <summary>
    /// A profile with nothing filled in. The starting point for a player entering their own numbers,
    /// and what an old saved match restores with until somebody supplies one.
    /// </summary>
    public static RulesProfile Empty { get; } = new(
        Name: string.Empty,
        DieFaces: 0,
        BeamDamage: [],
        BeamRangeBandWidth: 0,
        MaxScreenLevel: 0,
        TorpedoMaximumRange: 0,
        TorpedoBandWidth: 0,
        TorpedoBestToHit: 0,
        NeedleBeamRange: 0,
        NeedleSystemKillRoll: 0,
        EnhancedNeedleBeams: false,
        NeedleHullDamageRoll: 0,
        ThresholdRows: ThresholdRowMode.FixedRows,
        ThresholdRowCount: 0,
        EscortRowCount: 0,
        CruiserRowCount: 0,
        MaxPartiesPerJob: 0,
        RepairRollWithOneParty: 0,
        RepairBestRoll: 0,
        FighterMoveAllowance: 0,
        CarrierRatesFollowBays: false,
        TrueCarrierAllowance: 0,
        OtherShipAllowance: 0,
        CarrierTurnaroundRoll: false,
        Turnaround: [],
        PointDefenseRange: 0,
        PointDefenseKills: [],
        PointDefenseChainOnFace: 0,
        MissilesPerSalvo: 0,
        SalvoAttackRadius: 0);

    /// <summary>True when this profile has enough in it to play a match against.</summary>
    public bool IsPlayable => Validate().Count == 0;

    /// <summary>
    /// The same profile with every table guaranteed to be a real empty array rather than a default
    /// one.
    /// </summary>
    /// <remarks>
    /// A JSON body that leaves a table out deserialises it as <c>default(ImmutableArray&lt;T&gt;)</c>,
    /// which is not an empty array - it is a null-ish struct that throws the moment anything
    /// enumerates or serialises it. Sending a profile with no turnaround table therefore blew up on
    /// the way back out rather than on the way in, which is a miserable way to find out. Every
    /// profile crossing into the app goes through here.
    /// </remarks>
    public RulesProfile Normalized() => this with
    {
        Name = Name ?? string.Empty,
        BeamDamage = BeamDamage.IsDefault ? [] : BeamDamage,
        Turnaround = Turnaround.IsDefault ? [] : Turnaround,
        PointDefenseKills = PointDefenseKills.IsDefault ? [] : PointDefenseKills,
    };

    /// <summary>
    /// What is still missing before a match can be played against this profile.
    /// </summary>
    /// <returns>One line per gap, empty when the profile is complete.</returns>
    /// <remarks>
    /// Only the numbers every match needs are required. A table that has no salvo missiles in it
    /// does not have to describe them, so ordnance, needles and carrier operations are checked only
    /// once their own numbers are partly filled in - a half-entered section is a mistake, an empty
    /// one is a choice.
    /// </remarks>
    public IReadOnlyList<string> Validate()
    {
        var gaps = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            gaps.Add("The profile needs a name, so you can tell it from the next one.");
        }

        if (DieFaces < 2)
        {
            gaps.Add("Say how many faces the die has.");
        }

        if (BeamDamage.IsDefaultOrEmpty)
        {
            gaps.Add("Fill in what a beam die scores at each face and screen level.");
        }

        if (BeamRangeBandWidth <= 0)
        {
            gaps.Add("Say how wide a beam's range band is.");
        }

        if (ThresholdRowCount <= 1)
        {
            gaps.Add("Say how many rows a hull's damage track is drawn in.");
        }

        if (ThresholdRows == ThresholdRowMode.ByShipClass && (EscortRowCount <= 0 || CruiserRowCount <= 0))
        {
            gaps.Add("Sizing the track by class needs a row count for an escort and for a cruiser.");
        }

        if (MaxPartiesPerJob > 0 && (RepairRollWithOneParty <= 0 || RepairBestRoll <= 0))
        {
            gaps.Add("Damage control needs the roll one party makes and the best it can get to.");
        }

        if (PointDefenseRange > 0 && PointDefenseKills.IsDefaultOrEmpty)
        {
            gaps.Add("Point defence has a range but nothing saying what its dice shoot down.");
        }

        if (MissilesPerSalvo > 0 && SalvoAttackRadius <= 0)
        {
            gaps.Add("A salvo needs to know how close its target must be to the point of aim.");
        }

        if (EnhancedNeedleBeams && NeedleHullDamageRoll <= 0)
        {
            gaps.Add("An enhanced needle needs the roll it draws blood on.");
        }

        if (CarrierTurnaroundRoll && Turnaround.IsDefaultOrEmpty)
        {
            gaps.Add("Turnaround is rolled for but nothing says what the faces mean.");
        }

        // A table row for a face the die cannot show is a slip in the transcription, not a choice,
        // and one that fails quietly: the row is never matched, so the die scores nothing and the
        // table looks merely unlucky. Say so while the numbers are still on screen.
        if (DieFaces >= 2)
        {
            if (!BeamDamage.IsDefaultOrEmpty && BeamDamage.Any(entry => entry.DieFace < 1 || entry.DieFace > DieFaces))
            {
                gaps.Add($"A beam damage entry names a face the {DieFaces}-sided die does not have.");
            }

            if (!PointDefenseKills.IsDefaultOrEmpty && PointDefenseKills.Any(entry => entry.DieFace < 1 || entry.DieFace > DieFaces))
            {
                gaps.Add($"A point defence entry names a face the {DieFaces}-sided die does not have.");
            }

            if (!Turnaround.IsDefaultOrEmpty && Turnaround.Any(entry => entry.DieFace < 1 || entry.DieFace > DieFaces))
            {
                gaps.Add($"A turnaround entry names a face the {DieFaces}-sided die does not have.");
            }
        }

        return gaps;
    }

    /// <summary>
    /// What a beam die scores at this face against this much screening. A combination the profile
    /// does not mention scores nothing, so a player only has to enter the faces that do something.
    /// </summary>
    /// <param name="dieFace">The face rolled.</param>
    /// <param name="screenLevel">The target's screen level.</param>
    public int BeamDamageFor(int dieFace, int screenLevel)
    {
        var screens = Math.Clamp(screenLevel, 0, Math.Max(0, MaxScreenLevel));
        foreach (var entry in BeamDamage.IsDefault ? [] : BeamDamage)
        {
            if (entry.DieFace == dieFace && entry.ScreenLevel == screens)
            {
                return Math.Max(0, entry.Damage);
            }
        }

        return 0;
    }

    /// <summary>How many incoming one point-defence face accounts for.</summary>
    /// <param name="dieFace">The face rolled.</param>
    public int PointDefenseKillsFor(int dieFace)
    {
        foreach (var entry in PointDefenseKills.IsDefault ? [] : PointDefenseKills)
        {
            if (entry.DieFace == dieFace)
            {
                return Math.Max(0, entry.Kills);
            }
        }

        return 0;
    }

    /// <summary>
    /// What a turnaround face means. A face the profile does not mention leaves the group flyable
    /// and ready next turn, which is the least this roll can do to it.
    /// </summary>
    /// <param name="dieFace">The face rolled.</param>
    public TurnaroundEntry TurnaroundFor(int dieFace)
    {
        foreach (var entry in Turnaround.IsDefault ? [] : Turnaround)
        {
            if (entry.DieFace == dieFace)
            {
                return entry;
            }
        }

        return new TurnaroundEntry(dieFace, IsGroundedForGame: false, TurnsBeforeRelaunch: 1);
    }

    /// <summary>Compares two profiles by their contents, tables included.</summary>
    /// <param name="other">The profile to compare against.</param>
    public bool Equals(RulesProfile? other) =>
        other is not null
        && Name == other.Name
        && DieFaces == other.DieFaces
        && BeamRangeBandWidth == other.BeamRangeBandWidth
        && MaxScreenLevel == other.MaxScreenLevel
        && TorpedoMaximumRange == other.TorpedoMaximumRange
        && TorpedoBandWidth == other.TorpedoBandWidth
        && TorpedoBestToHit == other.TorpedoBestToHit
        && NeedleBeamRange == other.NeedleBeamRange
        && NeedleSystemKillRoll == other.NeedleSystemKillRoll
        && EnhancedNeedleBeams == other.EnhancedNeedleBeams
        && NeedleHullDamageRoll == other.NeedleHullDamageRoll
        && ThresholdRows == other.ThresholdRows
        && ThresholdRowCount == other.ThresholdRowCount
        && EscortRowCount == other.EscortRowCount
        && CruiserRowCount == other.CruiserRowCount
        && MaxPartiesPerJob == other.MaxPartiesPerJob
        && RepairRollWithOneParty == other.RepairRollWithOneParty
        && RepairBestRoll == other.RepairBestRoll
        && FighterMoveAllowance == other.FighterMoveAllowance
        && CarrierRatesFollowBays == other.CarrierRatesFollowBays
        && TrueCarrierAllowance == other.TrueCarrierAllowance
        && OtherShipAllowance == other.OtherShipAllowance
        && CarrierTurnaroundRoll == other.CarrierTurnaroundRoll
        && PointDefenseRange == other.PointDefenseRange
        && PointDefenseChainOnFace == other.PointDefenseChainOnFace
        && MissilesPerSalvo == other.MissilesPerSalvo
        && SalvoAttackRadius == other.SalvoAttackRadius
        && Same(BeamDamage, other.BeamDamage)
        && Same(Turnaround, other.Turnaround)
        && Same(PointDefenseKills, other.PointDefenseKills);

    /// <summary>Hashes a profile by its contents, tables included.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(DieFaces);
        hash.Add(BeamRangeBandWidth);
        hash.Add(MaxScreenLevel);
        hash.Add(ThresholdRowCount);
        hash.Add(MissilesPerSalvo);
        foreach (var entry in BeamDamage.IsDefault ? [] : BeamDamage)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Compares two tables element by element.
    /// </summary>
    /// <remarks>
    /// An <see cref="ImmutableArray{T}"/> compares by reference, so two profiles built from the same
    /// numbers would otherwise come out unequal - which has bitten this codebase before.
    /// </remarks>
    private static bool Same<T>(ImmutableArray<T> left, ImmutableArray<T> right) =>
        (left.IsDefaultOrEmpty && right.IsDefaultOrEmpty)
        || (!left.IsDefault && !right.IsDefault && left.SequenceEqual(right));
}
