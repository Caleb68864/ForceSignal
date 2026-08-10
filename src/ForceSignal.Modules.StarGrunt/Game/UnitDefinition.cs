using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
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

    /// <summary>The die its leader throws for a confidence test.</summary>
    public QualityDie LeadershipDie { get; init; } = QualityDie.D8;

    /// <summary>The figures in it, at full strength.</summary>
    public ImmutableArray<FigureProfile> Figures { get; init; } = [];

    /// <summary>What it is carrying.</summary>
    public ImmutableArray<WeaponProfile> Weapons { get; init; } = [];

    /// <summary>How many figures it has when nothing has happened to it yet.</summary>
    public int FullStrength => Figures.Length;
}
