using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Morale;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Game;

/// <summary>One figure in a unit, and what it is wearing.</summary>
/// <param name="ArmourDie">
/// The figure's personal armour die, before any cover. Entered by the user from their own records.
/// </param>
/// <remarks>
/// Figures are listed rather than counted because they do not have to be alike: a squad may mix
/// armour, and a casualty comes off a particular figure rather than off a tally.
/// </remarks>
public readonly record struct FigureProfile(QualityDie ArmourDie);

/// <summary>
/// A weapon as it appears on a unit's record card.
/// </summary>
/// <remarks>
/// Every die here is a user input. The engine ships no weapon table, which is why this record has
/// somewhere to put an impact die and nowhere to look one up.
/// </remarks>
public sealed record WeaponProfile
{
    /// <summary>What the card calls it.</summary>
    public required string Name { get; init; }

    /// <summary>The die rolled once per potential hit, against the target's armour.</summary>
    public required QualityDie ImpactDie { get; init; }

    /// <summary>True when this is a support weapon, adding its own die to a volley.</summary>
    public bool IsSupport { get; init; }

    /// <summary>
    /// The die this weapon adds to the squad's volley when it is folded into small-arms fire.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ImpactDie"/> and not derived from it. A support weapon folded into
    /// squad fire adds <em>weight</em>, never its own heavier impact - so it contributes this die to
    /// the roll while every hit is still resolved on the small arms. Both numbers come off the
    /// user's own record card.
    /// </remarks>
    public QualityDie SupportFirepowerDie { get; init; } = QualityDie.D6;

    /// <summary>
    /// True when this weapon always needs an action of its own and can never be folded in.
    /// </summary>
    /// <remarks>
    /// A property of the weapon on the user's card rather than a list of names here, because naming
    /// the weapons the rule applies to would be shipping part of their weapon table.
    /// </remarks>
    public bool NeverJoinsSquadFire { get; init; }

    /// <summary>True when the weapon is effective only inside one range band.</summary>
    public bool IsCloseRange { get; init; }
}

/// <summary>
/// What a unit is: the part of it that does not change as the game is played.
/// </summary>
/// <remarks>
/// Split from <see cref="UnitStatus"/> because the two have different lifetimes. This is transcribed
/// once off a record card and then read; the status changes every time somebody shoots. Keeping them
/// apart means a force can be exported, re-imported and played again without carrying last game's
/// casualties with it.
/// </remarks>
public sealed record UnitDefinition
{
    /// <summary>How the sequence layer names this unit.</summary>
    public required UnitId Id { get; init; }

    /// <summary>What the players call it.</summary>
    public required string Name { get; init; }

    /// <summary>Which side it belongs to.</summary>
    public required SideId Side { get; init; }

    /// <summary>Where it sits in the chain of command.</summary>
    public CommandLevel Level { get; init; } = CommandLevel.Squad;

    /// <summary>The unit's quality die, which also sets its range band.</summary>
    public QualityDie QualityDie { get; init; } = QualityDie.D8;

    /// <summary>
    /// The leader's Leadership Value, from 1 to 3, where 1 is the best.
    /// </summary>
    /// <remarks>
    /// A number rather than a die, which is what the rules use and what every roll against a leader
    /// has to beat. It was modelled as a die at first and that was simply wrong: nothing in the game
    /// ever rolls a leadership die, and the suppression and rally engines both take a value.
    /// </remarks>
    public int LeadershipValue { get; init; } = 2;

    /// <summary>
    /// How worn the unit is, which caps where its confidence starts and how far it can be rallied.
    /// </summary>
    /// <remarks>
    /// A scenario property rather than something that changes mid-game, so it sits with the
    /// definition. Added because rallying is wrong without it: a tired unit cannot be lifted past
    /// Steady however well it rolls.
    /// </remarks>
    public FatigueLevel Fatigue { get; init; } = FatigueLevel.Fresh;

    /// <summary>The figures in it, at full strength.</summary>
    public ImmutableArray<FigureProfile> Figures { get; init; } = [];

    /// <summary>What it is carrying.</summary>
    public ImmutableArray<WeaponProfile> Weapons { get; init; } = [];

    /// <summary>How many figures it has when nothing has happened to it yet.</summary>
    public int FullStrength => Figures.Length;

    /// <inheritdoc />
    /// <remarks>
    /// Written by hand because the two collections above are <see cref="ImmutableArray{T}"/>, which
    /// compares by reference: the generated record equality would be reference equality in a
    /// record's clothes, and the restore-fidelity assertion this whole design rests on would be
    /// guaranteed to fail. See <see cref="StructuralEquality"/>.
    /// </remarks>
    public bool Equals(UnitDefinition? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && Side == other.Side
        && Level == other.Level
        && QualityDie == other.QualityDie
        && LeadershipValue == other.LeadershipValue
        && Fatigue == other.Fatigue
        && StructuralEquality.Sequence(Figures, other.Figures)
        && StructuralEquality.Sequence(Weapons, other.Weapons);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        Id,
        Name,
        Side,
        Level,
        QualityDie,
        HashCode.Combine(LeadershipValue, Fatigue),
        StructuralEquality.SequenceHash(Figures),
        StructuralEquality.SequenceHash(Weapons));
}
